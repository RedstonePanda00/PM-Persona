using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Prometheus
{
    public sealed class Bullet_Scattershot : Bullet
    {
        private bool ricocheted;

        public override int DamageAmount => ricocheted
            ? Mathf.Max(1, Mathf.RoundToInt(base.DamageAmount * 0.75f))
            : base.DamageAmount;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref ricocheted, "scattershotRicocheted");
        }

        protected override void Impact(Thing hitThing, bool blockedByShield = false)
        {
            if (!ricocheted && !blockedByShield && hitThing is Building &&
                hitThing.def.Fillage == FillCategory.Full &&
                !(hitThing is Building_Door door && door.Open))
                TryRicochet(hitThing);
            base.Impact(hitThing, blockedByShield);
        }

        private void TryRicochet(Thing surface)
        {
            // Intersect the incoming ray with the occupied rectangle's entry face.
            // Using the surface boundary avoids launching from inside the wall cell.
            Vector3 direction = (destination - origin).Yto0().normalized;
            CellRect rect = surface.OccupiedRect();
            float tx = Mathf.Abs(direction.x) < 0.0001f ? float.NegativeInfinity
                : ((direction.x > 0f ? rect.minX : rect.maxX + 1f) - origin.x) / direction.x;
            float tz = Mathf.Abs(direction.z) < 0.0001f ? float.NegativeInfinity
                : ((direction.z > 0f ? rect.minZ : rect.maxZ + 1f) - origin.z) / direction.z;
            float entry = Mathf.Max(tx, tz);
            if (entry < 0f || float.IsInfinity(entry)) return;
            Vector3 normal = tx > tz
                ? new Vector3(-Mathf.Sign(direction.x), 0f, 0f)
                : new Vector3(0f, 0f, -Mathf.Sign(direction.z));
            Vector3 start = origin.Yto0() + direction * entry + normal * 0.06f;
            if (!start.InBounds(Map) || rect.Contains(start.ToIntVec3())) return;

            var bounce = (Bullet_Scattershot)GenSpawn.Spawn(def, start.ToIntVec3(), Map);
            bounce.ricocheted = true;
            bounce.origin = start;
            bounce.destination = start + Vector3.Reflect(direction, normal) * 8f;
            bounce.launcher = launcher;
            bounce.equipment = equipment;
            bounce.equipmentDef = equipmentDef;
            bounce.equipmentQuality = equipmentQuality;
            bounce.damageDefOverride = damageDefOverride;
            bounce.extraDamages = extraDamages;
            bounce.stoppingPower = stoppingPower;
            bounce.preventFriendlyFire = preventFriendlyFire;
            bounce.usedTarget = bounce.destination.ToIntVec3();
            bounce.intendedTarget = bounce.usedTarget;
            bounce.HitFlags = ProjectileHitFlags.All;
            bounce.ticksToImpact = Mathf.Max(1, Mathf.CeilToInt(bounce.StartingTicksToImpact));
            bounce.lifetime = bounce.ticksToImpact;
            // This is continuation of one pellet, not another weapon shot: do not
            // call Launch and cause the framework's dual-wield hook to duplicate it.
            FleckMaker.Static(start, Map, FleckDefOf.ShotFlash, 0.35f);
        }
    }
}
