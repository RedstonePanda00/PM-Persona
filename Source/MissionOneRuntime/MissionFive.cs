using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using ProtossMech;
using PMP_Persona.Romulus;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI.Group;

namespace PMP.Campaign {
    // Opt-in story map component. Native deep-resource and research state are saved
    // by the game; initialization never runs again when loading a mid-mission save.
    public sealed partial class MissionFiveMapComponent : CustomMapComponent, ICampaignMapModule, ICampaignMapOverlay {
        bool enabled, configured, showOre = true;
        int elapsedTicks, enemyCap = 80;
        double holdSeconds = 900;
        Dictionary<string, float> previousResearch = new Dictionary<string, float>();
        CampaignController Controller => Current.Game?.GetComponent<CampaignController>();
        public bool Active => enabled && MissionScope.Owns(Controller, map, "mission-five");
        public double Seconds => elapsedTicks / 60d;
        public double HoldSeconds => holdSeconds;
        public int EnemyCap => enemyCap;
        public bool Ready => Active && Controller.State.Variables.GetValueOrDefault("m5-ready") == 1;
        public bool Extracting => Controller.State.Variables.GetValueOrDefault("m5-ending") == 1;
        public MissionFiveMapComponent(Map map) : base(map) { }
        public static MissionFiveMapComponent For(Map map) {
            var component = map?.GetComponent<MissionFiveMapComponent>();
            return component?.Active == true ? component : null;
        }
        public void Attach(CampaignController c, MapComponentSpec spec) {
            enabled = spec.Enabled;
            double Setting(string key, double fallback) => spec.Settings.TryGetValue(key, out var value) ? double.Parse(value, CultureInfo.InvariantCulture) : fallback;
            holdSeconds = Setting("holdSeconds", 900); enemyCap = (int)Setting("enemyCap", 80);
            if (holdSeconds <= 0 || enemyCap < 1) throw new InvalidOperationException("Invalid mission-five duration or enemy cap.");
            Current.Game.GetComponent<LegionFactionRegistry>().Register(Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec, "Hostile"));
            var ore = CellRect.FromLimits((int)Setting("oreMinX",150),(int)Setting("oreMinZ",124),(int)Setting("oreMaxX",173),(int)Setting("oreMaxZ",131));
            int perCell = ThingDefOf.Steel.deepCountPerCell;
            if (ore.Area<=0 || !ore.InBounds(map) || perCell < 1 || perCell > ushort.MaxValue) throw new InvalidOperationException("Invalid steel deposit settings.");
            // Exclude complete footprints with an extra four-cell safety margin.
            var excluded = new HashSet<IntVec3>();
            foreach (var p in c.MapSpec.Objects.Where(p => !p.SpawnAtStart && p.Faction == "Player" && p.Kind == "Thing" && p.Group!="m5-drills")) {
                var def = DefDatabase<ThingDef>.GetNamed(p.Def.Name);
                if(def.category!=ThingCategory.Building)continue;
                foreach (var cell in GenAdj.OccupiedRect(new IntVec3(p.Position.X, 0, p.Position.Z), new Rot4(p.Rotation), def.size).ExpandedBy(4)) excluded.Add(cell);
            }
            int total = 0;
            foreach (var cell in ore) {
                if(excluded.Contains(cell))throw new InvalidOperationException("Steel deposit overlaps opening base buildings.");
                map.deepResourceGrid.SetAt(cell, ThingDefOf.Steel, perCell); total += perCell;
            }
            if (total < 7500) throw new InvalidOperationException("Mission-five steel deposit must contain at least 7500 steel.");
            c.State.Variables["m5-initial-steel"] = total;
            foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p => p.tab?.defName == "ProtossTech")) {
                previousResearch[project.defName] = Find.ResearchManager.GetProgress(project);
                if (!project.IsFinished) Find.ResearchManager.FinishProject(project, false, null, false);
            }
        }
        public void ConfigureArrival() {
            if (configured) return;
            var c = Controller;
            var storage = c.Objects["m5-storage-0"].TryGetComp<CompHyperspaceStorage>();
            // PMC's assignment API is internal; call its normal validation/notification path.
            bool assigned = (bool)AccessTools.Method(typeof(CompHyperspaceStorage), "TryAssign").Invoke(storage, new object[] { ThingDefOf.Steel, -1 });
            if (!assigned) throw new InvalidOperationException("Could not assign opening steel storage.");
            var replicator = c.Objects["m5-replicator"].TryGetComp<CompReplicator>();
            if (!replicator.Enabled) replicator.CompGetGizmosExtra().OfType<Command_Toggle>().Single().toggleAction();
            int workerIndex = 0;
            foreach (var p in c.MapSpec.Objects.Where(p => p.Group == "m5-probes")) {
                var pawn = (Pawn)c.Objects[p.Id];
                if (pawn.equipment?.Primary?.def.defName != "PM_MiningDrill") throw new InvalidOperationException("Probe arrived without mining drill: " + p.Id);
                pawn.equipment.Primary.TryGetComp<CompRefuelable>().Refuel(100);
                pawn.drafter.Drafted = false;
                if (pawn.workSettings != null) {
                    pawn.workSettings.EnableAndInitialize();
                    pawn.workSettings.SetPriority(WorkTypeDefOf.Construction, 1);
                    pawn.workSettings.SetPriority(WorkTypeDefOf.Mining, workerIndex >= 6 ? 3 : 2);
                    pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling, workerIndex >= 6 ? 2 : 3);
                }
                workerIndex++;
            }
            // The colony is a construction mission: enable repairs throughout its basin.
            foreach (var cell in map.AllCells.Where(cell => cell.DistanceToSquared(map.Center) <= 77 * 77)) map.areaManager.Home[cell] = true;
            configured = true;
        }
        public override void MapComponentTick() {
            if (Active && Extracting) TickFinale();
            if (!Ready || Controller.Transitioning || Controller.EntryPending || Controller.State.Phase != "Running" || Controller.Freeze || Extracting) return;
            elapsedTicks++;
            if (elapsedTicks % 60 != 0) return;
            var v = Controller.State.Variables;
            CheckFirstContacts();
            v["m5-seconds"] = Seconds;
            // Strategic attraction score; never used as a Pawn spawn budget.
            v["m5-attracted"] = Math.Floor(Math.Max(0, Seconds - 180) * (20 + Seconds / 30));
            if (Seconds >= holdSeconds) {
                Controller.State.Objectives["m5-hold"] = "Complete";
                return; // Once earned, extraction stays available even if the base falls.
            }
            bool probe = map.mapPawns.AllPawnsSpawned.Any(p => p.Faction == Faction.OfPlayer && !p.Dead && p.def.defName == "PM_Probe");
            bool replicator = map.listerBuildings.allBuildingsColonist.Any(b => b.def.defName == "PM_Replicator");
            if (!probe && !replicator && v.GetValueOrDefault("m5-failure-queued") == 0) {
                v["m5-failure-queued"] = 1;
                Controller.QueueStory(new[] { new Act { Kind = "Fail", Id = "m5-base-lost", Text = "前哨已失守：复制器和探机全部损毁。" } });
            }
        }
        public bool BeginExtraction() {
            var c = Controller;
            if (!Ready || Seconds < holdSeconds || Extracting || c.State.Phase != "Running" || c.Presentation || !c.CanFinishStory) return false;
            c.State.Variables["m5-ending"] = 1;
            c.QueueStory(c.MapSpec.Groups.Single(g => g.Id == "m5-extraction").Actions);
            return true;
        }
        public void DrawCampaignOverlay() {
            if (!Active || Find.CurrentMap != map || Controller.EntryPending || Controller.Transitioning) return;
            if (briefing.Visible) { briefing.Draw(Controller); return; }
            if (showOre) map.deepResourceGrid.MarkForDraw();
            if (!Ready || Controller.Presentation || Controller.State.Phase != "Running") return;
            if (Seconds < 24) DrawEntrances();
            var old = Text.Font; Text.Font = GameFont.Small;
            float width = Math.Min(330, UI.screenWidth - 32);
            float left = UI.screenWidth - width - 16;
            var rect = new Rect(left, 82, width, 191);
            Widgets.DrawWindowBackground(rect);
            string clock = TimeSpan.FromSeconds(Math.Max(0, holdSeconds - Seconds)).ToString(@"mm\:ss");
            Widgets.Label(new Rect(left+12, 92, width - 24, 28), Seconds < holdSeconds ? "PMP_M5_Remaining".Translate(clock) : "PMP_M5_Ready".Translate());
            Widgets.Label(new Rect(left+12, 120, width - 24, 25), "PMP_M5_Kills".Translate((int)Controller.State.Variables.GetValueOrDefault("m5-kills")));
            var attractedRect = new Rect(left+12, 145, width - 24, 25);
            Widgets.Label(attractedRect, "PMP_M5_Attracted".Translate((long)Controller.State.Variables.GetValueOrDefault("m5-attracted")));
            TooltipHandler.TipRegion(attractedRect, "PMP_M5_AttractedTip".Translate());
            Widgets.CheckboxLabeled(new Rect(left+12, 173, width - 24, 25), "PMP_M5_ShowOre".Translate(), ref showOre);
            if (Seconds >= holdSeconds) {
                if (Widgets.ButtonText(new Rect(left+12, 212, width - 24, 36), "PMP_M5_Extract".Translate())) BeginExtraction();
            } else Widgets.Label(new Rect(left+12, 212, width - 24, 36), "PMP_M5_BuildHint".Translate());
            Text.Font = old;
        }
        public void RestoreResearch() {
            if (previousResearch == null || previousResearch.Count == 0 || Find.ResearchManager == null) return;
            var progress = (Dictionary<ResearchProjectDef, float>)AccessTools.Field(typeof(ResearchManager), "progress").GetValue(Find.ResearchManager);
            foreach (var pair in previousResearch) {
                var def = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(pair.Key);
                if (def != null) progress[def] = pair.Value;
            }
            previousResearch.Clear();
        }
        public void Detach() { briefing.End(); RestoreResearch(); enabled = false; }
        public void SetEnabled(bool value) { if (!value) Detach(); else enabled = true; }
        public override void MapRemoved() { Detach(); base.MapRemoved(); }
        public override void ExposeData() {
            base.ExposeData();
            Scribe_Values.Look(ref enabled, "enabled"); Scribe_Values.Look(ref configured, "configured");
            Scribe_Values.Look(ref elapsedTicks, "elapsedTicks"); Scribe_Values.Look(ref holdSeconds, "holdSeconds", 900d);
            Scribe_Values.Look(ref enemyCap, "enemyCap", 80); Scribe_Values.Look(ref showOre, "showOre", true);
            Scribe_Collections.Look(ref previousResearch, "previousResearch", LookMode.Value, LookMode.Value);
        }
    }
    [CampaignModule("pmp.m5.ready", "Action", "Configure the arrived probes and assign the empty steel cabinet once.")]
    public sealed class MissionFiveReady : IActionModule {
        public bool Execute(Act a, IHost host, string source, double delta) { MissionFiveMapComponent.For(((CampaignController)host).Map).ConfigureArrival(); return true; }
    }
    [CampaignModule("pmp.m5.wave", "Action", "Spawn a capped native assault from Target's authored pawn slots. Text is direction; Value is wave size. Defers until space is available.")]
    public sealed class MissionFiveWave : IActionModule {
        public bool Execute(Act a, IHost host, string source, double delta) {
            var c = (CampaignController)host; var component = MissionFiveMapComponent.For(c.Map);
            if (component == null || component.Extracting || c.State.Phase != "Running") return true;
            // Waiting fibers are polled at tick rate; scan only once per simulated second.
            string key = "m5-wave-poll:" + a.Id;
            int tick = Find.TickManager.TicksGame;
            if (c.State.Variables.TryGetValue(key, out var previous) && tick - previous < 60) return false;
            c.State.Variables[key] = tick;
            var faction = Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec, "Hostile");
            int count = Math.Max(1, (int)a.Value);
            if (c.Map.mapPawns.AllPawnsSpawned.Count(p => p.Faction == faction && !p.Dead) + count > component.EnemyCap) return false;
            int waveIndex=(int)c.State.Variables.GetValueOrDefault("m5-waves");
            string target=a.Target=="cycle"?"m5-endless-"+new[]{"sw","nw","se","ne"}[waveIndex%4]:a.Target;
            var slots = c.MapSpec.Objects.Where(p => p.Kind == "PawnKind" && p.Group == target)
                .Where(p => !c.Objects.TryGetValue(p.Id, out var old) || old is Pawn pawn && (pawn.Dead || pawn.Destroyed)).Take(count).ToList();
            if (slots.Count < count) return false;
            var newcomers = new List<Pawn>();
            foreach (var slot in slots) {
                var p = Json.Copy(slot);
                var at = CellFinder.StandableCellNear(new IntVec3(p.Position.X, 0, p.Position.Z), c.Map, 8);
                p.Position = new Cell { X = at.x, Z = at.z };
                var pawn = (Pawn)c.Place(p, c.Map); newcomers.Add(pawn);
                if (pawn.kindDef.defName == "PMP_LegionCenturion" && pawn.equipment?.Primary == null)
                    pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_CenturionMinigun")));
            }
            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, canKidnap: false, canTimeoutOrFlee: false, canSteal: false), c.Map, newcomers);
            c.State.Variables["m5-waves"] = c.State.Variables.GetValueOrDefault("m5-waves") + 1;
            component.AnnounceWave(waveIndex,newcomers);
            string direction=a.Target=="cycle"?new[]{"西南","西北","东南","东北"}[waveIndex%4]:a.Text;
            Messages.Message("PMP_M5_Wave".Translate(RcText.T(direction), newcomers.Count), new TargetInfo(newcomers[0].Position, c.Map), MessageTypeDefOf.ThreatSmall, false);
            return true;
        }
    }
    [CampaignModule("pmp.m5.finish", "Action", "Finish the fifth mission with time, kills and attraction score.")]
    public sealed class MissionFiveFinish : IActionModule {
        public bool Execute(Act a, IHost host, string source, double delta) {
            var c = (CampaignController)host; var component = MissionFiveMapComponent.For(c.Map);
            string summary = "PMP_M5_Result".Translate(TimeSpan.FromSeconds(component.Seconds).ToString(@"hh\:mm\:ss"), (int)c.State.Variables.GetValueOrDefault("m5-kills"), (long)c.State.Variables.GetValueOrDefault("m5-attracted"));
            c.FinishStory("", summary); return true;
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    static class MissionFiveKillCount {
        static void Prefix(Pawn __instance, out bool __state) {
            var component = MissionFiveMapComponent.For(__instance.Map);
            __state = component?.Ready == true && !component.Extracting && !__instance.Dead && LegionDoctrine.IsLegionFaction(__instance.Faction);
        }
        static void Postfix(Pawn __instance, bool __state) {
            if (!__state || !__instance.Dead) return;
            var v = Current.Game.GetComponent<CampaignController>().State.Variables;
            v["m5-kills"] = v.GetValueOrDefault("m5-kills") + 1;
        }
    }
}
