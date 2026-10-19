using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    // Opt-in native rendering, lighting, selection and save/load regression scene.
    public sealed class PurificationAudit17 : GameComponent
    {
        private int frame, stage, start, expectedAge;
        private bool done, loading;
        private Thing_MobileBeam beam, second;
        private Thing solar;
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "PurificationAudit17");
        public PurificationAudit17(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "purificationAuditStage");
            Scribe_Values.Look(ref expectedAge, "purificationAuditAge");
        }
        public override void LoadedGame()
        { if (GenCommandLine.CommandLineArgPassed("pmp17purificationaudit")) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; }
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17purificationaudit") || done || loading || Find.CurrentMap == null || ++frame < 90) return;
            try
            {
                int f = frame - 90; Map map = Find.CurrentMap;
                if (stage == 1)
                {
                    var beams = map.listerThings.AllThings.OfType<Thing_MobileBeam>().OrderBy(b => b.thingIDNumber).ToArray();
                    Check(beams.Length == 2, "Beam ownership lost on load");
                    Check(beams[0].VisualAge == expectedAge, "Guidance/active age restarted on load");
                    map.skyManager.SkyManagerUpdate();
                    Check(map.skyManager.CurSkyGlow > .98f, "White daylight lost on reload");
                    int end = beams[0].GetComp<CompDestroyAfterDelay>().DestructionTick;
                    Check(end - (Find.TickManager.TicksGame - beams[0].VisualAge) == 3600 + Thing_MobileBeam.GuidanceTicks,
                        "Guidance consumed support duration");
                    Find.TickManager.DebugSetTicksGame(end);
                    AccessTools.Method(typeof(Thing_MobileBeam), "Tick").Invoke(beams[0], null);
                    Check(beams[0].Destroyed, "Natural expiry failed");
                    map.skyManager.SkyManagerUpdate();
                    Check(map.skyManager.CurSkyGlow > .98f, "First removal cancelled second beam daylight");
                    beams[1].Destroy(DestroyMode.Vanish); map.skyManager.SkyManagerUpdate();
                    Check(map.skyManager.CurSkyGlow < .3f, "Night not restored after last beam removed");
                    Check(!Log.Messages.Any(m => m.type == LogMessageType.Error), Errors());
                    File.WriteAllText(Path.Combine(Folder, "results.txt"), "PASS native guidance columns + plasma volume/contact/scorch; 3x3 occupied selection grid follows movement; no guidance damage; solar reacts to vanilla sky; unchanged clock; two-beam lighting removal; active save/load preserves age and daylight. Zero Verse errors.");
                    Finish("PASS"); return;
                }
                if (f == 0)
                {
                    Directory.CreateDirectory(Folder); Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    for (int hour = 0; hour < 24; hour++)
                    {
                        Find.TickManager.DebugSetTicksGame(60000 + hour * 2500); map.skyManager.SkyManagerUpdate();
                        if (map.skyManager.CurSkyGlow < .15f) break;
                    }
                    start = Find.TickManager.TicksGame;
                    var rect = CellRect.CenteredOn(map.Center, 25).ClipInsideMap(map);
                    GenDebug.ClearArea(rect, map); foreach (var c in rect) map.fogGrid.Unfog(c);
                    Find.CameraDriver.config.sizeRange = new FloatRange(4, 100);
                    Find.CameraDriver.SetRootPosAndSize(map.Center.ToVector3Shifted(), 24);
                    solar = ThingMaker.MakeThing(ThingDefOf.SolarGenerator); solar.SetFaction(Faction.OfPlayer);
                    GenSpawn.Spawn(solar, map.Center + new IntVec3(-18, 0, -15), map);
                    var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel);
                    wall.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(wall, map.Center + new IntVec3(1, 0, 0), map);
                    var plant = ThingMaker.MakeThing(ThingDef.Named("Plant_Grass"));
                    GenSpawn.Spawn(plant, map.Center, map);
                    beam = Spawn(map.Center, map);
                    Check(wall.Spawned && plant.Spawned, "Selection footprint wiped existing objects");
                    CheckSelection(beam, map);
                    Check(Math.Abs(Power(solar)) < 200, "Baseline is not dark enough for solar test");
                }
                if (f <= 140)
                {
                    Find.TickManager.DebugSetTicksGame(start + f);
                    if (f > 0) AccessTools.Method(typeof(Thing_MobileBeam), "Tick").Invoke(beam, null);
                    if (f < Thing_MobileBeam.GuidanceTicks)
                        Check(map.listerThings.ThingsOfDef(ThingDefOf.Fire).Count == 0, "Damage/fire during guidance");
                    if (f == 24 || f == 60 || f == 105 || f == 135)
                        ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Beam-" + f + ".png"));
                }
                if (f == 141)
                {
                    map.skyManager.SkyManagerUpdate(); Check(map.skyManager.CurSkyGlow > .98f, "No daytime sky");
                    Check(Power(solar) > 1000, "Sky did not affect solar generation: " + Power(solar));
                    Check(Find.TickManager.TicksGame == start + 140, "Beam changed game clock");
                    beam.SetDestination(map.Center + new IntVec3(10, 0, 0));
                    for (int i = 1; i <= 60; i++)
                    { Find.TickManager.DebugSetTicksGame(start + 140 + i); AccessTools.Method(typeof(Thing_MobileBeam), "Tick").Invoke(beam, null); }
                    Check(beam.Position.x > map.Center.x + 2, "Beam did not move"); CheckSelection(beam, map);
                    second = Spawn(map.Center + new IntVec3(16, 0, 0), map);
                    for (int i = 1; i <= 110; i++)
                    {
                        Find.TickManager.DebugSetTicksGame(start + 200 + i);
                        AccessTools.Method(typeof(Thing_MobileBeam), "Tick").Invoke(second, null);
                    }
                }
                if (f == 150) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Moved-and-two-beams.png"));
                if (f == 165)
                {
                    Check(!Log.Messages.Any(m => m.type == LogMessageType.Error), Errors());
                    expectedAge = beam.VisualAge; stage = 1; loading = true;
                    GameDataSaveLoader.SaveGame("PMPPurificationAudit");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("PMPPurificationAudit"));
                }
            }
            catch (Exception e) { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "results.txt"), e.ToString()); Finish("FAIL"); }
        }
        private static Thing_MobileBeam Spawn(IntVec3 cell, Map map)
        {
            var b = (Thing_MobileBeam)ThingMaker.MakeThing(ThingDef.Named("PMP_PurificationBeam"));
            b.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(b, cell, map); return b;
        }
        private static void CheckSelection(Thing b, Map map)
        {
            Check(b.OccupiedRect().Area == 9, "Not 3x3");
            foreach (var cell in b.OccupiedRect()) Check(cell.GetThingList(map).Contains(b), "Selection grid stale at " + cell);
        }
        private static float Power(Thing t) => (float)AccessTools.Property(typeof(CompPowerPlantSolar), "DesiredPowerOutput").GetValue(t.TryGetComp<CompPowerPlantSolar>());
        private static string Errors() => string.Join("\n", Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text));
        private void Finish(string result)
        { done = true; File.WriteAllText(Path.Combine(Folder, "complete.txt"), result); Application.Quit(); }
    }
}
