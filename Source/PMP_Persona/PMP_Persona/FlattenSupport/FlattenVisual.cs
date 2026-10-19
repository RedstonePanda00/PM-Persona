using System;
using System.Collections.Generic;
using HarmonyLib;
using ProtossMech;
using UnityEngine;
using Verse;

namespace PMP_Persona.FlattenSupport
{
    // Only the two-second transition allocates render targets. Final sheets use RenderTree directly.
    [StaticConstructorOnStartup]
    internal static class FlattenVisual
    {
        internal const string MaterialName = "PMP17_FlattenMaterial";
        private static readonly Dictionary<Thing_FlattenedPawn, Capture> Captures = new Dictionary<Thing_FlattenedPawn, Capture>();
        private static readonly List<Thing_FlattenedPawn> Remove = new List<Thing_FlattenedPawn>();
        private static readonly AccessTools.FieldRef<PawnRenderTree, List<PawnGraphicDrawRequest>> Requests =
            AccessTools.FieldRefAccess<PawnRenderTree, List<PawnGraphicDrawRequest>>("drawRequests");
        private static readonly AccessTools.FieldRef<PawnRenderTree, PawnDrawParms> OldParms =
            AccessTools.FieldRefAccess<PawnRenderTree, PawnDrawParms>("oldParms");
        private static TargetOutlineCaptureCamera camera;
        private static int frame = -1, capturesThisFrame;
        private static Material fallback;
        internal static int ActiveCount => Captures.Count;
        internal sealed class Capture : IDisposable
        {
            internal RenderTexture Texture;
            internal Material Material;
            internal float HalfSize;
            public void Dispose()
            {
                if (Texture != null) { Texture.Release(); UnityEngine.Object.Destroy(Texture); }
                if (Material != null) UnityEngine.Object.Destroy(Material);
            }
        }

        internal static void Prepare(Thing_FlattenedPawn sheet)
        {
            if (sheet.Ready || !sheet.Spawned || Captures.ContainsKey(sheet)) return;
            if (frame != Time.frameCount) { frame = Time.frameCount; capturesThisFrame = 0; }
            // Large crowds still get procedural sheets immediately; silhouette capture is amortized.
            if (capturesThisFrame >= 8 || Captures.Count >= 256) return;
            capturesThisFrame++;
            Captures.Add(sheet, CaptureTree(sheet));
        }

        internal static Capture CaptureTree(Thing_FlattenedPawn sheet)
            => CapturePawn(sheet.HeldPawn, sheet.DrawParameters(Vector3.zero), MaterialName);

        // Shared native RenderTree capture; callers own the returned GPU resources.
        internal static Capture CapturePawn(Pawn pawn, PawnDrawParms parms, string materialName)
        {
            var capture = new Capture();
            var tree = pawn.Drawer.renderer.renderTree;
            parms.matrix = Matrix4x4.identity; parms.tint = Color.white;
            parms.flags |= PawnRenderFlags.DrawNow;
            tree.EnsureInitialized(parms.flags);
            var requests = Requests(tree);
            var saved = new List<PawnGraphicDrawRequest>(requests);
            var old = OldParms(tree);
            try
            {
                tree.ParallelPreDraw(parms);
                float extent = .6f;
                foreach (var request in requests)
                {
                    if (request.mesh == null) continue;
                    Bounds b = request.mesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = b.center + Vector3.Scale(b.extents,
                            new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                        Vector3 p = request.preDrawnComputedMatrix.MultiplyPoint3x4(corner);
                        extent = Mathf.Max(extent, Mathf.Abs(p.x), Mathf.Abs(p.z));
                    }
                }
                capture.HalfSize = extent * 1.12f;
                capture.Texture = new RenderTexture(256,256,24,RenderTextureFormat.ARGB32)
                {name="PMP temporary dimensional slices",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
                capture.Texture.Create();
                capture.Material = PersonaMaterials.Create(materialName);
                capture.Material.mainTexture = capture.Texture;
                if (camera == null) camera = TargetOutlineCaptureCamera.Create();
                camera.RenderTo(capture.Texture,capture.HalfSize,()=>tree.Draw(parms));
                return capture;
            }
            catch { capture.Dispose(); throw; }
            finally
            {
                requests.Clear();requests.AddRange(saved);OldParms(tree)=old;
                if (tree.rootNode != null) tree.rootNode.requestRecache=true;
            }
        }

        internal static void Draw(Thing_FlattenedPawn sheet, Vector3 location)
        {
            if (sheet.Ready || !sheet.Spawned) return;
            Captures.TryGetValue(sheet,out var capture);
            if (fallback == null && capture == null) fallback=PersonaMaterials.Create(MaterialName);
            var material=capture?.Material??fallback;
            float half=capture?.HalfSize??1f;
            Configure(material,sheet.Progress,half,capture!=null);
            location.y=AltitudeLayer.MoteOverhead.AltitudeFor()+.02f;
            Graphics.DrawMesh(MeshPool.plane10,Matrix4x4.TRS(location,Quaternion.identity,new Vector3(half*4,1,half*4)),
                material,0,null,0,null,UnityEngine.Rendering.ShadowCastingMode.Off,false,renderQueue:3500);
        }
        internal static void Configure(Material material,float progress,float half,bool captured)
            => material.SetVector("_State",new Vector4(Mathf.Clamp01(progress),half,captured?1:0,0));

        internal static void Maintain()
        {
            Remove.Clear();
            foreach (var entry in Captures)
                if (entry.Key.Destroyed || !entry.Key.Spawned || entry.Key.Map!=Find.CurrentMap || entry.Key.Ready) Remove.Add(entry.Key);
            foreach (var sheet in Remove) {Captures[sheet].Dispose();Captures.Remove(sheet);}
            if (Captures.Count==0 && camera!=null) {UnityEngine.Object.Destroy(camera.gameObject);camera=null;}
        }
        internal static void Clear()
        {
            foreach(var capture in Captures.Values)capture.Dispose();
            Captures.Clear();
            if(camera!=null)UnityEngine.Object.Destroy(camera.gameObject);camera=null;
            if(fallback!=null)UnityEngine.Object.Destroy(fallback);fallback=null;
        }
    }

    public sealed class FlattenVisualLifetime : GameComponent
    {
        public FlattenVisualLifetime(Game game) { }
        public override void GameComponentUpdate()=>FlattenVisual.Maintain();
        public override void LoadedGame()=>FlattenVisual.Clear();
        public override void StartedNewGame()=>FlattenVisual.Clear();
    }
}
