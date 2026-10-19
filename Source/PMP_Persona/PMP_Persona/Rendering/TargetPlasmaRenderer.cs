using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Rendering
{
    /// <summary>Shared, GPU-only RenderTree silhouette plasma. Call once per target per frame.
    /// Flow points downstream in world XZ; intensity and fade are in [0,1]. No damage or support-specific state.</summary>
    [StaticConstructorOnStartup]
    public static class TargetPlasmaRenderer
    {
        internal const string MaterialName = "PMP17_TargetPlasmaMaterial";
        private const int Resolution = 256, MaxTargets = 256, CapturesPerFrame = 8;
        private static readonly AccessTools.FieldRef<PawnRenderTree, List<PawnGraphicDrawRequest>> Requests =
            AccessTools.FieldRefAccess<PawnRenderTree, List<PawnGraphicDrawRequest>>("drawRequests");
        private static readonly AccessTools.FieldRef<PawnRenderTree, PawnDrawParms> OldParms =
            AccessTools.FieldRefAccess<PawnRenderTree, PawnDrawParms>("oldParms");
        private static readonly MethodInfo Prepare = AccessTools.Method(typeof(PawnRenderer), "ParallelGetPreRenderResults");
        private static readonly FieldInfo PreparedParms = AccessTools.Field(Prepare.ReturnType, "parms");
        private static readonly Dictionary<Pawn, Capture> Captures = new Dictionary<Pawn, Capture>();
        private static readonly List<Pawn> Remove = new List<Pawn>();
        private static readonly HashSet<Pawn> Scheduled = new HashSet<Pawn>();
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static TargetOutlineCaptureCamera camera;
        private static Map currentMap;
        private static int frame = -1, capturesThisFrame;
        private static bool capturing;
        private struct SurfaceDraw
        {
            internal Capture Capture;
            internal Pawn Target;
            internal Vector3 Flow;
            internal float Intensity, Fade, Time;
            internal int Seed;
        }
        private static readonly List<SurfaceDraw> Pending = new List<SurfaceDraw>();
        internal static int CachedTargetCount => Captures.Count;
        internal static int CapturesThisFrame => capturesThisFrame;
        internal static int LastCaptureFrame(Pawn pawn) => Captures.TryGetValue(pawn, out var value) ? value.CapturedFrame : -1;

        internal sealed class Capture : IDisposable
        {
            internal readonly RenderTexture Texture;
            internal readonly Material Material;
            internal float HalfSize;
            internal int CapturedFrame = -9999, UsedFrame, NodeCount;
            internal Rot4 Rotation;
            internal PawnPosture Posture;
            internal Capture()
            {
                Texture = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32)
                { name = "PMP RenderTree plasma silhouette", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                Texture.Create();
                Material = PersonaMaterials.Create(MaterialName);
                Material.mainTexture = Texture;
            }
            public void Dispose()
            {
                Texture.Release(); UnityEngine.Object.Destroy(Texture); UnityEngine.Object.Destroy(Material);
            }
        }

        public static void Draw(Pawn target, Vector3 flow, float intensity, float fade = 1f)
        {
            if (fade <= 0f) return;
            Capture capture = VisibleCapture(target);
            if (capture == null) return;
            Pending.Add(new SurfaceDraw { Capture=capture, Target=target, Flow=flow, Intensity=intensity,
                Fade=fade, Time=GenTicks.TicksGame/60f, Seed=target.thingIDNumber });
        }

        // Share the bounded RenderTree cache with the IR sensor; never capture hidden targets.
        internal static Capture VisibleCapture(Pawn target)
        {
            if (capturing || target == null || !target.Spawned || target.Map != Find.CurrentMap ||
                target.Position.Fogged(target.Map) || target.IsHiddenFromPlayer()) return null;
            Maintain();
            Capture capture = Acquire(target);
            if (capture == null) return null;
            capture.UsedFrame = Time.frameCount;
            bool changed = capture.Rotation != target.Rotation || capture.Posture != target.GetPosture();
            if (capturesThisFrame < CapturesPerFrame && (capture.CapturedFrame < 0 || Scheduled.Contains(target) || (changed && Captures.Count <= CapturesPerFrame)))
            {
                capturesThisFrame++;
                try { CaptureTree(target, capture); }
                catch (Exception ex) { Log.ErrorOnce("[PMP] RenderTree plasma capture failed: " + ex, target.thingIDNumber ^ 725383); return null; }
            }
            return capture.CapturedFrame < 0 ? null : capture;
        }

        // RimWorld 1.7 invokes MapComponentDraw before the map's dynamic pawns. Submit surfaces
        // only after UpdatePlay, so both normal and offscreen submission order put them above pawns.
        internal static void Flush()
        {
            BuildingPlasmaRenderer.Flush();
            foreach(var draw in Pending)
            {
                if(!draw.Target.Spawned || draw.Target.Map!=Find.CurrentMap) continue;
                var capture=draw.Capture;
                // Ticks can move/despawn the target between the early map callback and this flush.
                Vector3 center=draw.Target.DrawPos;
                center.y=AltitudeLayer.MoteOverhead.AltitudeFor()+.03f;
                Configure(Properties,draw.Flow,draw.Intensity,draw.Fade,draw.Time,draw.Seed,capture.HalfSize);
                Graphics.DrawMesh(MeshPool.plane10,Matrix4x4.TRS(center,Quaternion.identity,
                    new Vector3(capture.HalfSize*2,1,capture.HalfSize*2)),capture.Material,0,null,0,Properties,
                    UnityEngine.Rendering.ShadowCastingMode.Off,false,renderQueue:3500);
            }
            Pending.Clear();
        }

        internal static void Configure(MaterialPropertyBlock block, Vector3 flow, float intensity, float fade,
            float time, int seed, float halfSize)
        {
            flow.y = 0; flow = flow.sqrMagnitude > .001f ? flow.normalized : Vector3.forward;
            block.Clear();
            block.SetVector("_Flow", new Vector4(flow.x, flow.z, Mathf.Clamp01(intensity), Mathf.Clamp01(fade)));
            block.SetVector("_Clock", new Vector4(time, seed % 997, 1f / Resolution, halfSize));
        }

        private static Capture Acquire(Pawn target)
        {
            if (Captures.TryGetValue(target, out var capture)) return capture;
            // Never churn textures for a crowd larger than the cache. Unused entries expire via Maintain.
            if (Captures.Count >= MaxTargets) return null;
            capture = new Capture(); Captures.Add(target, capture); return capture;
        }

        internal static Capture CaptureForAudit(Pawn pawn)
        {
            var capture = new Capture();
            try { CaptureTree(pawn, capture); return capture; }
            catch { capture.Dispose(); throw; }
        }

        private static void CaptureTree(Pawn pawn, Capture capture)
        {
            if (camera == null) camera = TargetOutlineCaptureCamera.Create();
            PawnRenderer renderer = pawn.Drawer.renderer;
            PawnRenderTree tree = renderer.renderTree;
            tree.EnsureInitialized(PawnRenderFlags.Clothes | PawnRenderFlags.Headgear);
            var requests = Requests(tree);
            var saved = SimplePool<List<PawnGraphicDrawRequest>>.Get(); saved.AddRange(requests);
            PawnDrawParms old = OldParms(tree);
            capturing = true;
            try
            {
                Vector3 origin = pawn.DrawPos;
                // The same game routine used for a map pawn: laying angle, bed, crawl, swimming,
                // life-stage offset, animation and equipment. disableCache prevents atlas shortcuts.
                object result = Prepare.Invoke(renderer, new object[] { origin, null, false, true });
                var parms = (PawnDrawParms)PreparedParms.GetValue(result);
                Matrix4x4 translation = Matrix4x4.Translate(-origin);
                parms.matrix = translation * parms.matrix;
                parms.flags |= PawnRenderFlags.DrawNow;
                float extent = .1f;
                for (int i = 0; i < requests.Count; i++)
                {
                    var request = requests[i];
                    request.preDrawnComputedMatrix = translation * request.preDrawnComputedMatrix;
                    requests[i] = request;
                    if (request.mesh == null || request.material == null) continue;
                    Bounds b = request.mesh.bounds;
                    for (int c = 0; c < 8; c++)
                    {
                        var corner = b.center + Vector3.Scale(b.extents,
                            new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                        Vector3 p = request.preDrawnComputedMatrix.MultiplyPoint3x4(corner);
                        extent = Mathf.Max(extent, Mathf.Abs(p.x), Mathf.Abs(p.z));
                    }
                }
                // Keep room for a wake in any direction; sizing comes from meshes, not race BodySize.
                capture.HalfSize = Mathf.Max(.5f, extent * 2.2f);
                capture.NodeCount = requests.Count;
                camera.RenderTo(capture.Texture, capture.HalfSize, () => tree.Draw(parms));
                capture.CapturedFrame = Time.frameCount;
                capture.Rotation = pawn.Rotation; capture.Posture = pawn.GetPosture();
            }
            finally
            {
                requests.Clear(); requests.AddRange(saved); OldParms(tree) = old;
                // Capture may consume an apparel/node recache between parallel preparation and map draw.
                if (tree.rootNode != null) tree.rootNode.requestRecache = true;
                saved.Clear(); SimplePool<List<PawnGraphicDrawRequest>>.Return(saved); capturing = false;
            }
        }

        internal static void Maintain()
        {
            if (frame == Time.frameCount) return;
            frame = Time.frameCount; capturesThisFrame = 0;
            Pending.Clear();
            if (currentMap != Find.CurrentMap) { Clear(); currentMap = Find.CurrentMap; }
            Remove.Clear();
            foreach (var pair in Captures)
                if (!pair.Key.Spawned || pair.Key.Map != currentMap || Time.frameCount - pair.Value.UsedFrame > 90)
                    Remove.Add(pair.Key);
            foreach (var pawn in Remove) { Captures[pawn].Dispose(); Captures.Remove(pawn); }
            // Oldest-first scheduling prevents early pawns in map order from starving a large crowd.
            Scheduled.Clear();
            foreach (var pair in Captures.Where(p => frame - p.Value.CapturedFrame >= 3 && frame - p.Value.UsedFrame <= 2)
                         .OrderBy(p => p.Value.CapturedFrame).Take(CapturesPerFrame)) Scheduled.Add(pair.Key);
        }

        public static void Clear()
        {
            BuildingPlasmaRenderer.Clear();
            foreach (var capture in Captures.Values) capture.Dispose();
            Captures.Clear();
            Pending.Clear();
            Scheduled.Clear();
            if (camera != null) UnityEngine.Object.Destroy(camera.gameObject);
            camera = null;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.UpdatePlay))]
    internal static class TargetPlasmaAfterMapDraw
    {
        private static void Postfix() => TargetPlasmaRenderer.Flush();
    }

    // Cleanup continues even after the last field disappears, and on new/load game.
    public sealed class TargetPlasmaLifecycle : GameComponent
    {
        public TargetPlasmaLifecycle(Game game) { }
        public override void StartedNewGame() => TargetPlasmaRenderer.Clear();
        public override void LoadedGame() => TargetPlasmaRenderer.Clear();
        public override void GameComponentUpdate() => TargetPlasmaRenderer.Maintain();
    }
}
