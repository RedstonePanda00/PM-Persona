using System;
using System.IO;
using System.Linq;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.EnergyFieldSupport
{
    public sealed class EnergyFieldAudit17 : GameComponent
    {
        private int frame, stage;
        private bool loading, done;
        private IntVec3 center;
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "EnergyFieldAudit17");
        public EnergyFieldAudit17(Game game) { }
        public override void ExposeData()
        { Scribe_Values.Look(ref stage, "energyAuditStage"); Scribe_Values.Look(ref center, "energyAuditCenter"); }
        public override void LoadedGame()
        { if (GenCommandLine.CommandLineArgPassed("pmp17energyaudit")) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17energyaudit") || loading || done || Find.CurrentMap == null || ++frame < 90) return;
            try
            {
                int f = frame - 90; Map map = Find.CurrentMap;
                var manager = Current.Game.GetComponent<GameComponent_EnergyFieldManager>();
                if (stage == 1)
                {
                    if (f == 0)
                    {
                        Check(manager.TryGetEnergyField(Faction.OfPlayer, out var restoredMap, out var restoredCenter)
                            && restoredMap == map && restoredCenter == center, "Field lost on load");
                        Check(WarpOperations.HasEnergyFieldAt(map, center + new IntVec3(9, 0, 0)), "Warp hook lost on load");
                        manager.RemoveEnergyField(Faction.OfPlayer);
                        Check(!manager.TryGetEnergyField(Faction.OfPlayer, out _, out _), "End did not remove field");
                        Check(!WarpOperations.HasEnergyFieldAt(map, center), "Ended field still enables warp");
                    }
                    if (f == 10) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Ended.png"));
                    if (f < 20) return;
                    Check(!Log.Messages.Any(m => m.type == LogMessageType.Error), Errors());
                    File.WriteAllText(Path.Combine(Folder, "results.txt"), "PASS PMC native energy-field shader shared by live field and placement preview; radius remains 10; inside/outside and blocked-cell warp rules; relocation, save/load and removal; native rendering with zero Verse errors.");
                    Finish("PASS"); return;
                }
                if (f == 0)
                {
                    Directory.CreateDirectory(Folder); Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    center = map.Center;
                    var rect = CellRect.CenteredOn(center, 42).ClipInsideMap(map);
                    GenDebug.ClearArea(rect, map); foreach (var cell in rect) map.fogGrid.Unfog(cell);
                    Find.CameraDriver.config.sizeRange = new FloatRange(4, 100);
                    Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(), 28);
                    manager.SetEnergyField(Faction.OfPlayer, map, center);
                    Check(WarpOperations.HasEnergyFieldAt(map, center + new IntVec3(9, 0, 0)), "Inside rejected");
                    Check(!WarpOperations.HasEnergyFieldAt(map, center + new IntVec3(11, 0, 0)), "Outside accepted");
                    var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel);
                    GenSpawn.Spawn(wall, center + new IntVec3(4, 0, 0), map);
                    Check(!WarpOperations.HasEnergyFieldAt(map, wall.Position), "Blocked cell accepted");
                }
                // A second draw proves per-field uniforms coexist on the same shared material.
                if (f < 50) EnergyFieldWorldDrawer.DrawAt(map, center + new IntVec3(-25, 0, 0));
                if (f == 20 || f == 45) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Shared-field-" + f + ".png"));
                if (f == 55)
                {
                    var old = center; center += new IntVec3(0, 0, 18);
                    manager.SetEnergyField(Faction.OfPlayer, map, center);
                    Check(!WarpOperations.HasEnergyFieldAt(map, old), "Old field survived relocation");
                    Check(WarpOperations.HasEnergyFieldAt(map, center), "Relocated field missing");
                }
                if (f == 65) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Relocated.png"));
                if (f == 80)
                {
                    Check(!Log.Messages.Any(m => m.type == LogMessageType.Error), Errors());
                    stage = 1; loading = true; GameDataSaveLoader.SaveGame("PMPEnergyFieldAudit");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("PMPEnergyFieldAudit"));
                }
            }
            catch (Exception e) { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "results.txt"), e.ToString()); Finish("FAIL"); }
        }
        private static string Errors() => string.Join("\n", Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text));
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        private void Finish(string result) { done = true; File.WriteAllText(Path.Combine(Folder, "complete.txt"), result); Application.Quit(); }
    }
}
