using UnityEngine;

namespace PMP_Persona.SupernovaSupport
{
    public struct SupernovaShellStageConfig
    {
        public float preFlashDuration;
        public float expansionDuration;
        public float interactionDuration;
        public float decayDuration;
        public float loopIdleDuration;
        public float loopFadeOutDuration;
        public bool loop;

        public static SupernovaShellStageConfig Default => new SupernovaShellStageConfig
        {
            preFlashDuration = SupernovaConstants.ShellPreFlashDurationSec,
            expansionDuration = SupernovaConstants.ShellExpansionDurationSec,
            interactionDuration = SupernovaConstants.ShellInteractionDurationSec,
            decayDuration = SupernovaConstants.ShellDecayDurationSec,
            loopIdleDuration = SupernovaConstants.ShellLoopIdleDurationSec,
            loopFadeOutDuration = SupernovaConstants.ShellLoopFadeOutDurationSec,
            loop = true
        };

        public float ActiveDuration =>
            Mathf.Max(0.05f, preFlashDuration + expansionDuration + interactionDuration + decayDuration);

        public float CycleDuration => ActiveDuration + Mathf.Max(loopIdleDuration, 0f);
    }

    public struct SupernovaShellStageState
    {
        public float localTime;
        public float preFlash;
        public float expansion;
        public float interaction;
        public float decay;
        public float progress;
        public float colorCool01;
        public float shockFront01;
        public float hotspotActivity;
        public float visibility;
        public float totalDuration;
        public float preFlashDuration;
    }

    public static class SupernovaShellStageEvaluator
    {
        public static SupernovaShellStageState Evaluate(float visualTimeSec, SupernovaShellStageConfig config)
        {
            float activeDuration = config.ActiveDuration;
            float cycleDuration = config.CycleDuration;
            float stageTime = config.loop
                ? Mathf.Repeat(visualTimeSec, Mathf.Max(cycleDuration, 1e-4f))
                : Mathf.Clamp(visualTimeSec, 0f, activeDuration);

            bool isIdleWindow = config.loop && stageTime > activeDuration;
            float idleTime = isIdleWindow ? stageTime - activeDuration : 0f;

            var state = new SupernovaShellStageState
            {
                localTime = visualTimeSec,
                totalDuration = activeDuration,
                preFlashDuration = config.preFlashDuration
            };

            if (isIdleWindow)
            {
                state.preFlash = 1f;
                state.expansion = 1f;
                state.interaction = 1f;
                state.decay = 1f;
                state.progress = 1f;
                state.colorCool01 = 0f;
                state.shockFront01 = 1f;
                state.hotspotActivity = 0f;

                float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, idleTime / Mathf.Max(config.loopFadeOutDuration, 1e-4f));
                state.visibility = Safe01(fadeOut);
            }
            else
            {
                EvaluateExplosion(stageTime, activeDuration, config, out state.preFlash, out state.expansion,
                    out state.interaction, out state.decay, out state.progress, out state.colorCool01,
                    out state.shockFront01, out state.hotspotActivity);
                state.visibility = 1f;
            }

            return state;
        }

        private static void EvaluateExplosion(
            float stageTime,
            float totalDuration,
            SupernovaShellStageConfig config,
            out float preFlash,
            out float expansion,
            out float interaction,
            out float decay,
            out float progress,
            out float colorCool01,
            out float shockFront01,
            out float hotspotActivity)
        {
            float t = stageTime;
            float t0 = config.preFlashDuration;
            float t1 = t0 + config.expansionDuration;
            float t2 = t1 + config.interactionDuration;

            preFlash = 0f;
            expansion = 0f;
            interaction = 0f;
            decay = 0f;

            if (t <= t0)
            {
                preFlash = Safe01(t / Mathf.Max(config.preFlashDuration, 1e-4f));
            }
            else if (t <= t1)
            {
                preFlash = 1f;
                expansion = Safe01((t - t0) / Mathf.Max(config.expansionDuration, 1e-4f));
            }
            else if (t <= t2)
            {
                preFlash = 1f;
                expansion = 1f;
                interaction = Safe01((t - t1) / Mathf.Max(config.interactionDuration, 1e-4f));
            }
            else
            {
                preFlash = 1f;
                expansion = 1f;
                interaction = 1f;
                decay = Safe01((t - t2) / Mathf.Max(config.decayDuration, 1e-4f));
            }

            progress = Safe01(stageTime / Mathf.Max(totalDuration, 1e-4f));
            colorCool01 = Safe01(1f - Mathf.Pow(progress, 0.58f));

            float travel01 = progress;
            shockFront01 = 1f - Mathf.Pow(1f - travel01, 1.55f);
            hotspotActivity = Safe01(interaction * (1f - decay * 0.65f));
        }

        private static float Safe01(float v) => Mathf.Clamp01(v);
    }
}
