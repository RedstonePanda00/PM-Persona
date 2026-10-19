using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    // Disposable native render scene, enabled only by the explicit audit command line.
    public sealed class LegionRenderAudit17 : GameComponent
    {
        private int frame;
        private int stage, expectedCount, expectedAge, expectedPending, expectedWarpStart;
        private Vector2 expectedHead;
        private bool loading;
        private Building_CarrierOrbiter carrier;
        private bool done;
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "LegionAudit17");
        public LegionRenderAudit17(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "legionAuditStage"); Scribe_Values.Look(ref expectedCount, "legionAuditCount");
            Scribe_Values.Look(ref expectedAge, "legionAuditAge"); Scribe_Values.Look(ref expectedPending, "legionAuditPending");
            Scribe_Values.Look(ref expectedHead, "legionAuditHead");
            Scribe_Values.Look(ref expectedWarpStart, "legionAuditWarpStart");
        }
        public override void LoadedGame()
        {
            if (GenCommandLine.CommandLineArgPassed("pmp17legionaudit") && stage == 1)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17legionaudit") || done || loading || Find.CurrentMap == null || ++frame < 90) return;
            try
            {
                int f = frame - 90;
                Map map = Find.CurrentMap;
                var center = map.Center.ToVector3Shifted();
                if (stage == 1)
                {
                    carrier = map.listerThings.AllThings.OfType<Building_CarrierOrbiter>().Single();
                    if (carrier.FlockCount != expectedCount || carrier.FlockAge != expectedAge || carrier.PendingStrikeCount != expectedPending
                        || Vector2.Distance(carrier.FlockHead, expectedHead) > .001f) throw new Exception("Flock or pending strike changed across save/load");
                    if (carrier.HeadFlightState != 5 || carrier.HeadWarpStart != expectedWarpStart) throw new Exception("Warp phase/timer lost on load");
                    carrier.AuditResumeWarp();
                    if (Log.Messages.Any(m => m.type == LogMessageType.Error)) throw new Exception("Verse errors after reload");
                    File.AppendAllText(Path.Combine(Folder, "results.txt"), " PASS positions, member count, support age and pending strikes survive save/load; hidden transit resumes through warp-in and rejoins.");
                    Finish("PASS"); return;
                }
                if (f == 0)
                {
                    Directory.CreateDirectory(Folder);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    foreach (var cell in CellRect.CenteredOn(map.Center, 18).ClipInsideMap(map)) map.fogGrid.Unfog(cell);
                    Find.CameraDriver.config.sizeRange = new FloatRange(4, 100);
                    Find.CameraDriver.SetRootPosAndSize(center, 8);
                }
                if (f < 50)
                {
                    var bodies = (Material[])AccessTools.Field(typeof(Building_CarrierOrbiter), "variantShadowMats").GetValue(null);
                    var draw = AccessTools.Method(typeof(Building_CarrierOrbiter), "DrawInstancedBatch");
                    for (int v = 0; v < 3; v++)
                    {
                        var p = center + new Vector3((v - 1) * 7, 0, 0); p.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                        // Cross the 1023-instance boundary; off-screen copies must not affect the visible sprite.
                        var matrices = new List<Matrix4x4>(1025);
                        for (int i = 0; i < 1025; i++)
                            matrices.Add(Matrix4x4.TRS(p + (i == 0 || i == 1024 ? Vector3.zero : new Vector3(-1000, 0, 0)),
                                Quaternion.identity, new Vector3(5, 1, 5)));
                        draw.Invoke(null, new object[] { matrices, bodies[v] });
                    }
                    if (f == 30) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Variants.png"));
                    return;
                }
                if (f == 50)
                {
                    carrier = (Building_CarrierOrbiter)ThingMaker.MakeThing(ThingDef.Named("PMP_CarrierOrbiter"));
                    GenSpawn.Spawn(carrier, map.Center, map);
                    carrier.AuditFleetLifecycle();
                    File.WriteAllText(Path.Combine(Folder, "motion-results.txt"), carrier.AuditFlockMotion());
                    Find.CameraDriver.SetRootPosAndSize(center, 65);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                }
                if (f == 130 || f == 210) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Swarm-" + f + ".png"));
                if (f == 140)
                { Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; carrier.PrepareFleetAuditPose(false, 51); }
                if (f == 150) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Fleet-entry.png"));
                if (f == 160) carrier.PrepareFleetAuditPose(true, 29);
                if (f == 170) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Fleet-exit.png"));
                if (f == 180)
                { carrier.SetupOrbit(map.Center); carrier.AuditFlockMotion(); }
                if (f == 60)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Find.CameraDriver.SetRootPosAndSize(center, 8);
                    carrier.PrepareWarpAuditPose(4, 6);
                }
                if (f == 70) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Warp-out.png"));
                if (f == 80) carrier.PrepareWarpAuditPose(5, 4);
                if (f == 85) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Warp-transit.png"));
                if (f == 90) carrier.PrepareWarpAuditPose(6, 5);
                if (f == 100) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Warp-in.png"));
                if (f == 110)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                    Find.CameraDriver.SetRootPosAndSize(center, 65);
                }
                if (f < 230) return;
                if (Log.Messages.Any(m => m.type == LogMessageType.Error))
                    throw new Exception(string.Join("\n", Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text)));
                File.WriteAllText(Path.Combine(Folder, "results.txt"), "PASS initial staggered warp entry; final warp departure; final salvo completes; no dispatch/return after retirement; three variants and 1023-instance boundary; zero Verse errors. Inspect Fleet-entry/exit.png and Swarm images.");
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                carrier.EnqueueLegionStrike(Find.TickManager.TicksGame + 600, new IntVec3(10, 0, 10), 1, 1, 0);
                carrier.PrepareWarpAuditPose(5, 4); expectedWarpStart = carrier.HeadWarpStart;
                expectedCount = carrier.FlockCount; expectedHead = carrier.FlockHead; expectedAge = carrier.FlockAge; expectedPending = carrier.PendingStrikeCount;
                stage = 1; loading = true;
                GameDataSaveLoader.SaveGame("PMPLegionAudit");
                LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("PMPLegionAudit"));
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "results.txt"), ex.ToString()); Finish("FAIL");
            }
        }
        private void Finish(string result)
        {
            done = true; carrier?.Destroy(DestroyMode.Vanish);
            File.WriteAllText(Path.Combine(Folder, "complete.txt"), result); Application.Quit();
        }
    }
}
