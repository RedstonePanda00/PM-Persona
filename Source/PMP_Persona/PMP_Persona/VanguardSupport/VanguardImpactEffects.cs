using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace PMP_Persona.VanguardSupport
{
    // Effect-specific state only. Uses the game's existing ParticleSystem implementation and
    // the same explicit game-clock simulation as PMC R42Vfx; no duplicate Unity compatibility layer.
    public sealed class VanguardImpactEffects : MapComponent
    {
        private const int MaxBursts = 24, MaxScars = 96;
        private sealed class Burst
        {
            internal GameObject Root;
            internal ParticleSystem System;
            internal ParticleSystemRenderer Renderer;
            internal ParticleSystem Dust;
            internal ParticleSystemRenderer DustRenderer;
            internal int EndTick;
            internal bool Water;
        }
        private struct Scar { internal Vector3 Point; internal int Tick, Seed; internal bool Missile, Water, Fracture, ScorchOnly; internal Material Material, Wave; internal float Scale; }
        private static readonly List<VanguardImpactEffects> Instances = new List<VanguardImpactEffects>();
        private readonly List<Burst> bursts = new List<Burst>();
        private readonly List<Scar> scars = new List<Scar>();
        private readonly ParticleSystem.Particle[] particles = new ParticleSystem.Particle[48];
        private readonly Stack<Material> spareScars = new Stack<Material>();
        private Material surface, missileTrail, debris, droplets, smoke;
        internal int ActiveBursts => bursts.FindAll(b => b.Root.activeSelf).Count;
        internal int ScarCount => scars.Count;
        internal Vector2 ImpactShake
        {
            get
            {
                Vector2 shake = Vector2.zero;
                float maximum = 2.4f;
                foreach (var scar in scars)
                {
                    int age = Find.TickManager.TicksGame - scar.Tick;
                    if (!scar.Missile || age < 0 || age >= (scar.Fracture ? 90 : 24)) continue;
                    float amplitude = scar.Fracture ? 22 * Mathf.Exp(-age / 22f) : 1.6f * Mathf.Exp(-age / 5f);
                    if (scar.Fracture) maximum = 24;
                    float phase = scar.Seed % 97;
                    shake += new Vector2(Mathf.Sin(age * 1.7f + phase), Mathf.Cos(age * 2.1f + phase)) * amplitude;
                }
                return Vector2.ClampMagnitude(shake, maximum);
            }
        }
        internal int ParticleCount { get { int n = 0; foreach (var b in bursts) n += b.System.particleCount + b.Dust.particleCount; return n; } }
        internal float ScreenFlash
        {
            get
            {
                float flash = 0;
                foreach (var scar in scars)
                    if (scar.Missile && !scar.Point.ToIntVec3().Fogged(map))
                        flash = Mathf.Max(flash, .18f * Mathf.Exp(-(Find.TickManager.TicksGame - scar.Tick) * .65f));
                return flash;
            }
        }
        public VanguardImpactEffects(Map map) : base(map) { Instances.Add(this); }

        private void Materials()
        {
            if (surface != null) return;
            surface = PersonaMaterials.Create("PMP17_VanguardImpactMaterial");
            surface.SetVector("_Tint", new Vector4(1, .65f, .18f, 1));
            missileTrail = PersonaMaterials.Create("PMP17_VanguardImpactMaterial");
            missileTrail.SetVector("_Tint", new Vector4(.48f, .76f, 1, 1));
            debris = PersonaMaterials.Create("PMP17_VanguardImpactMaterial");
            debris.SetVector("_Params", new Vector4(0, 1, 0, 2));
            debris.SetVector("_Tint", Vector4.one);
            droplets = PersonaMaterials.Create("PMP17_VanguardImpactMaterial");
            droplets.SetVector("_Params", new Vector4(0, 1, 0, 4));
            smoke = PersonaMaterials.Create("PMP17_VanguardImpactMaterial");
            smoke.SetVector("_Params", new Vector4(0, 1, 0, 5));
        }

        internal void AddScorchOnly(Vector3 point, float span, int seed)
        {
            var cell = point.ToIntVec3();
            if (map != Find.CurrentMap || !cell.InBounds(map) || cell.Fogged(map)) return;
            Materials();
            point.y = AltitudeLayer.MoteLow.AltitudeFor();
            // Refresh stationary footprints instead of stacking dozens of opaque decals.
            for (int i = 0; i < scars.Count; i++)
                if (scars[i].ScorchOnly && scars[i].Seed == seed && (scars[i].Point - point).sqrMagnitude < 2.25f)
                { var s = scars[i]; s.Tick = Find.TickManager.TicksGame; scars[i] = s; return; }
            if (scars.Count >= MaxScars) { RecycleScar(scars[0]); scars.RemoveAt(0); }
            scars.Add(new Scar { Point = point, Tick = Find.TickManager.TicksGame, Seed = seed,
                Water = cell.GetTerrain(map).IsWater, Scale = span / 3.6f, ScorchOnly = true,
                Material = spareScars.Count > 0 ? spareScars.Pop() : PersonaMaterials.Create("PMP17_VanguardImpactMaterial") });
        }

        internal void Add(IntVec3 cell, bool missile, int seed, float scale = 1, bool fracture = false)
        {
            if (map != Find.CurrentMap || !cell.InBounds(map) || cell.Fogged(map)) return;
            Materials();
            TerrainDef terrain = cell.GetTerrain(map);
            bool water = terrain.IsWater;
            Vector3 point = cell.ToVector3Shifted();
            point.y = AltitudeLayer.MoteLow.AltitudeFor();
            int tick = Find.TickManager.TicksGame;
            if (scars.Count >= MaxScars) { RecycleScar(scars[0]); scars.RemoveAt(0); }
            var scarMaterial = spareScars.Count > 0 ? spareScars.Pop() : PersonaMaterials.Create("PMP17_VanguardImpactMaterial");
            scars.Add(new Scar { Point = point, Tick = tick, Seed = seed, Missile = missile, Water = water,
                Material = scarMaterial, Scale = scale, Fracture = fracture,
                Wave = fracture ? (spareScars.Count > 0 ? spareScars.Pop() : PersonaMaterials.Create("PMP17_VanguardImpactMaterial")) : null });
            Burst burst = bursts.Find(b => !b.Root.activeSelf);
            if (burst == null)
            {
                if (bursts.Count >= MaxBursts) return;
                var root = new GameObject("PMP Zeta impact particles", typeof(ParticleSystem));
                var ps = root.GetComponent<ParticleSystem>();
                var renderer = root.GetComponent<ParticleSystemRenderer>() ?? root.AddComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = debris;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                var main = ps.main;
                main.playOnAwake = false; main.loop = false; main.duration = 2;
                main.maxParticles = 48; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.gravityModifier = 1.5f;
                var emission = ps.emission; emission.enabled = false;
                var shape = ps.shape; shape.enabled = false;
                // The engine collision module is still a stub. Only our particles get a ground-plane bounce below.
                var collision = ps.collision; collision.enabled = false;
                var color = ps.colorOverLifetime; color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .55f), new GradientAlphaKey(0, 1) });
                color.color = new ParticleSystem.MinMaxGradient(gradient);
                var dustRoot = new GameObject("PMP Zeta radial ejecta", typeof(ParticleSystem));
                dustRoot.transform.SetParent(root.transform, false);
                var dust = dustRoot.GetComponent<ParticleSystem>();
                var dustRenderer = dustRoot.GetComponent<ParticleSystemRenderer>() ?? dustRoot.AddComponent<ParticleSystemRenderer>();
                dustRenderer.sharedMaterial = smoke;
                dustRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                dustRenderer.alignment = ParticleSystemRenderSpace.View;
                var dm = dust.main; dm.playOnAwake = false; dm.loop = false; dm.duration = 2;
                dm.maxParticles = 112; dm.simulationSpace = ParticleSystemSimulationSpace.Local; dm.gravityModifier = .4f;
                var de = dust.emission; de.enabled = false;
                var ds = dust.shape; ds.enabled = false;
                var dc = dust.colorOverLifetime; dc.enabled = true;
                dc.color = new ParticleSystem.MinMaxGradient(gradient);
                var dz = dust.sizeOverLifetime; dz.enabled = true;
                dz.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .35f, 1, 1.8f));
                burst = new Burst { Root = root, System = ps, Renderer = renderer, Dust = dust, DustRenderer = dustRenderer }; bursts.Add(burst);
            }
            burst.Root.SetActive(true);
            point.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            burst.Root.transform.position = point;
            burst.EndTick = tick + (fracture ? 140 : 110);
            burst.Water = water;
            burst.Renderer.sharedMaterial = water ? droplets : debris;
            burst.System.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            burst.Dust.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var dustMain = burst.Dust.main; dustMain.gravityModifier = fracture ? 0 : .4f;
            burst.System.useAutoRandomSeed = false; burst.System.randomSeed = (uint)Math.Max(1, seed);
            // Isolate cosmetic randomness from gameplay RNG, including explosion rolls.
            var random = new System.Random(seed);
            float Next(float min, float max) => min + (float)random.NextDouble() * (max - min);
            Color tint = water ? new Color(.65f, .83f, .93f) : terrain.IsFloor ? new Color(.43f, .45f, .46f)
                : terrain.defName.IndexOf("Rock", StringComparison.OrdinalIgnoreCase) >= 0 || terrain.defName.IndexOf("Gravel", StringComparison.OrdinalIgnoreCase) >= 0
                    ? new Color(.38f, .36f, .32f) : new Color(.36f, .23f, .12f);
            for (int i = 0; i < (fracture ? 0 : missile ? 32 : 9); i++)
            {
                float angle = Next(0, Mathf.PI * 2), speed = Next(1.5f, missile ? 8 : 4);
                float shade = Next(.7f, 1.4f);
                burst.System.Emit(new ParticleSystem.EmitParams {
                    position = new Vector3(Next(-.2f, .2f), .05f, Next(-.2f, .2f)),
                    velocity = new Vector3(Mathf.Cos(angle) * speed, Next(3, missile ? 9 : 6), Mathf.Sin(angle) * speed),
                    startLifetime = Next(.8f, 1.65f), startSize = Next(.13f, missile ? .55f : .30f),
                    rotation = Next(0, 360), angularVelocity = Next(-380, 380),
                    startColor = new Color(tint.r * shade, tint.g * shade, tint.b * shade, water ? .8f : 1),
                    randomSeed = (uint)random.Next(1, int.MaxValue)
                }, 1);
            }
            burst.System.Simulate(.0001f, false, false, false);
            if (fracture)
            {
                // A continuous expanding dust wall starts at the visible scar perimeter.
                // This branch emits no center-origin radial fingers or spokes.
                float radius = 13 * scale * .36f;
                for (int i = 0; i < 112; i++)
                {
                    float angle = (i + Next(-.35f, .35f)) / 112 * Mathf.PI * 2;
                    var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    float shade = Next(.50f, .78f);
                    burst.Dust.Emit(new ParticleSystem.EmitParams {
                        position = radial * (radius + Next(-.25f, .25f)),
                        velocity = radial * 18 + Vector3.up * Next(.3f, 1.2f),
                        startLifetime = Next(1.7f, 2.1f), startSize = Next(2.4f, 3.8f),
                        rotation = Next(0, 360), angularVelocity = Next(-35, 35),
                        startColor = water ? new Color(.63f, .80f, .91f, .65f) : new Color(shade, shade * .82f, shade * .61f, .75f),
                        randomSeed = (uint)random.Next(1, int.MaxValue)
                    }, 1);
                }
                burst.Dust.Simulate(.0001f, false, false, false);
            }
            else if (missile)
            {
                // Fast radial fingers followed by expanding puffs, based on the supplied LOKI stills.
                // Cosmetic only: no extra projectiles, explosion radius or damage.
                float phase = Next(0, Mathf.PI * 2);
                float[] gaps = new float[10];
                float gapSum = 0;
                for (int i = 0; i < gaps.Length; i++) { gaps[i] = Next(.45f, 1.65f); gapSum += gaps[i]; }
                float angle = phase;
                for (int arm = 0; arm < 10; arm++)
                {
                    angle += gaps[arm] / gapSum * Mathf.PI * 2;
                    var radial = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    float reach = Next(.85f, 1.45f), thickness = Next(.8f, 1.4f);
                    for (int j = 0; j < 10; j++)
                    {
                        float speed = (4 + j * 2.1f) * reach;
                        float shade = Next(.62f, .92f);
                        Color dustColor = water ? new Color(.66f, .82f, .94f, .55f)
                            : j < 2 ? new Color(1, .38f, .075f, .8f) : new Color(shade, shade * .94f, shade * .84f, .65f);
                        burst.Dust.Emit(new ParticleSystem.EmitParams {
                            position = radial * .15f, velocity = radial * speed + Vector3.up * Next(2, 6),
                            startLifetime = Next(.95f, 1.7f), startSize = Next(2.5f, 4.2f) * thickness * (1 - j * .025f),
                            rotation = Next(0, 360), angularVelocity = Next(-55, 55), startColor = dustColor,
                            randomSeed = (uint)random.Next(1, int.MaxValue)
                        }, 1);
                    }
                }
                burst.Dust.Simulate(.0001f, false, false, false);
            }
        }

        public override void MapComponentTick()
        {
            int tick = Find.TickManager.TicksGame;
            for (int i = scars.Count - 1; i >= 0; i--)
                if (tick - scars[i].Tick >= 360) { RecycleScar(scars[i]); scars.RemoveAt(i); }
            foreach (var burst in bursts)
            {
                if (!burst.Root.activeSelf) continue;
                if (tick >= burst.EndTick)
                {
                    burst.System.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    burst.Dust.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    burst.Root.SetActive(false); continue;
                }
                burst.System.Simulate(1f / 60, false, false, false);
                burst.Dust.Simulate(1f / 60, false, false, false);
                int count = burst.System.GetParticles(particles);
                bool changed = false;
                for (int i = 0; i < count; i++)
                {
                    if (particles[i].position.y >= 0) continue;
                    var p = particles[i]; var pos = p.position; var velocity = p.velocity;
                    if (burst.Water) { p.remainingLifetime = 0; particles[i] = p; changed = true; continue; }
                    pos.y = 0; velocity.x *= .5f; velocity.z *= .5f;
                    velocity.y = Mathf.Abs(velocity.y) > 1 ? -velocity.y * .18f : 0;
                    p.position = pos; p.velocity = velocity; particles[i] = p; changed = true;
                }
                if (changed) burst.System.SetParticles(particles, count);
            }
        }

        public override void MapComponentUpdate()
        {
            bool visible = map == Find.CurrentMap && RimWorld.Planet.WorldRendererUtility.DrawingMap;
            foreach (var burst in bursts) { burst.Renderer.enabled = visible; burst.DustRenderer.enabled = visible; }
            // MapComponentDraw precedes terrain submission in 1.7. Submit decals after the map mesh.
            if (visible) DrawScars();
        }
        private void DrawScars()
        {
            if (map != Find.CurrentMap || scars.Count == 0) return;
            Materials();
            foreach (var scar in scars)
            {
                if (scar.Point.ToIntVec3().Fogged(map)) continue;
                float age = (Find.TickManager.TicksGame - scar.Tick) / 60f;
                if (scar.ScorchOnly) age += .25f; // No repeated impact flash while a continuous beam refreshes its trail.
                float span = (scar.Missile ? 13 : 3.6f) * scar.Scale;
                // The sensor's offscreen camera must also see the per-impact uniforms.
                // Keep bounded, reusable materials rather than relying on copied draw-property arenas.
                scar.Material.SetVector("_Params", new Vector4(age, 6, scar.Seed % 997, scar.Water ? 3 : 0));
                scar.Material.SetVector("_Tint", new Vector4(1, 1, 1, scar.Fracture || scar.ScorchOnly ? 0 : 1));
                Draw(scar.Point, Quaternion.AngleAxis(scar.Seed % 360, Vector3.up), new Vector3(span, 1, span), scar.Material, 3005);
                if (scar.Fracture && age < 2.2f)
                {
                    float initialRadius = span * .36f;
                    float extent = initialRadius + 18 * age + 3;
                    scar.Wave.SetVector("_Params", new Vector4(age, initialRadius, scar.Seed % 997, 6));
                    Draw(scar.Point, Quaternion.identity, new Vector3(extent * 2, 1, extent * 2), scar.Wave, 3010);
                }
            }
        }
        internal void DrawFlight(Vector3 head, Vector3 source, bool missile, int seed)
        {
            if (map != Find.CurrentMap) return;
            Materials();
            var direction = head - source; direction.y = 0;
            float length = Mathf.Min(direction.magnitude, missile ? 48 : 10);
            if (length < .05f) return;
            direction.Normalize(); head.y = AltitudeLayer.MoteOverhead.AltitudeFor();
            var trail = missile ? missileTrail : surface;
            trail.SetVector("_Params", new Vector4(Find.TickManager.TicksGame / 60f, 1, 17, 1));
            Draw(head - direction * length / 2, Quaternion.LookRotation(direction),
                new Vector3(missile ? 3 : .55f, 1, length), trail, 3100);
        }
        private void Draw(Vector3 position, Quaternion rotation, Vector3 scale, Material material, int queue) =>
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(position, rotation, scale), material, 0, null, 0,
                (MaterialPropertyBlock)null, ShadowCastingMode.Off, false, renderQueue: queue);
        private void RecycleScar(Scar scar)
        {
            spareScars.Push(scar.Material);
            if (scar.Wave != null) spareScars.Push(scar.Wave);
        }
        public override void MapRemoved()
        {
            Dispose(); Instances.Remove(this);
        }
        private void Dispose()
        {
            foreach (var burst in bursts) { burst.Root.SetActive(false); UnityEngine.Object.Destroy(burst.Root); }
            bursts.Clear();
            foreach (var scar in scars) { UnityEngine.Object.Destroy(scar.Material); if (scar.Wave != null) UnityEngine.Object.Destroy(scar.Wave); }
            foreach (var material in spareScars) UnityEngine.Object.Destroy(material);
            scars.Clear(); spareScars.Clear();
            if (surface != null) UnityEngine.Object.Destroy(surface);
            if (debris != null) UnityEngine.Object.Destroy(debris);
            if (droplets != null) UnityEngine.Object.Destroy(droplets);
            if (smoke != null) UnityEngine.Object.Destroy(smoke);
            if (missileTrail != null) UnityEngine.Object.Destroy(missileTrail);
            surface = debris = droplets = smoke = missileTrail = null;
        }
        internal static void ClearAll()
        {
            foreach (var instance in Instances) instance.Dispose();
            Instances.Clear();
        }
    }
    [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
    internal static class VanguardImpactCleanup { static void Prefix() => VanguardImpactEffects.ClearAll(); }
}
