using UnityEngine;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public class CompProperties_SupernovaShell : CompProperties
    {
        public float shellDiameter = SupernovaConstants.DefaultShellDiameter;
        public float gasCloudYOffset;
        public float shockwaveYOffset = 0.003f;

        public float preFlashDuration = SupernovaConstants.ShellPreFlashDurationSec;
        public float expansionDuration = SupernovaConstants.ShellExpansionDurationSec;
        public float interactionDuration = SupernovaConstants.ShellInteractionDurationSec;
        public float decayDuration = SupernovaConstants.ShellDecayDurationSec;
        public float loopIdleDuration = SupernovaConstants.ShellLoopIdleDurationSec;
        public float loopFadeOutDuration = SupernovaConstants.ShellLoopFadeOutDurationSec;

        public CompProperties_SupernovaShell()
        {
            compClass = typeof(CompSupernovaShell);
        }

        internal SupernovaShellStageConfig ToStageConfig() => new SupernovaShellStageConfig
        {
            preFlashDuration = preFlashDuration,
            expansionDuration = expansionDuration,
            interactionDuration = interactionDuration,
            decayDuration = decayDuration,
            loopIdleDuration = loopIdleDuration,
            loopFadeOutDuration = loopFadeOutDuration,
            loop = true
        };
    }

    public class CompSupernovaShell : ThingComp
    {
        private int spawnTick = -1;
        public CompProperties_SupernovaShell Props => (CompProperties_SupernovaShell)props;
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (spawnTick < 0) spawnTick = GenTicks.TicksGame;
        }
        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref spawnTick, "shellSpawnTick", -1);
        }
        public void DrawShellVisual(Vector3 center)
        {
            float time = (GenTicks.TicksGame - spawnTick) / 60f;
            var stage = SupernovaShellStageEvaluator.Evaluate(time, Props.ToStageConfig());
            float stellarRadius = parent.GetComp<CompStarVisual>()?.Radius ?? 1f;
            AstroSupernovaRenderer.Draw(center, Props.shellDiameter * .5f, stellarRadius, stage, SupernovaWarpVisuals.StarSeed(parent.Position));
        }
    }
}
