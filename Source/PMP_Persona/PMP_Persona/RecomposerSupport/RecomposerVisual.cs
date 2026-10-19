using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PMP_Persona.FlattenSupport;
using RimWorld;
using PMP_Persona.PrismSupport;
using UnityEngine;
using Verse;

namespace PMP_Persona.RecomposerSupport
{
    [StaticConstructorOnStartup]
    internal static class RecomposerVisual
    {
        internal const string MaterialName = "PMP17_RecomposerMaterial";
        private sealed class Session : IDisposable
        {
            internal readonly Dictionary<Pawn, FlattenVisual.Capture> bodies = new Dictionary<Pawn, FlattenVisual.Capture>();
            internal readonly Dictionary<Pawn, (Rot4 facing, PawnPosture posture)> poses = new Dictionary<Pawn, (Rot4, PawnPosture)>();
            internal Material field = PersonaMaterials.Create(MaterialName);
            internal Material volume = PersonaMaterials.Create("PMP17_RecomposerFieldMaterial");
            public void Dispose() { foreach (var body in bodies.Values) body.Dispose(); UnityEngine.Object.Destroy(field); UnityEngine.Object.Destroy(volume); }
        }
        private static readonly Dictionary<Thing_RecomposerMarker, Session> Sessions = new Dictionary<Thing_RecomposerMarker, Session>();
        private static int frame = -1, captures;
        internal static int ActiveCount => Sessions.Values.Sum(s => s.bodies.Count);
        internal static PawnDrawParms Parameters(Pawn pawn, Rot4 facing, PawnPosture posture)
        {
            var parms = PawnDrawParms.Default;
            parms.pawn = pawn; parms.facing = facing; parms.posture = posture;
            parms.flags = PawnRenderFlags.Headgear | PawnRenderFlags.Clothes | PawnRenderFlags.NeverAimWeapon;
            parms.rotDrawMode = RotDrawMode.Fresh; parms.tint = Color.white; parms.matrix = Matrix4x4.identity;
            return parms;
        }
        internal static void Prepare(Thing_RecomposerMarker marker)
        {
            if (!marker.Spawned) return;
            if (frame != Time.frameCount) { frame = Time.frameCount; captures = 0; }
            try
            {
                if (!Sessions.TryGetValue(marker, out var session)) Sessions.Add(marker, session = new Session());
                if (marker.Locked)
                {
                    foreach (var entry in marker.Subjects)
                    {
                        Capture(session, entry.original, entry.facing, entry.posture);
                        Capture(session, entry.replacement, entry.facing, PawnPosture.Standing);
                    }
                }
                else foreach (var pawn in RecomposerPawnUtility.Targets(marker.Map, marker.Position))
                    Capture(session, pawn, pawn.Rotation, pawn.GetPosture());
            }
            catch (Exception e) { Log.ErrorOnce("[PMP Recomposer] Native capture failed: " + e, 912674); }
        }
        private static void Capture(Session session, Pawn pawn, Rot4 facing, PawnPosture posture)
        {
            if (pawn == null || pawn.Discarded || captures >= 8) return;
            if (session.bodies.TryGetValue(pawn, out var previous))
            {
                if (session.poses[pawn] == (facing, posture)) return;
                previous.Dispose(); session.bodies.Remove(pawn);
            }
            captures++;
            var capture = FlattenVisual.CapturePawn(pawn, Parameters(pawn, facing, posture), MaterialName);
            capture.Material.SetVector("_Geometry", new Vector4(capture.HalfSize, 0, 0, 0));
            session.bodies.Add(pawn, capture); session.poses[pawn] = (facing, posture);
        }
        internal static void Configure(Material material, int phase, float progress, int seed, int mode)
            => material.SetVector("_State", new Vector4(phase, Mathf.Clamp01(progress), seed % 997, mode));
        internal static void Draw(Thing_RecomposerMarker marker)
        {
            if (!marker.Spawned || marker.Map != Find.CurrentMap || !Sessions.TryGetValue(marker, out var session)) return;
            float age = marker.Age;
            float scan = Mathf.Clamp01((age - marker.Config.fieldTicks) / Math.Max(1f, marker.Config.scanTicks));
            float fade = Mathf.Clamp01(age / Math.Max(1f, marker.Config.fieldTicks)) *
                (1 - Mathf.Clamp01((age - marker.ActivationTick) / Math.Max(1f, marker.Config.activationTicks)));
            session.field.SetVector("_State", new Vector4(age / 60, scan, fade, 2));
            Mesh(session.field, marker.Position.ToVector3Shifted(), RecomposerConstants.TargetRadius * 2.1f, 3490);
            OrbitalBeamVolume.Draw(session.volume, marker.Position.ToVector3Shifted(), RecomposerConstants.TargetRadius,
                new Vector4(age / Math.Max(1f, marker.Config.fieldTicks), scan, fade, marker.Locked ? 1 : 0));
            if (!marker.Locked)
            {
                foreach (var pawn in RecomposerPawnUtility.Targets(marker.Map, marker.Position))
                    if (session.bodies.TryGetValue(pawn, out var capture))
                    {
                        // The scan plane sweeps south to north across the actual map, not all bodies at once.
                        float localScan = age < marker.Config.fieldTicks ? 0 : Mathf.Clamp01(
                            (scan * 2 - 1 - (pawn.Position.z - marker.Position.z) / RecomposerConstants.TargetRadius) * 6 + .5f);
                        Configure(capture.Material, 0, localScan, pawn.thingIDNumber, 0);
                        Mesh(capture.Material, pawn.DrawPos, capture.HalfSize * 4, 3500);
                    }
                return;
            }
            int phase = age < marker.CoreTick ? 1 : age < marker.AssemblyTick ? 2 : age < marker.ActivationTick ? 3 : 4;
            float progress = phase == 1 ? (age - marker.LockTick) / Math.Max(1f, marker.Config.disassemblyTicks) :
                phase == 2 ? (age - marker.CoreTick) / Math.Max(1f, marker.Config.coreTicks) :
                phase == 3 ? (age - marker.AssemblyTick) / Math.Max(1f, marker.Config.assemblyTicks) :
                (age - marker.ActivationTick) / Math.Max(1f, marker.Config.activationTicks);
            foreach (var entry in marker.Subjects)
            {
                if (entry.completed) continue;
                var pawn = phase >= 3 ? entry.replacement : entry.original;
                if (session.bodies.TryGetValue(pawn, out var capture))
                {
                    Configure(capture.Material, phase, progress, entry.original.thingIDNumber, 0);
                    Mesh(capture.Material, entry.cell.ToVector3Shifted(), capture.HalfSize * 4, 3500);
                }
                else
                {
                    // During an amortized crowd capture, keep an actual body visible until its texture is ready.
                    var parms = Parameters(pawn, entry.facing, phase >= 3 ? PawnPosture.Standing : entry.posture);
                    parms.matrix = Matrix4x4.TRS(entry.cell.ToVector3ShiftedWithAltitude(AltitudeLayer.MoteOverhead), Quaternion.identity, Vector3.one);
                    var tree = pawn.Drawer.renderer.renderTree;
                    tree.EnsureInitialized(parms.flags); tree.ParallelPreDraw(parms); tree.Draw(parms);
                }
            }
            if (phase == 2 && marker.Subjects.Count > 0 && Find.CameraDriver.CurrentViewRect.Contains(marker.Position))
                Find.CameraDriver.shaker.SetMinShake(marker.Config.coreShake * Mathf.Sin(Mathf.PI * progress) * Prefs.ScreenShakeIntensity);
        }
        private static void Mesh(Material material, Vector3 position, float size, int queue)
        {
            position.y = (queue < 3500 ? AltitudeLayer.MetaOverlays : AltitudeLayer.MoteOverhead).AltitudeFor() + .025f;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(position, Quaternion.identity, new Vector3(size, 1, size)),
                material, 0, null, 0, null, UnityEngine.Rendering.ShadowCastingMode.Off, false, renderQueue: queue);
        }
        internal static void ForgetBody(Thing_RecomposerMarker marker, Pawn pawn)
        {
            if (Sessions.TryGetValue(marker, out var session) && session.bodies.TryGetValue(pawn, out var capture))
            { capture.Dispose(); session.bodies.Remove(pawn); session.poses.Remove(pawn); }
        }
        internal static void Release(Thing_RecomposerMarker marker)
        { if (Sessions.TryGetValue(marker, out var session)) { session.Dispose(); Sessions.Remove(marker); } }
        internal static void Maintain()
        {
            foreach (var marker in Sessions.Keys.ToList())
                if (marker.Destroyed || !marker.Spawned || marker.Map != Find.CurrentMap) Release(marker);
        }
        internal static void Clear() { foreach (var session in Sessions.Values) session.Dispose(); Sessions.Clear(); }
    }
    public sealed class RecomposerVisualLifetime : GameComponent
    {
        public RecomposerVisualLifetime(Game game) { }
        public override void GameComponentUpdate() => RecomposerVisual.Maintain();
        public override void LoadedGame() => RecomposerVisual.Clear();
        public override void StartedNewGame() => RecomposerVisual.Clear();
    }
    [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
    internal static class RecomposerVisualCleanup { static void Prefix() => RecomposerVisual.Clear(); }
}
