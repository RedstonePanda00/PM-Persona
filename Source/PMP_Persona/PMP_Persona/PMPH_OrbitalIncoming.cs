using PMP_Persona.AirPatrolLegion;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    public class PMPH_OrbitalIncoming : Skyfaller
    {
        private const float MaxAngleDeviation = 10f;
        // Extra cells beyond the map north edge (or view north edge) so the spawn sits off-screen.
        private const int OffscreenMarginCells = 10;
        private const float AccelerationPower = 2.15f;

        private float startX;
        private float startZ;
        private float impactY;
        private int trajectoryTicksMax;
        private bool trajectoryInitialized;
        private readonly AirPatrolMissileVisualFx missileFx = new AirPatrolMissileVisualFx();
        internal int VanguardWeapon;

        public override Vector3 DrawPos
        {
            get
            {
                if (!trajectoryInitialized)
                    return base.DrawPos;
                return ComputeDrawPos(GetFlightProgress());
            }
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad)
            {
                if (!trajectoryInitialized)
                    InitializeTrajectory(map);
                return;
            }

            if (!BeingTransportedOnGravship)
                InitializeTrajectory(map);
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            missileFx.Dispose();
            base.Destroy(mode);
        }

        protected override void Tick()
        {
            base.Tick();
            if (!trajectoryInitialized || Map == null)
                return;

            if (VanguardWeapon != 0) return;

            Vector3 pos = DrawPos;
            missileFx.PushSample(pos);
            missileFx.TickFleckTrail(Map, pos);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (VanguardWeapon != 0 && trajectoryInitialized)
            {
                if (ticksToImpact <= trajectoryTicksMax)
                    Map.GetComponent<VanguardSupport.VanguardImpactEffects>().DrawFlight(DrawPos,
                        new Vector3(startX, impactY, startZ), VanguardWeapon == 2, thingIDNumber);
                return;
            }
            base.DrawAt(drawLoc, flip);
            if (trajectoryInitialized)
                missileFx.Draw();
        }

        protected override void GetDrawPositionAndRotation(ref Vector3 drawLoc, out float extraRotation)
        {
            if (!trajectoryInitialized)
            {
                base.GetDrawPositionAndRotation(ref drawLoc, out extraRotation);
                return;
            }

            drawLoc = ComputeDrawPos(GetFlightProgress());
            extraRotation = ComputeFlightRotation(drawLoc);
            angle = extraRotation;
        }

        public override float DrawAngle()
        {
            if (!trajectoryInitialized || !def.skyfaller.rotateGraphicTowardsDirection)
                return base.DrawAngle();
            return angle;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref startX, "pmpStartX", 0f);
            Scribe_Values.Look(ref startZ, "pmpStartZ", 0f);
            Scribe_Values.Look(ref impactY, "pmpImpactY", 0f);
            Scribe_Values.Look(ref trajectoryTicksMax, "pmpTrajTicksMax", 0);
            Scribe_Values.Look(ref trajectoryInitialized, "pmpTrajInit", false);
            Scribe_Values.Look(ref VanguardWeapon, "vanguardWeapon");
        }

        protected override void Impact()
        {
            if (VanguardWeapon != 0)
                Map.GetComponent<VanguardSupport.VanguardImpactEffects>().Add(Position, VanguardWeapon == 2, thingIDNumber);
            base.Impact();
        }

        private void InitializeTrajectory(Map map)
        {
            Vector3 impact = ImpactDrawPos;
            impactY = impact.y;

            ResolveFixedStartPoint(map, impact, out startX, out startZ);
            trajectoryTicksMax = Mathf.Max(ticksToImpact, 1);
            trajectoryInitialized = true;

            missileFx.Init(thingIDNumber.ToString());
            missileFx.PushSample(ComputeDrawPos(0f));
        }

        // One shared orbital entry lane for the whole map: north off-screen, camera-center X.
        // Does NOT use impact X/Z to place the start point (only enforces startZ > impactZ).
        private static void ResolveFixedStartPoint(Map map, Vector3 impact, out float x, out float z)
        {
            float laneX = map != null ? map.Center.x : impact.x;
            float laneZ = map != null ? map.Size.z - 1 + OffscreenMarginCells : impact.z + OffscreenMarginCells;

            if (map != null && Find.CurrentMap == map && Find.CameraDriver != null)
            {
                laneX = Find.CameraDriver.MapPosition.x + 0.5f;
                float viewNorthZ = Find.CameraDriver.CurrentViewRect.maxZ + OffscreenMarginCells;
                laneZ = Mathf.Max(laneZ, viewNorthZ);
            }

            if (laneZ <= impact.z)
                laneZ = impact.z + 1f;

            float dropSpan = laneZ - impact.z;
            float deviationRad = Rand.Range(-MaxAngleDeviation, MaxAngleDeviation) * Mathf.Deg2Rad;
            x = laneX + dropSpan * Mathf.Tan(deviationRad);
            z = laneZ;
        }

        private Vector3 ImpactDrawPos => GenThing.TrueCenter(this);

        private float GetFlightProgress()
        {
            if (trajectoryTicksMax <= 0 || ticksToImpact > trajectoryTicksMax)
                return 0f;

            float linear = 1f - (float)ticksToImpact / trajectoryTicksMax;
            return Mathf.Pow(Mathf.Clamp01(linear), AccelerationPower);
        }

        private Vector3 ComputeDrawPos(float progress)
        {
            Vector3 impact = ImpactDrawPos;
            progress = Mathf.Clamp01(progress);
            return new Vector3(
                Mathf.Lerp(startX, impact.x, progress),
                impactY,
                Mathf.Lerp(startZ, impact.z, progress));
        }

        private float ComputeFlightRotation(Vector3 currentDrawPos)
        {
            Vector3 impact = ImpactDrawPos;
            Vector3 flat = impact - currentDrawPos;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f)
                return 180f;
            return flat.AngleFlat();
        }
    }
}
