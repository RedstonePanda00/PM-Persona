using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;
using PMP_Persona.PrismSupport;
using PMP_Persona.VanguardSupport;

namespace PMP_Persona
{
    public partial class Thing_MobileBeam
    {
        internal const int GuidanceTicks = 72;
        private const int VisualLifetime = 3600;
        private int visualStartTick, lastScorchTick = -999999;
        private bool beamActivated;
        private IntVec3 queuedDestination = IntVec3.Invalid;
        private Material columnMaterial, contactMaterial;
        private readonly Material[] guidanceMaterials = new Material[3];
        private static readonly HashSet<Thing_MobileBeam> renderedBeams = new HashSet<Thing_MobileBeam>();
        internal int VisualAge => Find.TickManager.TicksGame - visualStartTick;

        public override void SetDestination(IntVec3 target)
        {
            if (VisualAge < GuidanceTicks) queuedDestination = target;
            else base.SetDestination(target);
        }

        private void ActivatePurification()
        {
            if (beamActivated) return;
            beamActivated = true;
            GetComp<CompAffectsSky>()?.StartFadeInHoldFadeOut(30, VisualLifetime - 45, 15);
            if (queuedDestination.IsValid) { base.SetDestination(queuedDestination); queuedDestination = IntVec3.Invalid; }
        }

        private void DrawPurification()
        {
            if (!Spawned || Map != Find.CurrentMap || Position.Fogged(Map)) return;
            renderedBeams.Add(this);
            float age = VisualAge - GuidanceTicks;
            if (age < 0)
            {
                float p = Mathf.Clamp01(VisualAge / (float)GuidanceTicks);
                float radius = 8 * (1 - VanguardPresentation.Ease(p));
                for (int i = 0; i < 3; i++)
                {
                    guidanceMaterials[i] ??= PersonaMaterials.Create("PMP17_PrismFractureMaterial");
                    float angle = i * Mathf.PI * 2 / 3 + p * 2.4f;
                    Vector3 at = DrawPos + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                    OrbitalBeamVolume.Draw(guidanceMaterials[i], at, 1.5f, new Vector4(VisualAge / 60f, 0, .8f, i));
                }
                if (age < -8) return;
            }
            float visibility = Mathf.SmoothStep(0, 1, (age + 8) / 30) * Mathf.SmoothStep(0, 1, (VisualLifetime - age) / 15);
            if (visibility <= 0) return;
            columnMaterial ??= PersonaMaterials.Create("PMP17_PurificationMaterial");
            contactMaterial ??= PersonaMaterials.Create("PMP17_PurificationContactMaterial");
            Vector3 point = DrawPos;
            // A narrower luminous core surrounded by surging plasma, inside the damage footprint.
            OrbitalBeamVolume.Draw(columnMaterial, point, BeamRadius * .58f,
                new Vector4((age + 8) / 60, 1, visibility, thingIDNumber % 97));
            if (age < 0) return;
            point.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            contactMaterial.SetVector("_Effect", new Vector4(age / 60, 0, visibility, thingIDNumber % 97));
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(point, Quaternion.identity,
                new Vector3(BeamRadius * 2, 1, BeamRadius * 2)), contactMaterial, 0, null, 0,
                (MaterialPropertyBlock)null, ShadowCastingMode.Off, false, renderQueue: 3015);
        }

        private void TickPurificationVisuals()
        {
            int now = Find.TickManager.TicksGame;
            if (now - lastScorchTick < 30) return;
            lastScorchTick = now;
            Map.GetComponent<VanguardImpactEffects>()?.AddScorchOnly(DrawPos, BeamRadius * 2.2f, thingIDNumber);
        }

        private void ReleasePurificationMaterials()
        {
            renderedBeams.Remove(this);
            if (columnMaterial != null) Object.Destroy(columnMaterial);
            if (contactMaterial != null) Object.Destroy(contactMaterial);
            columnMaterial = contactMaterial = null;
            for (int i = 0; i < guidanceMaterials.Length; i++)
            { if (guidanceMaterials[i] != null) Object.Destroy(guidanceMaterials[i]); guidanceMaterials[i] = null; }
        }

        internal static void ReleaseAllPurificationMaterials()
        {
            foreach (var beam in new List<Thing_MobileBeam>(renderedBeams)) beam.ReleasePurificationMaterials();
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
    internal static class PurificationCleanup
    { static void Prefix() => Thing_MobileBeam.ReleaseAllPurificationMaterials(); }
}
