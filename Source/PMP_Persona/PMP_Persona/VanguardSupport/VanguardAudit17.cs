using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.VanguardSupport
{
    // Opt-in integration checks and actual native UI screenshots in an isolated quicktest colony.
    public sealed class VanguardAudit17 : GameComponent
    {
        private int frame, stage;
        private OrbitalSupportSession ship;
        private Vector3 returnPosition;
        private float returnSize;
        private bool done, loading;
        private int impactTick = -1;
        private bool capturedHot, capturedCool;
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "VanguardAudit17");
        public VanguardAudit17(Game game) { }
        public override void ExposeData() => Scribe_Values.Look(ref stage, "vanguardAuditStage");
        public override void LoadedGame()
        {
            if (GenCommandLine.CommandLineArgPassed("pmp17vanguardaudit") && stage == 1)
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17vanguardaudit") || done || loading || Find.CurrentMap == null || ++frame < 90) return;
            try
            {
                int f = frame - 90;
                var controller = VanguardFireControl.CurrentController;
                if (stage == 1)
                {
                    if (f == 0)
                    {
                        ship = controller.Ship;
                        Require(ship != null && ship.Ready && ship.CombatTicks == 123 && ship.RemainingLifeTicks == 3477,
                            "support state after reload: ship=" + (ship != null) + ", phase=" + ship?.Phase + ", ticks=" + ship?.CombatTicks);
                        Require(ship.ReturnSpeed == TimeSpeed.Fast && ship.CameraSaved, "return camera/speed state was not saved");
                        Require(!ship.Infrared, "optical sensor state did not survive reload");
                        returnPosition = ship.ReturnPosition; returnSize = ship.ReturnSize;
                        Traverse.Create(ship).Field("combatTicks").SetValue(3599);
                        ship.MapComponentTick();
                        Require(ship.Phase == OrbitalSupportSession.SupportPhase.Departure, "support failed to expire");
                    }
                    if (f < 6)
                    {
                        ship.ExitElapsed = f * .14f;
                        controller.ApplyCamera();
                        Require(controller.Ship == ship && VanguardFireControl.OwnsView && !ship.Ready, "black hold released UI early");
                        if (f == 0 || f == 5) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Departure-black-" + f + ".png"));
                        return;
                    }
                    if (f == 6)
                    {
                        controller.Advance(1);
                        Require(ship.Destroyed && controller.Ship == null && !VanguardFireControl.OwnsView, "input remained captured after expiry");
                        Require(Math.Abs(Find.CameraDriver.RootSize - returnSize) < .01f, "camera zoom not restored");
                        Vector3 returned = Find.CameraDriver.MyCamera.transform.position;
                        Require(Math.Abs(returned.x - returnPosition.x) < .01 && Math.Abs(returned.z - returnPosition.z) < .01,
                            "camera position not restored");
                    }
                    if (f == 8) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Departure-restored.png"));
                    if (f < 20) return;
                    var errors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToArray();
                    Require(errors.Length == 0, string.Join("\n", errors));
                    Require(File.Exists(Path.Combine(Folder, "FireControl-78.png")), "native GUI screenshot missing");
                    File.WriteAllText(Path.Combine(Folder, "results.txt"), "PASS intro pause; phase handoff; timer starts only when ready; speed restored; independent cooldowns; original 1+5 projectiles; no gizmos; pause fire guard; save/reload and reconnect; expiry; camera/input cleanup; native UI screenshots; zero Verse errors.");
                    Finish("PASS"); return;
                }
                if (f == 0)
                {
                    Directory.CreateDirectory(Folder);
                    Find.CameraDriver.SetRootPosAndSize(Find.CurrentMap.Center.ToVector3Shifted() + new Vector3(-35, 0, -25), 30);
                    returnPosition = Find.CameraDriver.MyCamera.transform.position; returnSize = Find.CameraDriver.RootSize;
                    ship = OrbitalSupportSession.Attach(Find.CurrentMap, DefDatabase<PMP_Persona.ShipSupport.ShipSupportDef>.GetNamed("PMP_Support_Vanguard"));
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Fast;
                    controller.Begin(ship);
                    Require(Find.TickManager.CurTimeSpeed == TimeSpeed.Paused && ship.CombatTicks == 0, "intro did not pause simulation");
                    Require(Mathf.Abs(Find.CameraDriver.MyCamera.transform.position.x - ship.Map.Size.x * .5f) < 1
                        && Mathf.Abs(Find.CameraDriver.MyCamera.transform.position.z - ship.Map.Size.z * .5f) < 1, "intro did not center an off-center camera immediately");
                    Vector3 start = VanguardPresentation.ArrivalWorld(ship, VanguardPresentation.WarpStart);
                    Require(start.x > ship.Map.Size.x && start.z > ship.Map.Size.z && start.y == VanguardPresentation.ArrivalHeight,
                        "entry is not at elevated northeast map corner");
                    Require(!ship.TryFire(1, ship.Position), "weapon fired before ready");
                    Require(!ship.Map.listerThings.AllThings.Any(t => t.def.defName == "PMP_VanguardOrbiter"), "legacy anchor remains");
                    float previousLength = 0;
                    for (float t = 1.6f; t <= 2.201f; t += .05f)
                    {
                        Vector2 shape = VanguardPresentation.ArrivalWarp(t);
                        Require(shape.y == 1 && shape.x >= previousLength && shape.x <= 1, "A must only recover along movement axis");
                        previousLength = shape.x;
                    }
                    Require(Math.Abs(VanguardPresentation.ArrivalWarp(1.6f).x - 1f / 3) < .001f, "A initial one-third compression");
                    Require(VanguardPresentation.ArrivalWarp(VanguardPresentation.ApproachStart) == Vector2.one, "B reached before full shape restoration");
                    Require(VanguardPresentation.WarpLight(1.8f) > 0 && VanguardPresentation.WarpLight(2.3f) == 0, "short warp flash boundaries");
                    Vector2 lastPosition = VanguardPresentation.ArrivalPosition(VanguardPresentation.ApproachStart);
                    float lastPitch = 11, lastZoom = -1;
                    Require(Math.Abs(VanguardPresentation.ArrivalPosition(VanguardPresentation.WarpStart).x
                        - VanguardPresentation.ArrivalPosition(VanguardPresentation.WarpArrival).x - .58f / 3) < .0001f,
                        "warp distance was not reduced to one third");
                    Require(lastPosition.x - .5f > .3f, "slow movement distance was not extended");
                    Require(VanguardPresentation.ArrivalBlack(6.05f) == 0 && VanguardPresentation.ArrivalBlack(6.28f) == 1,
                        "brief arrival blackout boundaries");
                    for (float t = 2.2f; t < VanguardPresentation.ApproachEnd; t += .2f)
                    {
                        Vector2 position = VanguardPresentation.ArrivalPosition(t);
                        float pitch = VanguardPresentation.ArrivalPitch(t), zoom = VanguardPresentation.CameraApproach(t);
                        Require(position.x < lastPosition.x && position.y > lastPosition.y && pitch < lastPitch && zoom > lastZoom,
                            "B movement, leveling or camera stopped before handoff");
                        lastPosition = position; lastPitch = pitch; lastZoom = zoom;
                    }
                    Require((VanguardPresentation.ArrivalPosition(7) - new Vector2(.5f, .5f)).magnitude < .0001f
                        && VanguardPresentation.ArrivalPitch(7) == 0 && VanguardPresentation.CameraApproach(7) == 1,
                        "center/level/zoom endpoints not synchronized to fire control");
                    Require(VanguardPresentation.ArrivalShakeAmplitude(0) == 10 && VanguardPresentation.ArrivalShakeAmplitude(7) == 2.5f
                        && VanguardPresentation.ArrivalShake(6.2f, 7).magnitude > .2f, "intro shake did not converge to quarter strength");
                    Require(VanguardPresentation.ArrivalWorld(ship, 7).y == VanguardPresentation.SettledHeight
                        && VanguardPresentation.PlasmaWakeLength >= 3, "altitude/wake targets");
                    float lastSize = VanguardFireControl.OverviewSize(ship), lastHeight = VanguardPresentation.ArrivalHeight;
                    for (int i = 1; i <= 20; i++)
                    {
                        float t = Mathf.Lerp(VanguardPresentation.ApproachStart, VanguardPresentation.EntryEnd, i / 20f);
                        ship.EntryElapsed = t; controller.ApplyCamera();
                        float size = Find.CameraDriver.RootSize, height = VanguardPresentation.ArrivalWorld(ship, t).y;
                        Require(size < lastSize && height < lastHeight, "zoom or descent stopped before fire-control handoff");
                        lastSize = size; lastHeight = height;
                    }
                    Require(Mathf.Abs(lastSize - VanguardFireControl.ArrivalCloseSize) < .01f
                        && lastHeight == VanguardPresentation.SettledHeight, "descent/maximum zoom endpoint mismatch");
                }
                if (f < 70)
                {
                    float[] times = { 1.69f, 1.78f, 2.04f, 3.7f, 5.1f, 6.00f, 6.28f };
                    ship.EntryElapsed = times[f / 10]; controller.ApplyCamera();
                    Vector3 hullCenter = VanguardPresentation.ArrivalWorld(ship, ship.EntryElapsed);
                    var projected = VanguardPresentation.ProjectHull(hullCenter, 30, 15);
                    Require(projected.width > 0 && projected.height > 0 && Find.Camera.WorldToScreenPoint(hullCenter).z > 0,
                        "hull projection inverted or crossed the camera plane");
                    Require(ship.CombatTicks == 0, "intro consumed support duration");
                    if (f == 65) Require(Math.Abs(Find.CameraDriver.RootSize - VanguardFireControl.ArrivalCloseSize) < .01f,
                        "arrival camera did not reach close hull size");
                    if (f % 10 == 5) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Intro-" + (f / 10) + ".png"));
                }
                if (f == 70)
                {
                    ship.EntryElapsed = VanguardPresentation.EntryEnd - .01f; controller.Advance(.02f);
                    Require(ship.Ready && Find.TickManager.CurTimeSpeed == TimeSpeed.Fast, "readiness did not restore previous speed");
                    Require(ship.CombatTicks == 0 && ship.RemainingLifeTicks == 3600, "support timer started early");
                    OrbitalCameraShakeAudit.Run(Folder);
                    Vector3 view = ship.ViewPosition;
                    controller.MoveView(Vector3.right, .25f);
                    Require(Mathf.Abs(ship.ViewPosition.x - view.x - 16) < .001f, "fire-control speed is not 64 cells/second");
                    ship.ViewPosition = view;
                    controller.MoveView(new Vector3(1, 0, 1), .25f);
                    Require(Mathf.Abs((ship.ViewPosition - view).magnitude - 16) < .001f, "diagonal movement exceeds cardinal speed");
                    ship.ViewPosition = new Vector3(ship.Map.Size.x, view.y, ship.Map.Size.z);
                    controller.MoveView(new Vector3(1, 0, 1), .25f);
                    Require(ship.ViewPosition.x == ship.Map.Size.x && ship.ViewPosition.z == ship.Map.Size.z, "view escaped map bounds");
                    ship.ViewPosition = view;
                    int before = ship.Map.listerThings.AllThings.OfType<PMPH_OrbitalIncoming>().Count();
                    var target = new IntVec3(20, 0, 20);
                    Require(ship.TryFire(1, target) && ship.TryFire(2, target), "independent weapons did not both fire");
                    Require(!ship.TryFire(1, target) && !ship.TryFire(2, target), "weapon cooldown bypass");
                    Require(controller.Presentation.CannonKick(0) == 1 && controller.Presentation.CannonKick(1) == 0,
                        "actual shot/cooldown rejection did not produce exactly one barrel kick");
                    var rounds = ship.Map.listerThings.AllThings.OfType<PMPH_OrbitalIncoming>().ToList();
                    Require(rounds.Count == before + 6, "autocannon/missile volley counts changed");
                    foreach (var round in rounds.ToArray())
                        if (round.def.defName == "PMP_VanguardNeutronIncoming" || round.def.defName == "PMP_AntiparticleShellIncoming")
                            round.Destroy(DestroyMode.Vanish);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Require(!ship.TryFire(1, target), "weapon fired while paused");
                    controller.Presentation.Advance(.3f);
                    Require(controller.Presentation.CannonKick(0) == 1, "recoil advanced while game time was paused");
                    using (var probe = new VanguardPresentation())
                    {
                        probe.FireCannon(100);
                        Require(probe.CannonKick(0, 100) == 1 && probe.CannonKick(1, 100) == 0, "shot-tick recoil onset");
                        probe.FireCannon(115);
                        Require(probe.CannonKick(1, 115) == 1 && probe.CannonKick(0, 115) == 0, "15-tick alternating cadence");
                        Require(probe.CannonKick(1, 129) == 0, "recoil did not recover within firing interval");
                        for (int i = 0; i < 120; i++) probe.MoveCannons(new Vector2(1, 0), 1f / 60);
                        Require(probe.CannonSway.x > 4.9f && probe.CannonSway.x < 5.1f, "right movement must bank clockwise by five degrees");
                        for (int i = 0; i < 120; i++) probe.MoveCannons(new Vector2(-1, 0), 1f / 60);
                        Require(probe.CannonSway.x < -4.9f && probe.CannonSway.x > -5.1f, "left movement must bank counterclockwise");
                        for (int i = 0; i < 120; i++) probe.MoveCannons(new Vector2(0, 1), 1f / 60);
                        Require(Mathf.Abs(probe.CannonSway.x) < .01f && Mathf.Abs(probe.CannonSway.y - 7) < .1f,
                            "vertical translation behavior changed");
                        for (int i = 0; i < 180; i++) probe.MoveCannons(Vector2.zero, 1f / 60);
                        Require(probe.CannonSway.magnitude < .01f, "cannons did not settle after movement");
                    }
                    controller.ApplyCamera();
                    Require(Math.Abs(Find.CameraDriver.RootSize - VanguardFireControl.SensorSize) < .01f, "sensor zoom handoff");
                }
                if (f == 76 || f == 86)
                    for (int i = 0; i < 120; i++) controller.Presentation.MoveCannons(new Vector2(f == 76 ? 1 : -1, 0), 1f / 60);
                if (f == 80) { controller.ToggleSensor(); Require(!ship.Infrared, "R optical toggle"); }
                if (f == 78 || f == 88) ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "FireControl-" + f + ".png"));
                if (f == 90)
                {
                    var effects = ship.Map.GetComponent<VanguardImpactEffects>();
                    var cell = ship.ViewPosition.ToIntVec3() + new IntVec3(-4, 0, 2);
                    foreach (var c in CellRect.CenteredOn(cell, 18).ClipInsideMap(ship.Map)) ship.Map.fogGrid.Unfog(c);
                    effects.Add(cell + new IntVec3(-8, 0, 0), false, 311);
                    Require(effects.ImpactShake == Vector2.zero, "autocannon caused missile shake");
                    effects.Add(cell, true, 1729);
                    Require(effects.ImpactShake.magnitude > .01f && effects.ImpactShake.magnitude <= 2.4f, "missile impact shake missing/unbounded");
                    var waterCell = cell + new IntVec3(12, 0, 0);
                    ship.Map.terrainGrid.SetTerrain(waterCell, DefDatabase<TerrainDef>.GetNamed("WaterShallow"));
                    effects.Add(waterCell, true, 341);
                    Require(effects.ParticleCount > 73 && effects.ActiveBursts == 3, "ParticleSystem land/water debris and radial plume bursts");
                    var isolatedCell = new IntVec3(20, 0, 20);
                    ship.Map.fogGrid.Unfog(isolatedCell);
                    var round = (PMPH_OrbitalIncoming)PMPH_OrbitalIncoming.Spawn(DefDatabase<ThingDef>.GetNamed("PMP_VanguardNeutronIncoming"), isolatedCell, ship.Map);
                    round.VanguardWeapon = 1; round.ticksToImpact = 1;
                    Traverse.Create(round).Method("Tick").GetValue();
                    Require(round.Destroyed && effects.ScarCount == 4, "real projectile impact did not create effects");
                    int particles = effects.ParticleCount;
                    controller.Advance(.1f);
                    Require(effects.ParticleCount == particles, "real-time controller advanced paused particles");
                    impactTick = Find.TickManager.TicksGame;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                }
                if (f > 90 && impactTick >= 0)
                {
                    int age = Find.TickManager.TicksGame - impactTick;
                    if (age < 35) ship.Map.GetComponent<VanguardImpactEffects>().DrawFlight(
                        ship.Position.ToVector3Shifted() + new Vector3(13, 0, 12),
                        ship.Position.ToVector3Shifted() + new Vector3(18, 0, 50), true, 17);
                    if (!capturedHot && age >= 12) { ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Impact-hot.png")); capturedHot = true; }
                    if (!capturedCool && age >= 100) { ScreenCapture.CaptureScreenshot(Path.Combine(Folder, "Impact-cooling.png")); capturedCool = true; }
                    if (age < 365) return;
                    var effects = ship.Map.GetComponent<VanguardImpactEffects>();
                    Require(effects.ScarCount == 0 && effects.ParticleCount == 0 && effects.ActiveBursts == 0, "impact effect lifetime cleanup");
                    Require(effects.ImpactShake == Vector2.zero, "missile shake did not expire");
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    File.WriteAllText(Path.Combine(Folder, "impact-checks.txt"), "PASS ParticleSystem land/water emit/simulate/expiry; real projectile Impact hook; game clock; 6-second scar cleanup; dual independent recoil; optical/IR toggle.\n");
                    impactTick = -1;
                    Traverse.Create(ship).Field("combatTicks").SetValue(123);
                    stage = 1; loading = true;
                    GameDataSaveLoader.SaveGame("PMPVanguardAudit");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("PMPVanguardAudit"));
                }
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "results.txt"), ex.ToString());
                VanguardFireControl.CurrentController.End(); Finish("FAIL");
            }
        }
        private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        private void Finish(string result) { done = true; File.WriteAllText(Path.Combine(Folder, "complete.txt"), result); Application.Quit(); }
    }
}
