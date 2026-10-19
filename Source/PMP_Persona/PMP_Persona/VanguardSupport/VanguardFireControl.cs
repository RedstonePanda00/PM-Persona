using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using PMP_Persona.PrismSupport;

namespace PMP_Persona.VanguardSupport
{
    public sealed class VanguardFireControl : GameComponent
    {
        internal OrbitalSupportSession Ship;
        internal VanguardPresentation Presentation;
        internal PrismArrivalPresentation PrismArrival;
        private bool inputArmed;
        private Map controlledMap;
        private static VanguardFireControl previousController;
        private int scanFrame;
        private float lastTime = -1;
        internal const float SensorSize = 24;
        internal const float ViewMoveSpeed = 64;
        internal const float ArrivalCloseSize = 22;
        internal static float OverviewSize(OrbitalSupportSession ship) => Mathf.Max(64,
            Mathf.Max(ship.Map.Size.z, ship.Map.Size.x / ((float)UI.screenWidth / Mathf.Max(1, UI.screenHeight))) * .56f);
        internal const float DepartureBlackSeconds = 60f / 60f;
        internal static VanguardFireControl CurrentController => Current.Game?.GetComponent<VanguardFireControl>();
        internal static bool OwnsView => Current.ProgramState == ProgramState.Playing
            && !LongEventHandler.ShouldWaitForEvent
            && CurrentController?.Ship != null && CurrentController.Ship.Spawned
            && !CurrentController.Ship.Destroyed && Find.CurrentMap == CurrentController.Ship.Map;
        public VanguardFireControl(Game game)
        {
            // Release native render resources when a save/new game replaces the previous Game instance.
            previousController?.Presentation?.Dispose();
            previousController?.PrismArrival?.Dispose();
            if (previousController != null) { previousController.Presentation = null; previousController.Ship = null; }
            previousController = this;
        }

        public override void GameComponentUpdate()
        {
            PrismFractureEffects.PruneIdle();
            float now = Time.realtimeSinceStartup;
            float dt = lastTime < 0 ? 0 : Mathf.Clamp(now - lastTime, 0, .1f);
            lastTime = now;
            if (Ship == null)
            {
                if (Find.CurrentMap == null || ++scanFrame % 15 != 0) return;
                foreach (var map in Find.Maps)
                    if (map.GetComponent<OrbitalSupportSession>() is OrbitalSupportSession ship)
                    { Begin(ship); return; }
                return;
            }
            if (!Ship.Spawned || Ship.Destroyed || Find.CurrentMap != Ship.Map)
            { End(); return; }
            if (!Application.isFocused) { inputArmed = false; Ship.FireHeld = false; return; }
            try
            {
                Advance(dt);
                if (Ship == null || !Ship.Ready) return;
                Vector3 move = new Vector3((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                    0, (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
                MoveView(move, dt);
                bool left = Input.GetMouseButton(0), right = Input.GetMouseButton(1);
                Ship.FireHeld = false;
                if (!left && !right) inputArmed = true;
                var mouse = UI.MousePositionOnUIInverted;
                if (!inputArmed || mouse.x < 0 || mouse.y < 0 || mouse.x >= UI.screenWidth || mouse.y >= UI.screenHeight
                    || VanguardPresentation.PauseRect.Contains(mouse)) return;
                IntVec3 target = UI.MouseCell();
                Ship.AimCell = target; Ship.FireHeld = left;
                if (left) Ship.TryFire(Ship.IsPrism ? Ship.SelectedWeapon : 1, target);
                if (right && !Ship.IsPrism) Ship.TryFire(2, target);
            }
            catch (Exception ex)
            {
                Log.Error("[PMP] Vanguard fire control recovered after an error: " + ex);
                End();
            }
        }

        internal void MoveView(Vector3 move, float dt)
        {
            if (Ship == null || !Ship.Ready || dt <= 0) return;
            Vector3 beforeMove = Ship.ViewPosition;
            if (move.sqrMagnitude > 0) Ship.ViewPosition += move.normalized * (ViewMoveSpeed * dt);
            Ship.ViewPosition.x = Mathf.Clamp(Ship.ViewPosition.x, 0, Ship.Map.Size.x);
            Ship.ViewPosition.z = Mathf.Clamp(Ship.ViewPosition.z, 0, Ship.Map.Size.z);
            Vector3 displacement = Ship.ViewPosition - beforeMove;
            if (displacement.sqrMagnitude > .000001f)
                Ship.DesiredBearing = Mathf.Repeat(Mathf.Atan2(displacement.x, displacement.z) * Mathf.Rad2Deg, 360);
            Ship.Bearing = Mathf.Repeat(Mathf.LerpAngle(Ship.Bearing, Ship.DesiredBearing, 1 - Mathf.Exp(-dt * 9)), 360);
            Presentation.MoveCannons(new Vector2(displacement.x, displacement.z) / (ViewMoveSpeed * dt), dt);
        }

        internal void Begin(OrbitalSupportSession ship)
        {
            if (Ship != null) End();
            Ship = ship;
            controlledMap = ship.Map;
            Current.Game.CurrentMap = ship.Map;
            if (!ship.CameraSaved)
            {
                ship.ReturnPosition = Find.CameraDriver.MyCamera.transform.position;
                ship.ViewPosition = ship.ReturnPosition;
                ship.ReturnSize = Find.CameraDriver.RootSize;
                ship.ReturnSpeed = Find.TickManager.CurTimeSpeed;
                ship.CameraSaved = true;
            }
            Find.Selector.ClearSelection();
            Find.Targeter.StopTargeting();
            Find.DesignatorManager.Deselect();
            Presentation = new VanguardPresentation();
            if (ship.IsPrism) PrismArrival = new PrismArrivalPresentation();
            inputArmed = false; lastTime = Time.realtimeSinceStartup;
            if (ship.Phase == OrbitalSupportSession.SupportPhase.Arrival)
            {
                ship.ViewPosition = ship.Map.Center.ToVector3Shifted();
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                ApplyCamera();
            }
        }

        internal void Advance(float dt)
        {
            Presentation.Advance(dt);
            if (Ship.Phase == OrbitalSupportSession.SupportPhase.Arrival)
            {
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                Ship.EntryElapsed = Mathf.Min(Ship.EntryDuration, Ship.EntryElapsed + dt);
                if (Ship.EntryElapsed >= Ship.EntryEnd)
                {
                    PrismArrival?.RestoreWorld();
                    Vector3 focus = VanguardPresentation.ArrivalWorld(Ship, 7);
                    Ship.ViewPosition = new Vector3(focus.x, Ship.ViewPosition.y, focus.z);
                    Ship.Phase = OrbitalSupportSession.SupportPhase.FireControl;
                    Find.TickManager.CurTimeSpeed = Ship.ReturnSpeed;
                    inputArmed = false;
                }
            }
            else if (Ship.Phase == OrbitalSupportSession.SupportPhase.Departure)
            {
                Ship.ExitElapsed += dt;
                if (Ship.ExitElapsed >= DepartureBlackSeconds)
                {
                    End();
                }
            }
        }

        internal void ApplyCamera()
        {
            if (!OwnsView) return;
            if (Ship.IsPrism && Ship.Phase == OrbitalSupportSession.SupportPhase.Arrival)
            { PrismArrival.ApplyCamera(Ship); return; }
            float size = SensorSize;
            if (Ship.Phase == OrbitalSupportSession.SupportPhase.Arrival)
            {
                float t = Ship.EntryElapsed / Ship.EntryDuration * 7;
                size = Mathf.Lerp(Ship.ReturnSize, OverviewSize(Ship), VanguardPresentation.Ease(t / 1.6f));
                // Interpolate magnification ratios so zoom is visible throughout descent,
                // rather than concentrating apparent enlargement near the final small size.
                size = Mathf.Exp(Mathf.Lerp(Mathf.Log(size), Mathf.Log(ArrivalCloseSize), VanguardPresentation.CameraApproach(t)));
            }
            else if (Ship.Phase == OrbitalSupportSession.SupportPhase.Departure)
                size = Ship.ReturnSize;
            Vector3 position = Ship.ViewPosition;
            if (Ship.Phase == OrbitalSupportSession.SupportPhase.Departure) position = Ship.ReturnPosition;
            if (Ship.Phase == OrbitalSupportSession.SupportPhase.Arrival)
            {
                float t = Ship.EntryElapsed / Ship.EntryDuration * 7;
                Vector3 focus = VanguardPresentation.ArrivalWorld(Ship, 7);
                focus.y = position.y;
                position = focus;
                Vector2 shake = VanguardPresentation.ArrivalShake(Ship.EntryElapsed, Ship.EntryDuration);
                float cellPerPixel = size * 2 / Mathf.Max(1, UI.screenHeight);
                position += new Vector3(-shake.x, 0, shake.y) * cellPerPixel;
            }
            else if (Ship.Ready)
            {
                Vector2 shake = Ship.Map.GetComponent<VanguardImpactEffects>().ImpactShake;
                float cellPerPixel = size * 2 / Mathf.Max(1, UI.screenHeight);
                position += new Vector3(-shake.x, 0, shake.y) * cellPerPixel;
            }
            Find.CameraDriver.SetRootPosAndSize(position, size);
        }

        internal void End(bool restoreCamera = true, bool detach = true)
        {
            if (Ship != null && Current.ProgramState == ProgramState.Playing)
            {
                if (Ship.Phase == OrbitalSupportSession.SupportPhase.Arrival)
                    Find.TickManager.CurTimeSpeed = Ship.ReturnSpeed;
                if (restoreCamera && !LongEventHandler.ShouldWaitForEvent && Find.CurrentMap == controlledMap && Ship.CameraSaved)
                    Find.CameraDriver.SetRootPosAndSize(Ship.ReturnPosition, Ship.ReturnSize);
            }
            PrismArrival?.RestoreWorld(); PrismArrival?.Dispose(); PrismArrival = null;
            if (detach && Ship != null) Ship.Map.components.Remove(Ship);
            Ship = null; controlledMap = null; inputArmed = false;
            Presentation?.Dispose(); Presentation = null;
        }

        internal void OnGUI()
        {
            Text.StartOfOnGUI();
            var ev = Event.current;
            if (Ship.Ready && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.Space)
            {
                TogglePause(); ev.Use();
            }
            if (Ship.Ready && ev.type == EventType.KeyDown && ev.keyCode == KeyCode.R)
            { ToggleSensor(); ev.Use(); }
            if (Ship.Ready && Ship.IsPrism && ev.type == EventType.MouseDown && ev.button == 1)
            { Ship.CycleWeapon(); ev.Use(); }
            if (Ship.IsPrism && Ship.Phase == OrbitalSupportSession.SupportPhase.Arrival) PrismArrival.Draw(Ship);
            else Presentation.Draw(Ship);
            if (ev.type == EventType.KeyDown || ev.type == EventType.MouseDown || ev.type == EventType.MouseUp
                || ev.type == EventType.ScrollWheel) ev.Use();
        }
        internal void ToggleSensor() { if (Ship?.Ready == true) Ship.Infrared = !Ship.Infrared; }
        internal void WeaponFired(OrbitalSupportSession source, int weapon, int tick)
        {
            if (Ship != source || Presentation == null) return;
            if (weapon == 1 && !source.IsPrism) Presentation.FireCannon(tick);
            else Presentation.MissileFlash = 1;
        }
        internal static void TogglePause()
        {
            Find.TickManager.CurTimeSpeed = Find.TickManager.CurTimeSpeed == TimeSpeed.Paused
                ? TimeSpeed.Normal : TimeSpeed.Paused;
        }
        internal void Abort(Exception ex)
        {
            Log.Error("[PMP] Vanguard presentation stopped after a rendering error: " + ex);
            End();
        }
    }

    [HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
    internal static class VanguardExclusiveUI
    {
        static bool Prefix()
        {
            if (!VanguardFireControl.OwnsView) return true;
            try { VanguardFireControl.CurrentController.OnGUI(); return false; }
            catch (Exception ex) { VanguardFireControl.CurrentController.Abort(ex); return true; }
        }
    }
    [HarmonyPatch(typeof(CameraDriver), nameof(CameraDriver.Update))]
    internal static class VanguardCamera
    {
        static bool Prefix()
        {
            if (!VanguardFireControl.OwnsView) return true;
            VanguardFireControl.CurrentController.ApplyCamera();
            return false;
        }
    }
    [HarmonyPatch(typeof(Verse.Profile.MemoryUtility), nameof(Verse.Profile.MemoryUtility.ClearAllMapsAndWorld))]
    internal static class VanguardSessionCleanup
    {
        // Scene teardown invalidates the old CameraDriver; never enqueue camera work here.
        static void Prefix()
        {
            VanguardFireControl.CurrentController?.End(false);
            VanguardImpactEffects.ClearAll();
            PrismFractureEffects.ClearAll();
        }
    }
}
