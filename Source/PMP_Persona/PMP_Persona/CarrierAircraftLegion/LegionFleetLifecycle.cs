using UnityEngine;

namespace PMP_Persona.CarrierAircraftLegion
{
    public partial class Building_CarrierOrbiter
    {
        private bool AllDronesDeparted
        {
            get { foreach (var d in flockIndex) if (d.flightState != 9) return false; return true; }
        }

        private void TickFleetLifecycle(ClusterState c, DroneState d, int now)
        {
            if (ageTicks >= LifeTimeTicks)
            {
                // No return teleport after retirement. Issued attacks finish their salvos first.
                if (d.flightState == 5 || d.flightState == 7) { d.flightState = 9; return; }
                if (d.flightState == 4) { d.flightState = 8; return; }
                int stagger = d.fadeBatch * 12 + Mathf.FloorToInt(d.phase * 1.4f);
                if (ageTicks >= LifeTimeTicks + stagger && (d.flightState == 0 || d.flightState == 3 || d.flightState == 6))
                    BeginWarpOut(d, now);
                return;
            }
            if (d.flightState == 7 && now >= d.warpStartTick)
            {
                d.flightDirXZ = c.velocity.sqrMagnitude > .001f ? c.velocity.normalized : Vector2.right;
                // Resolve a live formation position while still invisible.
                d.position = KeepInside(HomeTarget(c, d) - d.flightDirXZ * 4);
                d.velocity = c.velocity + d.flightDirXZ * .25f;
                d.flightState = 6; d.warpStartTick = now;
            }
        }

        internal void AuditFleetLifecycle()
        {
            int now = Verse.Find.TickManager.TicksGame;
            SetupOrbit(Map.Center);
            foreach (var d in flockIndex) if (d.flightState != 7) throw new System.Exception("Fleet appeared without entry warp");
            bool entered = false;
            for (int t = 1; t <= 240; t++)
            {
                ageTicks++; TickFlock(now + t);
                foreach (var d in flockIndex) entered |= d.flightState == 6;
            }
            foreach (var d in flockIndex) if (d.flightState == 7) throw new System.Exception("Entry wave stuck");
            if (!entered) throw new System.Exception("No entry warp phase");
            // An in-flight salvo must finish even when the sixty-second dispatch window expires.
            var head = flockIndex[0]; head.flightState = 2; head.bombardmentEndTick = now + 270;
            head.flightDirXZ = Vector2.right;
            ageTicks = LifeTimeTicks - 1;
            bool departed = false;
            for (int t = 241; t <= 400; t++)
            {
                ageTicks++; TickFlock(now + t);
                if (t < 270 && head.flightState != 2) throw new System.Exception("Final salvo interrupted");
                foreach (var d in flockIndex) departed |= d.flightState == 8;
            }
            if (!departed || !AllDronesDeparted) throw new System.Exception("Fleet retirement failed");
            DispatchOneDroneToTarget(0, Map.Center, out int travel, out _);
            if (travel != 0) throw new System.Exception("Dispatch after retirement");
            SetupOrbit(Map.Center);
        }

        internal void PrepareFleetAuditPose(bool departure, int age)
        {
            SetupOrbit(Map.Center);
            int now = Verse.Find.TickManager.TicksGame;
            ageTicks = departure ? LifeTimeTicks + age : age;
            foreach (var d in flockIndex)
            {
                int localAge = age - d.fadeBatch * (departure ? 12 : FadeInStaggerTicks);
                d.flightDirXZ = d.velocity.sqrMagnitude > .001f ? d.velocity.normalized : Vector2.right;
                d.warpStartTick = now - Mathf.Max(0, localAge);
                d.flightState = (byte)(departure ? (localAge < 0 ? 0 : localAge < WarpOutTicks ? 8 : 9)
                    : (localAge < 0 ? 7 : localAge < WarpInTicks ? 6 : 0));
            }
        }
    }
}
