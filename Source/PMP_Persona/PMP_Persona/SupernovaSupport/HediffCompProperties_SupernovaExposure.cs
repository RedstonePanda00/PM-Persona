using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public class HediffCompProperties_SupernovaExposure : HediffCompProperties
    {
        public float severityPerPulse = SupernovaConstants.ExposureSeverityPerPulse;

        public HediffCompProperties_SupernovaExposure()
        {
            compClass = typeof(HediffComp_SupernovaExposure);
        }
    }
}
