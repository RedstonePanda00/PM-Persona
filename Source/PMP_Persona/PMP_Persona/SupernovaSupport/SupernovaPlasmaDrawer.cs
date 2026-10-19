using System.Collections.Generic;
using PMP_Persona.Rendering;
using UnityEngine;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    // Supernova-specific exposure adapter; silhouette capture is shared with other supports.
    internal static class SupernovaPlasmaDrawer
    {
        private sealed class Exposure
        {
            internal float Fade, Intensity;
            internal Vector3 Flow;
            internal int Seen;
        }
        private static readonly Dictionary<Pawn, Exposure> States = new Dictionary<Pawn, Exposure>();
        private static readonly List<Pawn> Stale = new List<Pawn>();
        private static int lastFrame = -1;
        private static Map map;

        public static void DrawForMap(Map current)
        {
            if (current == null || current != Find.CurrentMap || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            if (map != current) { States.Clear(); map = current; }
            var field = MapComponent_SupernovaField.Get(map);
            if (field.Instances.Count == 0 && States.Count == 0) return;
            float dt = Find.TickManager.Paused ? 0 : Mathf.Clamp(Time.deltaTime, 0f, .1f);
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || pawn.health == null) continue;
                var exposure = SupernovaFieldUtility.GetExposureHediff(pawn);
                if (exposure == null) continue;
                var source = SupernovaFieldUtility.FindInstigatorForCell(pawn.Position, field.Instances);
                if (source == null) continue;
                if (!States.TryGetValue(pawn, out var state)) { state = new Exposure(); States.Add(pawn, state); }
                state.Seen = Time.frameCount;
                state.Flow = pawn.DrawPos - source.DrawPos;
                state.Intensity = Mathf.Clamp01(exposure.Severity / SupernovaConstants.PlasmaSeverityForFull);
                state.Fade = Mathf.Min(1f, state.Fade + dt / Mathf.Max(.01f, SupernovaConstants.PlasmaFadeInSec));
            }
            Stale.Clear();
            foreach (var pair in States)
            {
                Pawn pawn = pair.Key; Exposure state = pair.Value;
                if (!pawn.Spawned || pawn.Map != map || pawn.Dead) { Stale.Add(pawn); continue; }
                if (state.Seen != Time.frameCount) state.Fade -= dt / Mathf.Max(.01f, SupernovaConstants.PlasmaFadeOutSec);
                if (state.Fade <= 0f) { Stale.Add(pawn); continue; }
                TargetPlasmaRenderer.Draw(pawn, state.Flow, state.Intensity, state.Fade);
            }
            foreach (Pawn pawn in Stale) States.Remove(pawn);
        }
    }
}
