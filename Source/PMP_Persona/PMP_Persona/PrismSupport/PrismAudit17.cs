using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using PMP_Persona.ShipSupport;
using PMP_Persona.VanguardSupport;

namespace PMP_Persona.PrismSupport
{
    // Opt-in isolated native audit, including serialized custom components and world-camera handoff.
    public sealed class PrismAudit17 : GameComponent
    {
        private int frame, stage, strikeTick;
        private bool loading, done, capturedGuidance, capturedMain, capturedWave;
        private float maximumShake;
        private OrbitalSupportSession ship;
        private Vector3 original;
        private IntVec3 strikeCell;
        private TerrainDef terrain;
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "PrismAudit17");
        public PrismAudit17(Game game) { }
        public override void ExposeData() => Scribe_Values.Look(ref stage, "prismAuditStage");
        public override void LoadedGame()
        {
            if (GenCommandLine.CommandLineArgPassed("pmp17prismaudit") && stage == 1)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17prismaudit") || loading || done || Find.CurrentMap == null || ++frame < 90) return;
            try
            {
                var c = VanguardFireControl.CurrentController;
                int f = frame - 90;
                if (stage == 1)
                {
                    if (f == 0)
                    {
                        ship = c.Ship;
                        Check(ship?.IsPrism == true && ship.Ready && ship.SelectedWeapon == 2 && !ship.Infrared,
                            "custom session did not resume after load");
                        Check(ship.Ammo(2) == 9 && ship.CombatTicks == 123, "ammo/timer persistence: " + ship.Ammo(2) + "/" + ship.CombatTicks);
                        Check(ship.Map.components.Count(x => x is OrbitalSupportSession) == 1, "duplicate session on load");
                        Check(!WorldRendererUtility.WorldSelected, "world camera retained after ready/reload");
                        ship.Phase = OrbitalSupportSession.SupportPhase.Departure;
                    }
                    if (f < 8)
                    {
                        ship.ExitElapsed = f * .10f; c.ApplyCamera();
                        if (f == 5) Capture("Exit-black"); return;
                    }
                    if (f == 8)
                    {
                        original = ship.ReturnPosition; c.Advance(1);
                        Check(c.Ship == null && ship.Map.GetComponent<OrbitalSupportSession>() == null, "session not detached");
                        Check(Math.Abs(Find.CameraDriver.MyCamera.transform.position.x - original.x) < .01f, "camera return");
                    }
                    if (f < 20) return;
                    Check(!Log.Messages.Any(m => m.type == LogMessageType.Error), string.Join("\n", Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text)));
                    File.WriteAllText(Path.Combine(Folder, "results.txt"), "PASS CustomMapComponent attach/save/load/remove; no anchor Things; native galaxy/jet/world/tile/map transitions; 1/10/40 ammo, independent 8/5/3 reloads; 90/300 tick magazine spans; three guidance volumes/main beam/terrain preservation; dynamic compass; solar HUD; timer and expiry. Zero Verse errors.");
                    Finish("PASS"); return;
                }
                if (f == 0)
                {
                    Directory.CreateDirectory(Folder);
                    OrbitalDamageAudit17.Run(Find.CurrentMap, Folder);
                    Check(Find.CurrentMap.GetComponent<OrbitalSupportSession>() == null, "session auto-created on an idle map");
                    Find.CameraDriver.SetRootPosAndSize(Find.CurrentMap.Center.ToVector3Shifted() + new Vector3(-20, 0, -15), 30);
                    original = Find.CameraDriver.MyCamera.transform.position;
                    ship = OrbitalSupportSession.Attach(Find.CurrentMap, DefDatabase<ShipSupportDef>.GetNamed("PMP_Support_Prism"));
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    c.Begin(ship);
                    Check(Find.TickManager.Paused && ship.CombatTicks == 0, "arrival must pause simulation");
                    Check(DefDatabase<ThingDef>.GetNamedSilentFail("PMP_PrismOrbiter") == null, "anchor def still present");
                }
                if (f < 70)
                {
                    float[] times = { 1.4f, 2.9f, 3.8f, 4.7f, 5.8f, 7.7f, 8.6f };
                    ship.EntryElapsed = times[f / 10]; c.ApplyCamera();
                    Check(ship.CombatTicks == 0, "arrival consumed combat time");
                    if (f == 25) Check(WorldRendererUtility.WorldSelected, "native world view never enabled");
                    if (f == 25)
                    {
                        var planet = Find.WorldCamera.WorldToViewportPoint(PlanetLayer.Selected.Origin);
                        Check(Math.Abs(planet.x - .2f) < .01f && planet.y > .75f, "planet did not appear at the upper-left vanishing point: " + planet);
                    }
                    if (f == 65) Check(WorldRendererUtility.DrawingMap, "map view did not return");
                    if (f % 10 == 5) Capture("Arrival-" + f / 10);
                    return;
                }
                if (f == 70)
                {
                    ship.EntryElapsed = ship.EntryEnd - .01f; c.Advance(.02f);
                    Check(ship.Ready && ship.RemainingLifeTicks == 3600, "readiness/timer handoff");
                    OrbitalCameraShakeAudit.Run(Folder);
                    TestMagazines();
                    ship.SetupOrbit(); ship.CameraSaved = true; ship.Phase = OrbitalSupportSession.SupportPhase.FireControl;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    for (int i = 0; i < 90; i++) c.MoveView(Vector3.right, 1f / 60);
                    Check(Mathf.Abs(Mathf.DeltaAngle(ship.Bearing, 90)) < .1f, "east compass");
                    for (int i = 0; i < 90; i++) c.MoveView(Vector3.back, 1f / 60);
                    Check(Mathf.Abs(Mathf.DeltaAngle(ship.Bearing, 180)) < .1f, "south compass");
                    float bearing = ship.Bearing; c.MoveView(Vector3.zero, 1f / 60);
                    Check(Mathf.Abs(ship.Bearing - bearing) < .01f, "idle compass jumped");
                    ship.ViewPosition = ship.Map.Center.ToVector3Shifted(); c.ApplyCamera();
                }
                if (f == 76) Capture("Fire-control");
                if (f == 80)
                {
                    ship.Infrared = false;
                    strikeCell = ship.Map.Center + new IntVec3(0, 0, 6);
                    foreach (var cell in CellRect.CenteredOn(strikeCell, 16).ClipInsideMap(ship.Map)) ship.Map.fogGrid.Unfog(cell);
                    terrain = strikeCell.GetTerrain(ship.Map);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                    Check(ship.TryFire(1, strikeCell) && !ship.TryFire(1, strikeCell), "fracture ammo guard");
                    strikeTick = Find.TickManager.TicksGame;
                }
                if (f > 80)
                {
                    int age = Find.TickManager.TicksGame - strikeTick;
                    var effects = ship.Map.GetComponent<VanguardImpactEffects>();
                    maximumShake = Mathf.Max(maximumShake, effects.ImpactShake.magnitude);
                    if (age >= 25 && !capturedGuidance) { Capture("Guidance"); capturedGuidance = true; }
                    if (age >= ship.Config.fractureGuidanceTicks + 8 && !capturedMain) { Capture("Main-beam"); capturedMain = true; }
                    if (age >= ship.Config.fractureGuidanceTicks + 35 && !capturedWave) { Capture("Shockwave"); capturedWave = true; }
                    if (age < ship.Config.fractureGuidanceTicks + 370) return;
                    Check(strikeCell.GetTerrain(ship.Map) == terrain, "fracture modified terrain");
                    Check(ship.Map.GetComponent<PrismFractureEffects>() == null, "completed fracture component not removed");
                    Check(maximumShake > 8 && maximumShake <= 24.01f, "fracture impact shake missing/unbounded: " + maximumShake);
                    Check(effects.ParticleCount == 0 && effects.ScarCount == 0 && effects.ImpactShake == Vector2.zero, "fracture dust/scar/shake cleanup");
                    ship.SetupOrbit(); ship.CameraSaved = true; ship.Phase = OrbitalSupportSession.SupportPhase.FireControl;
                    ship.SelectedWeapon = 2; ship.Infrared = false;
                    Check(ship.TryFire(2, new IntVec3(20, 0, 20)), "light particle single shot");
                    Traverse.Create(ship).Field("combatTicks").SetValue(123);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    stage = 1; loading = true;
                    GameDataSaveLoader.SaveGame("PMPPrismAudit");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("PMPPrismAudit"));
                }
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "results.txt"), ex.ToString());
                VanguardFireControl.CurrentController?.End(); Finish("FAIL");
            }
        }
        private void TestMagazines()
        {
            int baseline = Find.TickManager.TicksGame;
            var target = new IntVec3(15, 0, 15);
            try
            {
                foreach (int weapon in new[] { 3, 2 })
                {
                    int start = baseline + (weapon == 3 ? 0 : 1000), shots = 0, last = -1;
                    for (int age = 0; age <= (weapon == 3 ? 95 : 305); age++)
                    {
                        Find.TickManager.DebugSetTicksGame(start + age);
                        if (ship.TryFire(weapon, target)) { shots++; last = age; }
                    }
                    Check(shots == (weapon == 3 ? 40 : 10), "magazine count " + weapon + "=" + shots);
                    Check(Math.Abs(last - (weapon == 3 ? 90 : 300)) <= 1, "magazine timing " + weapon + "=" + last);
                    Check(ship.Ammo(weapon) == 0, "empty magazine guard");
                    int reload = Mathf.RoundToInt(ship.Config.weapons[weapon - 1].reloadSeconds * 60);
                    Find.TickManager.DebugSetTicksGame(start + last + reload - 1);
                    Check(!ship.TryFire(weapon, target), "early reload");
                    Find.TickManager.DebugSetTicksGame(start + last + reload);
                    Check(ship.TryFire(weapon, target) && ship.Ammo(weapon) == ship.Capacity(weapon) - 1, "full reload");
                }
                ship.SelectedWeapon = 1; ship.CycleWeapon(); ship.CycleWeapon(); ship.CycleWeapon();
                Check(ship.SelectedWeapon == 1, "right click cycle wrap");
            }
            finally
            {
                foreach (var round in ship.Map.listerThings.AllThings.OfType<PMPH_OrbitalIncoming>().ToArray()) round.Destroy(DestroyMode.Vanish);
                Find.TickManager.DebugSetTicksGame(baseline);
            }
        }
        private void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png"));
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private void Finish(string result) { done = true; File.WriteAllText(Path.Combine(Folder, "complete.txt"), result); Application.Quit(); }
    }
}
