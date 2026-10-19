using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public class Hediff_SupernovaExposure : HediffWithComps
    {
        private HediffStage cachedStage;
        private float lastSeverity = -1f;

        public override HediffStage CurStage
        {
            get
            {
                if (lastSeverity != Severity)
                {
                    lastSeverity = Severity;
                    cachedStage = BuildStage(Severity);
                }

                return cachedStage;
            }
        }

        private static HediffStage BuildStage(float severity)
        {
            var stage = new HediffStage
            {
                painFactor = SupernovaConstants.ExposurePainFactorBase
                    + severity * SupernovaConstants.ExposurePainFactorPerSeverity,
                statOffsets = new List<StatModifier>
                {
                    new StatModifier
                    {
                        stat = StatDefOf.ArmorRating_Heat,
                        value = -severity * SupernovaConstants.ExposureHeatArmorOffsetPerSeverity
                    }
                }
            };
            return stage;
        }
    }
}
