using ProtossMech;
using UnityEngine;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    [StaticConstructorOnStartup]
    internal static class SupernovaWarpVisuals
    {
        private static readonly MaterialPropertyBlock ruptureBlock = new MaterialPropertyBlock();
        internal const float RevealStart = .6f;
        // PMC starts its black hole strictly AFTER 0.099. Never enter that phase.
        internal const float RuptureEnd = .099f;
        internal static int StarSeed(IntVec3 cell) => unchecked((cell.x * 73856093 ^ cell.z * 19349663) & int.MaxValue) % 997;
        internal static float RuptureProgress(float progress) => RuptureEnd * Mathf.Clamp01(progress / RevealStart);
        internal static float RuptureShrink(float progress) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.7f, 1f, progress));

        internal static void Appearance(float progress, float footprint, out float scale, out float visibility, out Vector3 offset)
        {
            float reveal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(RevealStart, 1f, progress));
            // Reuse PMC's cubic enlargement, 18% overshoot, settling and damped tremor.
            float envelopeProgress = RuptureEnd + .09f * .7f * reveal;
            WarpBlackHoleEnvelope.Evaluate(envelopeProgress, 0f, out scale, out _, out visibility);
            offset = WarpBlackHoleEnvelope.PositionOffset(envelopeProgress, 0f, footprint);
        }

        internal static void Draw(IntVec3 cell, int ticksRemaining, int totalTicks)
        {
            float progress = Mathf.Clamp01(1f - ticksRemaining / (float)Mathf.Max(1, totalTicks));
            float elapsed = (totalTicks - ticksRemaining) / 60f;
            float radius = DefDatabase<ThingDef>.GetNamed(SupernovaConstants.SupernovaDefName)
                .GetCompProperties<CompProperties_StarVisual>()?.radius ?? 1f;
            float footprint = radius * 2f;
            Vector3 center = cell.ToVector3Shifted();
            center.y = AltitudeLayer.MoteOverhead.AltitudeFor() + radius;
            // Core draws the original singularity/refraction/fractures; its black-hole branch has zero scale.
            if (!WarpBlackHoleVisual.TryDraw(center, footprint, Rot4.North, RuptureProgress(progress),
                    RuptureShrink(progress), elapsed, ruptureBlock))
                Log.ErrorOnce("[PMP] PMC singularity/space-rupture effect is unavailable. Check the Core 1.7 installation.", 168435182);
            Appearance(progress, footprint, out float scale, out float visibility, out Vector3 offset);
            if (scale <= .001f || visibility <= .001f) return;
            var stage = new SupernovaShellStageState {
                localTime = (totalTicks - ticksRemaining - SupernovaConstants.WarpDeployTicks) / 60f,
                totalDuration = 1, visibility = 0
            };
            AstroSupernovaRenderer.Draw(center + offset, radius * scale * 1.8f, radius * scale,
                stage, StarSeed(cell), visibility, center.y);
        }
    }
}
