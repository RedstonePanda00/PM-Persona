using System.Collections.Generic;
using ProtossMech;
using RimWorld;
using Verse;

namespace PMP_Persona
{
    // Self-cast area execute + warp blade spin at the caster's current cell (no teleport).
    public class CompAbilityEffect_ZeratulCycloneStrike : CompAbilityEffect
    {
        public new CompProperties_ZeratulCycloneStrike Props => (CompProperties_ZeratulCycloneStrike)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);

            Pawn caster = parent.pawn;
            Map map = caster?.Map;
            if (caster == null || map == null)
            {
                return;
            }

            caster.TryGetComp<CompUmbraWarpBladeRenderer>()?.Notify_UmbraSkipLanded();

            IntVec3 center = caster.Position;
            DamageDef dmgDef = Props.damageDef ?? DamageDefOf.ExecutionCut;
            float amount = Props.damageAmount;
            float radius = Props.damageRadius;

            int numCells = GenRadial.NumCellsInRadius(radius);
            for (int i = 0; i < numCells; i++)
            {
                IntVec3 c = center + GenRadial.RadialPattern[i];
                if (!c.InBounds(map))
                {
                    continue;
                }

                List<Thing> things = c.GetThingList(map);
                for (int j = 0; j < things.Count; j++)
                {
                    if (things[j] is Pawn p && p.HostileTo(caster))
                    {
                        p.TakeDamage(new DamageInfo(dmgDef, amount, 999f, -1f, caster));
                    }
                }
            }
        }
    }

    public class CompProperties_ZeratulCycloneStrike : CompProperties_AbilityEffect
    {
        public float damageAmount = 100f;

        public float damageRadius = 2.9f;

        public DamageDef damageDef;

        public CompProperties_ZeratulCycloneStrike()
        {
            compClass = typeof(CompAbilityEffect_ZeratulCycloneStrike);
        }
    }
}
