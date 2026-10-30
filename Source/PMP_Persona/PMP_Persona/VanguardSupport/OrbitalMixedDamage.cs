using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PMP_Persona.VanguardSupport
{
    internal static class OrbitalMixedDamage
    {
        internal const float ArmorPenetration = 2f;
        internal static DamageDef Impact => DefDatabase<DamageDef>.GetNamed("PMP_OrbitalImpact");
        internal static DamageDef Blast => DefDatabase<DamageDef>.GetNamed("BombSuper");
        internal static DamageDef Vaporize => DefDatabase<DamageDef>.GetNamed("Vaporize");

        // Apply full damage per channel through TakeDamage.
        internal static void Apply(Thing target, float amount, Thing instigator = null,
            float angle = -1, ThingDef weapon = null, ThingDef projectile = null, Thing intendedTarget = null,
            bool explosion = false)
        {
            foreach (var type in new[] { Impact, Blast, Vaporize })
            {
                if (target.Destroyed || target is Pawn pawn && pawn.Dead) break;
                var info = new DamageInfo(type, amount, ArmorPenetration, angle, instigator,
                    weapon: weapon, intendedTarget: intendedTarget);
                if (explosion && type.explosionAffectOutsidePartsOnly)
                    info.SetBodyRegion(BodyPartHeight.Undefined, BodyPartDepth.Outside);
                BattleLogEntry_ExplosionImpact log = null;
                if (explosion && target is Pawn)
                {
                    log = new BattleLogEntry_ExplosionImpact(instigator, target, weapon, projectile, type);
                    Find.BattleLog.Add(log);
                }
                var result = target.TakeDamage(info);
                if (log != null) result.AssociateWithLog(log);
                if (explosion && result.wounded && target is Pawn victim && victim.stances != null)
                    victim.stances.stagger.StaggerFor(95);
            }
        }
    }

    // Apply three damage channels to one native explosion's deduplicated target set.
    public sealed class DamageWorker_OrbitalImpact : DamageWorker_AddInjury
    {
        public override void ExplosionStart(Explosion explosion, List<IntVec3> cellsToAffect)
        {
            explosion.armorPenetration = OrbitalMixedDamage.ArmorPenetration;
            base.ExplosionStart(explosion, cellsToAffect);
        }

        protected override void ExplosionDamageThing(Explosion explosion, Thing target,
            List<Thing> damagedThings, List<Thing> ignoredThings, IntVec3 cell)
        {
            if (target.def.category == ThingCategory.Mote || target.def.category == ThingCategory.Ethereal
                || damagedThings.Contains(target)) return;
            damagedThings.Add(target);
            if (ignoredThings != null && ignoredThings.Contains(target)) return;
            float angle = target.Position == explosion.Position ? Rand.RangeInclusive(0, 359)
                : (target.Position - explosion.Position).AngleFlat;
            OrbitalMixedDamage.Apply(target, explosion.GetDamageAmountAt(cell), explosion.instigator, angle,
                explosion.weapon, explosion.projectile, explosion.intendedTarget, true);
        }
    }
}
