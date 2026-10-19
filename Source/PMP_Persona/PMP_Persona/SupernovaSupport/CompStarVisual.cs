using UnityEngine;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public class CompProperties_StarVisual : CompProperties
    {
        public float radius = 1f;
        public CompProperties_StarVisual() { compClass = typeof(CompStarVisual); }
    }
    public class CompStarVisual : ThingComp
    {
        public float Radius => ((CompProperties_StarVisual)props).radius;
        public void DrawStarVisual(Vector3 center)
        {
            var stage = new SupernovaShellStageState { localTime = GenTicks.TicksGame / 60f, visibility = 0, totalDuration = 1 };
            AstroSupernovaRenderer.Draw(center, Radius * 1.8f, Radius, stage, parent.thingIDNumber);
        }
    }
}
