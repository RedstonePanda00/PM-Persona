using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Prometheus
{
    public sealed class IncinerationCluster : DefModExtension
    {
        public ThingDef submunition;
        public int count = 4;
        public FloatRange scatterRadius = new FloatRange(2.5f, 4f);
    }

    public sealed class Projectile_Incineration : Projectile_Explosive
    {
        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            // Preserve the native intercepted explosion, without releasing a cluster
            // through the shield. Ordinary hits use our Explode override below.
            if (blockedByShield) base.Explode();
            else base.Impact(hitThing);
        }

        protected override void Explode()
        {
            Map map = Map;
            IntVec3 cell = Position;
            Thing source = launcher;
            Thing weapon = equipment;
            IncinerationCluster cluster = def.GetModExtension<IncinerationCluster>();
            base.Explode();
            if (map == null || cluster?.submunition == null) return;

            Vector3 start = cell.ToVector3Shifted();
            float rotation = Rand.Range(0f, 360f);
            for (int i = 0; i < cluster.count; i++)
            {
                float angle = rotation + (i + Rand.Range(-0.2f, 0.2f)) * 360f / cluster.count;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;
                Vector3 end = start + direction * cluster.scatterRadius.RandomInRange;
                end.x = Mathf.Clamp(end.x, 0.01f, map.Size.x - 0.01f);
                end.z = Mathf.Clamp(end.z, 0.01f, map.Size.z - 0.01f);
                Projectile child = (Projectile)GenSpawn.Spawn(cluster.submunition, cell, map);
                LocalTargetInfo target = FirstWallOrEndpoint(start, end, map);
                // Use native explosive flight, impact and saved fuse. Vanilla skips
                // free interception within five cells, so target intervening walls.
                child.Launch(source, start, target, target, ProjectileHitFlags.All, false, weapon);
            }
        }

        private static LocalTargetInfo FirstWallOrEndpoint(Vector3 start, Vector3 end, Map map)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt((end - start).magnitude * 4f));
            IntVec3 originCell = start.ToIntVec3();
            for (int i = 1; i <= steps; i++)
            {
                IntVec3 cell = Vector3.Lerp(start, end, i / (float)steps).ToIntVec3();
                if (cell == originCell) continue;
                Building wall = cell.GetEdifice(map);
                if (wall?.def.Fillage == FillCategory.Full && !(wall is Building_Door door && door.Open))
                    return wall;
            }
            return end.ToIntVec3();
        }
    }
}
