using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public static class SupernovaFieldUtility
    {
        private static HediffDef cachedExposureDef;
        private static DamageDef cachedVaporizeDef;

        public static HediffDef ExposureHediffDef =>
            cachedExposureDef ?? (cachedExposureDef =
                DefDatabase<HediffDef>.GetNamedSilentFail(SupernovaConstants.ExposureHediffDefName));

        public static DamageDef VaporizeDamageDef =>
            cachedVaporizeDef ?? (cachedVaporizeDef =
                DefDatabase<DamageDef>.GetNamedSilentFail("Vaporize") ?? DamageDefOf.Burn);

        public static bool IsPawnInAnySupernovaField(Pawn pawn, IReadOnlyList<Thing_Supernova> instances)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || instances == null || instances.Count == 0)
                return false;
            return IsCellInAnySupernovaField(pawn.Position, instances);
        }

        public static bool IsCellInAnySupernovaField(IntVec3 cell, IReadOnlyList<Thing_Supernova> instances)
        {
            if (instances == null || instances.Count == 0)
                return false;

            for (int i = 0; i < instances.Count; i++)
            {
                Thing_Supernova sn = instances[i];
                if (sn == null || !sn.Spawned || sn.Destroyed)
                    continue;
                if (IsWithinField(cell, sn.Position))
                    return true;
            }

            return false;
        }

        public static bool IsWithinField(IntVec3 cell, IntVec3 center)
        {
            return cell.InHorDistOf(center, SupernovaConstants.FieldRadiusCells);
        }

        public static bool IsValidHostilePawn(Pawn pawn, Faction playerFaction)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
                return false;
            if (pawn.def?.category != ThingCategory.Pawn)
                return false;
            if (playerFaction == null)
                return true;
            return pawn.Faction != playerFaction;
        }

        public static bool IsValidHostileBuilding(Thing thing, Faction playerFaction)
        {
            if (thing == null || thing.Destroyed)
                return false;
            if (thing.def?.category != ThingCategory.Building || !thing.def.useHitPoints)
                return false;
            if (playerFaction == null)
                return true;
            return thing.Faction != playerFaction;
        }

        public static Thing_Supernova FindInstigatorForCell(IntVec3 cell, IReadOnlyList<Thing_Supernova> instances)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                Thing_Supernova sn = instances[i];
                if (sn == null || !sn.Spawned || sn.Destroyed)
                    continue;
                if (IsWithinField(cell, sn.Position))
                    return sn;
            }

            return null;
        }

        public static void ApplyVaporizeDamage(Thing target, Thing instigator, float amount = SupernovaConstants.DamagePerPulse)
        {
            if (target == null || target.Destroyed)
                return;

            var dinfo = new DamageInfo(VaporizeDamageDef, amount, 0f, -1f, instigator, null, null);
            target.TakeDamage(dinfo);
        }

        public static void ApplyBootstrapHits(Pawn pawn, Thing instigator)
        {
            for (int i = 0; i < SupernovaConstants.BootstrapHitCount; i++)
                ApplyVaporizeDamage(pawn, instigator);
        }

        public static Hediff GetExposureHediff(Pawn pawn)
        {
            HediffDef def = ExposureHediffDef;
            if (pawn == null || def == null)
                return null;
            return pawn.health.hediffSet.GetFirstHediffOfDef(def);
        }

        public static bool HasExposureHediff(Pawn pawn) => GetExposureHediff(pawn) != null;
    }
}
