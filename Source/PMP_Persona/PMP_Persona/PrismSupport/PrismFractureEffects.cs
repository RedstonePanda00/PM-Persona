using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;
using PMP_Persona.SupernovaSupport;
using PMP_Persona.VanguardSupport;

namespace PMP_Persona.PrismSupport
{
    public sealed class PrismFractureStrike : IExposable
    {
        public IntVec3 Target;
        public int Start, Guidance, Damage;
        public float Radius;
        public bool Impacted;
        internal readonly Material[] Materials = new Material[4];
        public void ExposeData()
        {
            Scribe_Values.Look(ref Target, "target"); Scribe_Values.Look(ref Start, "start");
            Scribe_Values.Look(ref Guidance, "guidance"); Scribe_Values.Look(ref Damage, "damage");
            Scribe_Values.Look(ref Radius, "radius"); Scribe_Values.Look(ref Impacted, "impacted");
        }
        internal void Dispose() { foreach (var m in Materials) if (m != null) UnityEngine.Object.Destroy(m); }
    }
    public sealed class PrismFractureEffects : CustomMapComponent
    {
        private List<PrismFractureStrike> strikes = new List<PrismFractureStrike>();
        private static readonly List<PrismFractureEffects> instances = new List<PrismFractureEffects>();
        internal int Count => strikes.Count;
        public PrismFractureEffects(Map map) : base(map) { instances.Add(this); }
        internal static PrismFractureEffects Get(Map map)
        {
            var component = map.GetComponent<PrismFractureEffects>();
            if (component == null) { component = new PrismFractureEffects(map); map.components.Add(component); }
            return component;
        }
        internal void Add(IntVec3 target, OrbitalSupportConfig config) => strikes.Add(new PrismFractureStrike {
            Target = target, Start = Find.TickManager.TicksGame, Guidance = config.fractureGuidanceTicks,
            Damage = config.fractureDamage, Radius = config.fractureRadius
        });
        public override void MapComponentTick()
        {
            for (int i = strikes.Count - 1; i >= 0; i--)
            {
                var strike = strikes[i]; int age = Find.TickManager.TicksGame - strike.Start;
                if (age >= strike.Guidance && !strike.Impacted)
                {
                    strike.Impacted = true;
                    var affected = new HashSet<Thing>();
                    foreach (var cell in GenRadial.RadialCellsAround(strike.Target, strike.Radius, true))
                        if (cell.InBounds(map))
                            foreach (var thing in cell.GetThingList(map))
                                if (thing is Pawn || thing is Building) affected.Add(thing);
                    foreach (var thing in affected)
                        if (thing.Spawned && !thing.Destroyed)
                            OrbitalMixedDamage.Apply(thing, strike.Damage * Mathf.Lerp(1, .35f,
                                Mathf.Clamp01(thing.Position.DistanceTo(strike.Target) / strike.Radius)));
                    // Direct radial damage intentionally leaves terrain definitions unchanged.
                    map.GetComponent<VanguardImpactEffects>().Add(strike.Target, true, strike.Start, strike.Radius * 2.2f / 13, true);
                }
                if (age >= strike.Guidance + 70) { strike.Dispose(); strikes.RemoveAt(i); }
            }
        }
        public override void MapComponentUpdate()
        {
            if (map != Find.CurrentMap || !RimWorld.Planet.WorldRendererUtility.DrawingMap) return;
            foreach (var strike in strikes)
            {
                if (strike.Target.Fogged(map)) continue;
                float age = Find.TickManager.TicksGame - strike.Start;
                Vector3 target = strike.Target.ToVector3Shifted();
                if (age < strike.Guidance)
                {
                    float p = Mathf.Clamp01(age / strike.Guidance), radius = 8 * (1 - VanguardPresentation.Ease(p));
                    for (int i = 0; i < 3; i++)
                    {
                        float angle = i * Mathf.PI * 2 / 3 + p * 2.4f;
                        Vector3 at = target + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                        DrawVolume(strike, i, at, 1.5f, 20, age / 60, .8f, false);
                    }
                }
                if (age >= strike.Guidance - 8)
                {
                    float beamAge = (age - strike.Guidance + 8) / 60;
                    float visibility = (1 - VanguardPresentation.Ease((beamAge - .35f) / .75f));
                    // The visible column fits inside the hot central scar, including its halo.
                    DrawVolume(strike, 3, target, strike.Radius * .55f, 24, beamAge, visibility, true);
                }
            }
        }
        private static void DrawVolume(PrismFractureStrike strike, int index, Vector3 point, float halfWidth,
            float height, float time, float visibility, bool main)
        {
            if (visibility <= 0) return;
            var material = strike.Materials[index] ?? (strike.Materials[index] = PersonaMaterials.Create("PMP17_PrismFractureMaterial"));
            OrbitalBeamVolume.Draw(material, point, halfWidth,
                new Vector4(time, main ? 1 : 0, visibility, index));
        }
        public override void ExposeData() => Scribe_Collections.Look(ref strikes, "strikes", LookMode.Deep);
        public override void MapRemoved() { foreach (var s in strikes) s.Dispose(); strikes.Clear(); instances.Remove(this); }
        // Called from the game update, outside the map-component traversal being edited.
        internal static void PruneIdle()
        {
            for (int i = instances.Count - 1; i >= 0; i--)
                if (instances[i].strikes.Count == 0)
                { var c = instances[i]; c.map.components.Remove(c); instances.RemoveAt(i); }
        }
        internal static void ClearAll()
        {
            foreach (var component in instances) foreach (var strike in component.strikes) strike.Dispose();
            instances.Clear();
        }
    }
}
