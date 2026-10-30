using RimWorld;
using Verse;
using System.Collections.Generic;
using System.Linq;

namespace PMP_Persona
{
    public class CompProperties_SplashOnMelee : CompProperties
    {
        public float splashDamageFactor = 0.5f;
        public float splashRadius = 1.0f;
        public DamageDef splashDamageDef = null;

        public CompProperties_SplashOnMelee()
        {
            this.compClass = typeof(CompSplashOnMelee);
        }
    }

    public class CompSplashOnMelee : ThingComp
    {
        public CompProperties_SplashOnMelee Props => (CompProperties_SplashOnMelee)this.props;

        private const string DefNameLegionnaire = "PM_Legionnaire";
        private const string DefNameKaldalis = "PMPH_Kaldalis";
        private const float SplashBonusPerLegionnaire = 0.67f;

        private static bool IsSplashableStructure(Thing t)
        {
            if (t == null || t is Pawn || !t.Spawned || !t.def.useHitPoints) return false;
            return t.def.category == ThingCategory.Building;
        }

        public static bool IsSplashMeleeNotifyVictim(Thing victim)
        {
            if (victim == null || !victim.Spawned) return false;
            if (victim is Pawn) return true;
            return IsSplashableStructure(victim);
        }

        public void Notify_DamageDealt(DamageInfo originalDinfo, Thing victim)
        {
            Pawn attacker = this.parent as Pawn;

            if (attacker == null || victim == null || !victim.Spawned || victim.Map == null || originalDinfo.Amount <= 0)
            {
                return;
            }
            
            if (originalDinfo.Def.isExplosive || (originalDinfo.Weapon != null && originalDinfo.Weapon.IsRangedWeapon))
            {
                return;
            }

            Map map = victim.Map;
            IntVec3 center = victim.Position;

            float effectiveSplashFactor = this.Props.splashDamageFactor;
            if (attacker.def?.defName == DefNameKaldalis && map != null)
            {
                int legionnaireCount = SpawnedPawnCountCache.GetCountInFaction(map, DefNameLegionnaire, attacker.Faction);
                effectiveSplashFactor += legionnaireCount * SplashBonusPerLegionnaire;
            }

            float splashDamageAmount = originalDinfo.Amount * effectiveSplashFactor;
            DamageDef damageDefToUse = this.Props.splashDamageDef ?? originalDinfo.Def;
            float splashArmorPenetration = splashDamageAmount * 0.015f;

            DamageInfo splashDinfo = new DamageInfo(
                def: damageDefToUse,
                amount: splashDamageAmount,
                armorPenetration: splashArmorPenetration,
                angle: -1f,
                instigator: attacker,
                weapon: originalDinfo.Weapon
            );

            if (victim is Pawn victimPawn)
            {
                if (victimPawn.HostileTo(attacker))
                {
                    victimPawn.TakeDamage(splashDinfo);
                    MoteMaker.MakeStaticMote(victimPawn.Position, map, ThingDefOf.Mote_Stun, 0.8f);
                }
            }
            else if (IsSplashableStructure(victim) && attacker.HostileTo(victim))
            {
                victim.TakeDamage(splashDinfo);
                MoteMaker.MakeStaticMote(victim.Position, map, ThingDefOf.Mote_Stun, 0.8f);
                return; // Apply backup damage only to primary buildings.
            }

            IEnumerable<Thing> potentialTargets = GenRadial.RadialDistinctThingsAround(center, map, this.Props.splashRadius, false);

            foreach (Thing target in potentialTargets)
            {
                if (target == victim) continue;
                if (target == attacker) continue;

                if (target is Pawn targetPawn)
                {
                    if (targetPawn.HostileTo(attacker))
                    {
                        targetPawn.TakeDamage(splashDinfo);
                        MoteMaker.MakeStaticMote(targetPawn.Position, map, ThingDefOf.Mote_Stun, 0.8f);
                    }
                }
                else if (IsSplashableStructure(target) && attacker.HostileTo(target))
                {
                    target.TakeDamage(splashDinfo);
                    MoteMaker.MakeStaticMote(target.Position, map, ThingDefOf.Mote_Stun, 0.8f);
                }
            }
        }
    }
}