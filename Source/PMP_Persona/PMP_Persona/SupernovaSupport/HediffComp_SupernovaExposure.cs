using RimWorld;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public class HediffComp_SupernovaExposure : HediffComp
    {
        private int ticksUntilPulse = SupernovaConstants.HediffPulseIntervalTicks;

        private HediffCompProperties_SupernovaExposure PropsTyped =>
            (HediffCompProperties_SupernovaExposure)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || pawn.Destroyed)
                return;

            Map map = pawn.Map;
            if (map == null)
                return;

            MapComponent_SupernovaField field = MapComponent_SupernovaField.Get(map);
            if (field == null || field.Instances.Count == 0)
            {
                RemoveExposure();
                return;
            }

            if (!SupernovaFieldUtility.IsPawnInAnySupernovaField(pawn, field.Instances))
            {
                RemoveExposure();
                return;
            }

            ticksUntilPulse--;
            if (ticksUntilPulse > 0)
                return;

            ticksUntilPulse = SupernovaConstants.HediffPulseIntervalTicks;

            Thing_Supernova instigator = SupernovaFieldUtility.FindInstigatorForCell(pawn.Position, field.Instances);
            SupernovaFieldUtility.ApplyVaporizeDamage(pawn, instigator);

            severityAdjustment += PropsTyped.severityPerPulse;
        }

        public override void CompPostPostRemoved()
        {
            base.CompPostPostRemoved();
            Pawn pawn = Pawn;
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Map == null)
                return;

            MapComponent_SupernovaField.Get(pawn.Map)?.AddToPendingPawns(pawn);
        }

        private void RemoveExposure()
        {
            Pawn pawn = Pawn;
            if (pawn?.health == null)
                return;
            pawn.health.RemoveHediff(parent);
        }

        public override void CompExposeData()
        {
            base.CompExposeData();
            Scribe_Values.Look(ref ticksUntilPulse, "ticksUntilPulse", SupernovaConstants.HediffPulseIntervalTicks);
        }
    }
}
