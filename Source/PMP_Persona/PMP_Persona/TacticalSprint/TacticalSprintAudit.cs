using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona.TacticalSprint
{
    // Opt-in native integration audit; ordinary games do not enter this code path.
    public sealed class TacticalSprintAudit : GameComponent
    {
        private int stage, phase, frames, started;
        private bool done, loading;
        private Pawn mason, openMason;
        private IntVec3 origin;
        private Thing obstacle;
        private IntVec3 Direction => phase == 3 || phase == 6 ? IntVec3.West : phase == 4 ? IntVec3.North : phase == 5 ? IntVec3.South : IntVec3.East;
        private List<string> results = new List<string>();
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "TacticalSprintAudit");
        public TacticalSprintAudit(Game game) { }
        public override void LoadedGame()
        {
            if (GenCommandLine.CommandLineArgPassed("pmp-tactical-sprint-audit")) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "sprintAuditStage");
            Scribe_Values.Look(ref phase, "sprintAuditPhase");
            Scribe_Values.Look(ref origin, "sprintAuditOrigin");
            Scribe_References.Look(ref mason, "sprintAuditMason");
            Scribe_References.Look(ref openMason, "sprintAuditOpenMason");
            Scribe_Collections.Look(ref results, "sprintAuditResults", LookMode.Value);
        }
        private void Check(bool value, string message)
        {
            results.Add((value ? "PASS " : "FAIL ") + message);
            File.WriteAllLines(Path.Combine(Folder, "results.txt"), results);
            if (!value) throw new Exception(message);
        }
        private static string Appearance(Pawn p) => string.Join("|", new[] { p.story.bodyType.defName,
            p.story.headType.defName, p.story.hairDef.defName, p.story.HairColor.ToString(), p.story.SkinColor.ToString(),
            p.style.beardDef.defName, p.style.FaceTattoo.defName, p.style.BodyTattoo.defName });
        private Pawn Make(string kind, IntVec3 at)
        {
            var def = DefDatabase<PawnKindDef>.GetNamed(kind); var maker = def.nameMaker; Pawn p;
            try { def.nameMaker = null; p = PawnGenerator.GeneratePawn(def, Faction.OfPlayer); }
            finally { def.nameMaker = maker; }
            GenSpawn.Spawn(p, at, Find.CurrentMap); p.drafter.Drafted = true;
            return p;
        }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp-tactical-sprint-audit") || done || loading || Find.CurrentMap == null || ++frames < 90) return;
            try
            {
                Directory.CreateDirectory(Folder);
                Map map = Find.CurrentMap;
                if (stage == 0)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    origin = map.Center + new IntVec3(20, 0, 20);
                    foreach (IntVec3 cell in CellRect.CenteredOn(origin, 24, 12).Cells)
                    {
                        foreach (Thing t in cell.GetThingList(map).ToList())
                            if (!(t is Pawn)) { if (t.def.destroyable) t.Destroy(); else t.DeSpawn(); }
                        map.roofGrid.SetRoof(cell, null); map.terrainGrid.SetTerrain(cell, TerrainDefOf.PavedTile);
                        map.fogGrid.Unfog(cell);
                    }
                    for (int x = -7; x <= 16; x++) foreach (int z in new[] { -1, 1 })
                        GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), origin + new IntVec3(x, 0, z), map);
                    mason = Make("PMP_CampaignMason", origin);
                    openMason = Make("PMPH_GestaltZero", origin + IntVec3.North * 7 + IntVec3.East * 5);
                    var comp = mason.GetComp<CompTacticalSprint>();
                    Check(comp != null && openMason.GetComp<CompTacticalSprint>() != null, "both Mason kinds opt into reusable comp");
                    Check(Appearance(mason) == Appearance(openMason), "campaign and open Mason have identical canonical appearance");
                    Check(DefDatabase<AbilityDef>.GetNamedSilentFail("PMPH_GestaltZeroInvisibility") == null &&
                        new[] { mason, openMason }.All(p => p.abilities.abilities.All(a => a.def.defName != "PMPH_GestaltZeroInvisibility")), "Gestalt invisibility definition and grants removed");
                    Check(DefDatabase<HediffDef>.GetNamedSilentFail("PsychicInvisibility") != null, "shared vanilla invisibility remains available");
                    Check(new[] { mason, openMason }.All(p => p.health.hediffSet.hediffs.Any(h => h.def.defName == "PMPH_HediffProtossHeart")), "both Mason variants retain Protoss heart");
                    var normal = Make("SpaceRefugee", origin + IntVec3.South * 6);
                    Check(normal.GetComp<CompTacticalSprint>() == null, "ordinary human has no sprint component");
                    var kind = normal.kindDef; var extension = new TacticalSprintExtension();
                    kind.modExtensions = kind.modExtensions ?? new List<DefModExtension>(); kind.modExtensions.Add(extension);
                    try { Check(SprintAttachment.Ensure(normal) != null, "another PawnKind opts in without Mason identifier checks"); }
                    finally { kind.modExtensions.Remove(extension); normal.Destroy(); }
                    Check(!comp.Enabled, "sprint defaults off");
                    float walk = mason.GetStatValue(StatDefOf.MoveSpeed);
                    comp.SetEnabled(true);
                    Check(Mathf.Abs(mason.GetStatValue(StatDefOf.MoveSpeed) / walk - 1.4f) < .001f, "native movement stat gains exactly 40 percent");
                    Check(mason.GetGizmos().OfType<Command_Toggle>().Any(g => g.defaultLabel == "PMP.Sprint.Label".Translate()), "native pawn gizmos expose sprint toggle");
                    Check(SprintTextures.Toggle != BaseContent.BadTex && SprintTextures.Toggle.width > 64, "custom Tac Sprinter inspired icon loads");
                    mason.drafter.Drafted = false;
                    Check(!comp.Active && Mathf.Abs(mason.GetStatValue(StatDefOf.MoveSpeed) - walk) < .001f && !comp.CompGetGizmosExtra().Any(), "undrafted pawn loses bonus and toggle but retains preference");
                    mason.drafter.Drafted = true;
                    Check(comp.Enabled && comp.Active, "drafting resumes saved enabled preference");
                    Check(comp.ObstacleAt(origin + IntVec3.North) == null, "impassable walls never trigger traversal");
                    var door = ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.Steel); GenSpawn.Spawn(door, origin + IntVec3.East * 13, map);
                    Check(comp.ObstacleAt(door.Position) == null, "doors retain native opening without traversal"); door.Destroy();
                    Find.Selector.ClearSelection(); Find.Selector.Select(mason);
                    Find.CameraDriver.SetRootPosAndSize((origin + IntVec3.East * 3).ToVector3Shifted(), 8);
                    BeginPhase(); return;
                }
                if (stage == 1)
                {
                    var comp = mason.GetComp<CompTacticalSprint>();
                    SprintTraversal expected = phase == 0 ? SprintTraversal.None : phase == 2 || phase == 6 ? SprintTraversal.Dive : phase == 7 ? SprintTraversal.Vault : SprintTraversal.Slide;
                    CheckTimeout();
                    if (!comp.Moving || comp.Traversal != expected || mason.pather.MovePercentage < .4f || mason.pather.MovePercentage > .8f ||
                        (phase == 0 && mason.Position == origin)) return;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Vector3 pos = mason.DrawPos, scale; float angle = 0f; Rot4 facing = mason.Rotation;
                    Check(comp.Pose(ref pos, ref angle, ref facing, out scale), "real path movement produces pose: " + expected);
                    Check(!mason.Downed && mason.GetPosture() == PawnPosture.Standing, "pose does not change gameplay posture: " + expected);
                    if (phase > 0) Check(obstacle != null && !obstacle.Destroyed && mason.pather.nextCell == obstacle.Position, "native next cell crosses actual obstacle: " + expected);
                    if (expected == SprintTraversal.Vault) Check(pos.z - mason.DrawPos.z > .15f, "vault visibly lifts body above cover");
                    if (expected == SprintTraversal.Slide || expected == SprintTraversal.Dive)
                    {
                        float target = Mathf.Atan2(Direction.x, Direction.z) * Mathf.Rad2Deg + (expected == SprintTraversal.Slide ? 180f : 0f);
                        Check(Mathf.Abs(Mathf.DeltaAngle(target, angle)) < 5f && scale.z < .9f, "prone body follows movement direction " + Direction);
                        Rot4 expectedFacing = Direction.x > 0 ? Rot4.East : Direction.x < 0 ? Rot4.West : Direction.z > 0 ? Rot4.North : Rot4.South;
                        Check(facing == expectedFacing, expected + " selects movement-facing body/head/apparel: " + expectedFacing);
                        CheckRenderTreeFacing(expectedFacing);
                    }
                    stage = 2; frames = 83; return;
                }
                if (stage == 2)
                {
                    if (phase > 0 && phase < 7)
                    {
                        var rendered = (PawnDrawParms)AccessTools.Field(typeof(PawnRenderTree), "oldParms").GetValue(mason.Drawer.renderer.renderTree);
                        Rot4 expectedFacing = Direction.x > 0 ? Rot4.East : Direction.x < 0 ? Rot4.West : Direction.z > 0 ? Rot4.North : Rot4.South;
                        Check(rendered.facing == expectedFacing, "live render tree uses motion facing after normal draw: phase " + phase);
                    }
                    ScreenCapture.CaptureScreenshot(Path.Combine(Folder, new[] { "run.png", "slide.png", "dive.png", "slide-west.png", "slide-north.png", "slide-south.png", "dive-west.png", "vault.png" }[phase]));
                    if (phase < 7) { phase++; stage = 4; frames = 80; return; }
                    stage = 5; loading = false; frames = 80; return;
                }
                if (stage == 5)
                {
                    stage = 3; loading = true;
                    GameDataSaveLoader.SaveGame("PMP-TacticalSprint-Audit");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("PMP-TacticalSprint-Audit"));
                    return;
                }
                if (stage == 4) { BeginPhase(); return; }
                if (stage == 3)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    var comp = mason.GetComp<CompTacticalSprint>();
                    Check(comp != null && comp.Enabled && mason.AllComps.OfType<CompTacticalSprint>().Count() == 1, "same-version save reload restores one enabled comp");
                    File.WriteAllText(Path.Combine(Folder, "loaded-pose.txt"), "active=" + comp.Active + " moving=" + comp.Moving + " traversal=" + comp.Traversal + " position=" + mason.Position + " next=" + mason.pather.nextCell + " draft=" + mason.Drafted);
                    Check(comp.Traversal == SprintTraversal.Vault, "in-progress traversal survives save reload");
                    Check(Appearance(mason) == Appearance(openMason), "appearance matches after reload");
                    comp.SetEnabled(false);
                    Vector3 pos = mason.DrawPos, scale; float angle = 0; Rot4 facing = mason.Rotation;
                    Check(!comp.Pose(ref pos, ref angle, ref facing, out scale), "switch off immediately stops animated pose");
                    comp.SetEnabled(true); mason.jobs.StopAll(); mason.pather.StopDead();
                    Check(!comp.Moving, "stop movement restores normal stationary pose");
                    mason.drafter.Drafted = false; Check(!comp.Active, "undrafting after load disables sprint");
                    var errors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToList();
                    File.WriteAllLines(Path.Combine(Folder, "errors.txt"), errors);
                    Check(errors.Count == 0, "zero native errors");
                    Finish("PASS");
                }
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(Folder, "failure.txt"), e.ToString()); Finish("FAIL"); }
        }
        private void CheckTimeout()
        {
            if (Find.TickManager.TicksGame - started > 600) throw new Exception("Movement timed out: phase=" + phase + " position=" + mason.Position + " traversal=" + mason.GetComp<CompTacticalSprint>().Traversal);
        }
        private void CheckRenderTreeFacing(Rot4 expectedFacing)
        {
            var renderer = mason.Drawer.renderer;
            var method = AccessTools.Method(typeof(PawnRenderer), "GetDrawParms");
            foreach (Rot4 incoming in new[] { Rot4.North, Rot4.South })
            {
                var parms = (PawnDrawParms)method.Invoke(renderer, new object[] { mason.DrawPos, 0f, incoming, RotDrawMode.Fresh, PawnRenderFlags.None });
                Check(parms.facing == expectedFacing, "final draw parameters override stale " + incoming + " with " + expectedFacing);
                var portrait = (PawnDrawParms)method.Invoke(renderer, new object[] { mason.DrawPos, 0f, incoming, RotDrawMode.Fresh, PawnRenderFlags.Portrait | PawnRenderFlags.Cache });
                Check(portrait.facing == incoming, "portrait retains requested facing " + incoming);
            }
        }
        private void BeginPhase()
        {
            mason.jobs.StopAll(); mason.Position = origin; mason.Notify_Teleported();
            if (obstacle != null && !obstacle.Destroyed) obstacle.Destroy(); obstacle = null;
            foreach (IntVec3 cell in CellRect.CenteredOn(origin, 24, 12).Cells)
                foreach (Thing thing in cell.GetThingList(mason.Map).ToList())
                    if (thing.def == ThingDefOf.Wall) thing.Destroy();
            IntVec3 side = new IntVec3(-Direction.z, 0, Direction.x);
            for (int i = -7; i <= 11; i++) foreach (int sign in new[] { -1, 1 })
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), origin + Direction * i + side * sign, mason.Map);
            Find.CameraDriver.SetRootPosAndSize((origin + Direction * 3).ToVector3Shifted(), 8);
            if (phase > 0)
            {
                ThingDef def = phase == 7 ? ThingDefOf.Sandbags : DefDatabase<ThingDef>.AllDefsListForReading.First(t =>
                    t.category == ThingCategory.Plant && t.pathCost > 0 && t.passability != Traversability.Impassable && t.plant != null && !t.plant.IsTree);
                obstacle = ThingMaker.MakeThing(def, phase == 7 ? ThingDefOf.Cloth : null);
                GenSpawn.Spawn(obstacle, origin + Direction * 3, mason.Map);
                File.AppendAllText(Path.Combine(Folder, "obstacles.txt"), phase + ": " + def.defName + "\n");
            }
            var comp = mason.GetComp<CompTacticalSprint>();
            // Force each rare branch only in this isolated audit; restore production weight after step starts.
            comp.Settings.diveChance = phase == 2 || phase == 6 ? 1f : 0f;
            var job = JobMaker.MakeJob(JobDefOf.Goto, origin + Direction * 10);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            mason.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            started = Find.TickManager.TicksGame; stage = 1;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
        }
        private void Finish(string result)
        {
            if (mason?.GetComp<CompTacticalSprint>() is CompTacticalSprint comp) comp.Settings.diveChance = .05f;
            done = true; File.WriteAllText(Path.Combine(Folder, "complete.txt"), result); Application.Quit();
        }
    }
}
