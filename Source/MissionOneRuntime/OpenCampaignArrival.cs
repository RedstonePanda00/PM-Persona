using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PMP_Persona;
using PMP_Persona.GestaltZero;
using PMP_Persona.PrismSupport;
using PMP_Persona.ShipSupport;
using PMP_Persona.VanguardSupport;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using Verse;

namespace PMP.Campaign {
    [StaticConstructorOnStartup]
    static class OpenCampaignActorTransfer {
        static OpenCampaignActorTransfer() {
            CampaignController.NodeInitialized += c => {
                if (c.Project?.Id == "pmp-mission-one" && c.NodeId == "open-campaign")
                    // Run before the entry checkpoint: a pooled actor must not be
                    // serialized both by the campaign and by the resurrection pool.
                    Current.Game.GetComponent<GameComponent_GestaltZeroReturn>().PrepareCampaignArrival(c.Actors["mason"]);
            };
        }
    }

    [CampaignModule("pmp.open.prepare", "Action", "Prepare the central Romulus outpost on the player's chosen native colony map.")]
    public sealed class OpenCampaignPrepare : IActionModule {
        public bool Execute(Act a, IHost host, string source, double delta) {
            var c = (CampaignController)host;
            if (c.NodeId != "open-campaign") throw new InvalidOperationException("Wrong node for open arrival.");
            if (c.State.Variables.ContainsKey(a.Id)) return true;
            var map = c.Map;
            // Only the authored outpost footprint is flattened; the selected biome remains outside it.
            foreach (var cell in CellRect.CenteredOn(map.Center, 20)) {
                map.roofGrid.SetRoof(cell, null);
                foreach (var thing in cell.GetThingList(map).ToList())
                    if (thing is Building || thing is Plant || thing is Filth) {
                        if (thing.def.destroyable) thing.Destroy(DestroyMode.Vanish);
                        else thing.DeSpawn(); // Native geysers cannot be destroyed.
                    }
                map.terrainGrid.SetTerrain(cell, TerrainDefOf.Concrete);
            }
            foreach (var p in c.MapSpec.Objects.Where(p => p.Group == "open-outpost")) c.Place(p, map);
            var faction = Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec, "Hostile");
            Current.Game.GetComponent<PMP_Persona.Romulus.LegionFactionRegistry>().Register(faction);
            c.State.Variables[a.Id] = 1;
            return true;
        }
    }

    [CampaignModule("pmp.open.bombard", "Action", "Bombard the central outpost with existing orbital weapons on the native simulation clock.")]
    public sealed class OpenCampaignBombardment : IActionModule {
        public bool Execute(Act a, IHost host, string source, double delta) {
            var c = (CampaignController)host; var map = c.Map; var v = c.State.Variables;
            const string key = "open-bombard";
            if (!v.TryGetValue(key, out var start)) v[key] = start = Find.TickManager.TicksGame;
            int age = Find.TickManager.TicksGame - (int)start;
            var offsets = new[] { new IntVec3(-9,0,-9), new IntVec3(9,0,9), new IntVec3(-9,0,9), new IntVec3(9,0,-9), IntVec3.Zero };
            var config = DefDatabase<ShipSupportDef>.GetNamed("PMP_Support_Prism").GetModExtension<OrbitalSupportConfig>();
            for (int i = (int)v.GetValueOrDefault(key + ":shots"); i < 25 && age >= i * 12; i++) {
                var at = map.Center + offsets[i % offsets.Length];
                if (i < 5) {
                    var fx = AccessTools.Method(typeof(PrismFractureEffects), "Get").Invoke(null, new object[] { map });
                    AccessTools.Method(typeof(PrismFractureEffects), "Add").Invoke(fx, new object[] { at, config });
                } else {
                    bool light = i % 4 == 0;
                    var incoming = PMPH_OrbitalIncoming.Spawn(ThingDef.Named(light ? "PMP_PrismLightParticleIncoming" : "PMP_PrismHeavyParticleIncoming"), at, map);
                    AccessTools.Field(typeof(PMPH_OrbitalIncoming), "VanguardWeapon").SetValue(incoming, light ? 2 : 1);
                    c.Objects[key + ":shot:" + i] = incoming;
                }
                v[key + ":shots"] = i + 1;
            }
            if (age >= 90 && !v.ContainsKey(key + ":beams")) {
                for (int i = 0; i < offsets.Length; i++) {
                    var beam = ThingMaker.MakeThing(ThingDef.Named("PMP_PurificationBeam"));
                    beam.SetFaction(Faction.OfPlayer);
                    GenSpawn.Spawn(beam, map.Center + offsets[i], map);
                    c.Objects[key + ":beam:" + i] = beam;
                }
                v[key + ":beams"] = 1;
            }
            if (age < 540 || c.Objects.Any(p => p.Key.StartsWith(key + ":shot:") && p.Value?.Spawned == true)) return false;
            if (v.ContainsKey(key + ":done")) return true;
            foreach (var pair in c.Objects.Where(p => p.Key.StartsWith(key + ":beam:")))
                if (pair.Value?.Spawned == true) pair.Value.Destroy(DestroyMode.Vanish);
            // Resolve the remaining fortifications as destroyed, and extinguish the landing zone.
            foreach (var cell in CellRect.CenteredOn(map.Center, 20)) {
                map.roofGrid.SetRoof(cell, null);
                foreach (var thing in cell.GetThingList(map).ToList()) {
                    if (thing is Building) {
                        if (thing.def.destroyable) thing.Destroy(DestroyMode.KillFinalize);
                        else thing.DeSpawn();
                    }
                    else if (thing is Fire || thing is Plant) thing.Destroy(DestroyMode.Vanish);
                }
                if ((cell - map.Center).LengthHorizontalSquared < 19 * 19)
                    FilthMaker.TryMakeFilth(cell, map, ThingDefOf.Filth_Ash);
            }
            v[key + ":done"] = 1;
            return true;
        }
    }

    [CampaignModule("pmp.open.ready", "Action", "Release the landed expedition for ordinary colony play.")]
    public sealed class OpenCampaignReady : IActionModule {
        public bool Execute(Act a, IHost host, string source, double delta) {
            var c = (CampaignController)host;
            foreach (var pawn in c.Map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == Faction.OfPlayer)) {
                if (pawn.drafter != null) pawn.drafter.Drafted = false;
                if (pawn.workSettings != null) {
                    pawn.workSettings.EnableAndInitialize();
                    if (pawn.kindDef.defName == "PM_Probe") {
                        pawn.workSettings.SetPriority(WorkTypeDefOf.Construction, 1);
                        pawn.workSettings.SetPriority(WorkTypeDefOf.Mining, 2);
                        pawn.workSettings.SetPriority(WorkTypeDefOf.Hauling, 3);
                        pawn.equipment?.Primary?.TryGetComp<CompRefuelable>()?.Refuel(100);
                    }
                }
            }
            foreach (var cell in CellRect.CenteredOn(c.Map.Center, 18)) c.Map.areaManager.Home[cell] = true;
            c.State.Variables["open-ready"] = 1;
            return true;
        }
    }
}
