using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PMP_Persona.SupernovaSupport;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;
using MeshDrawCommand = Waylith.Rendering.MeshDrawCommand;
using MeshDrawSubmission = Waylith.Rendering.MeshDrawSubmission;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace PMP_Persona.Rendering
{
    // Connected affected walls share one alpha union, one flowing surface and one wake.
    // Static map printing preserves linked atlases/corners; native capture preserves moving parts.
    internal static class BuildingPlasmaRenderer
    {
        private static readonly System.Reflection.FieldInfo Immediate = AccessTools.Field(typeof(Graphics), "ImmediateCapture");
        private static readonly Dictionary<int, Surface> Surfaces = new Dictionary<int, Surface>();
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static readonly List<Surface> Pending = new List<Surface>();
        private static TargetOutlineCaptureCamera camera;
        private static Map map;
        private static int frame = -1;
        private const int MaxSurfaces = 128, MaxPixels = 8 * 1024 * 1024;
        internal static int Count => Surfaces.Count;

        internal sealed class Surface : IDisposable
        {
            internal List<Thing> Members = new List<Thing>();
            internal RenderTexture Texture;
            internal UnityEngine.Material Material;
            internal Vector3 Center, Source;
            internal float HalfSize, Fade;
            internal int Captured = -10000, Seen, Resolution;
            internal bool Dirty = true, Dynamic;
            public void Dispose()
            {
                if (Texture != null) { Texture.Release(); UnityEngine.Object.Destroy(Texture); }
                if (Material != null) UnityEngine.Object.Destroy(Material);
            }
        }

        internal static bool Eligible(Thing thing, IReadOnlyList<Thing_Supernova> fields) =>
            SupernovaFieldUtility.IsValidHostileBuilding(thing, Faction.OfPlayer) &&
            thing.Spawned && !thing.Position.Fogged(thing.Map) &&
            SupernovaFieldUtility.IsCellInAnySupernovaField(thing.Position, fields);

        internal static List<List<Thing>> Group(IEnumerable<Thing> buildings)
        {
            var result = new List<List<Thing>>();
            var linked = new HashSet<Thing>();
            var cells = new Dictionary<IntVec3, Thing>();
            foreach (var building in buildings)
            {
                if (building.Graphic is Graphic_Linked)
                {
                    linked.Add(building);
                    foreach (var cell in building.OccupiedRect()) cells[cell] = building;
                }
                else result.Add(new List<Thing> { building });
            }
            var queue = new Queue<Thing>();
            while (linked.Count > 0)
            {
                var group = new List<Thing>();
                var first = linked.First(); linked.Remove(first); queue.Enqueue(first);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue(); group.Add(current);
                    foreach (var cell in current.OccupiedRect())
                    foreach (var direction in GenAdj.CardinalDirections)
                        if (cells.TryGetValue(cell + direction, out var neighbour) && linked.Remove(neighbour)) queue.Enqueue(neighbour);
                }
                group.Sort((a,b) => a.thingIDNumber.CompareTo(b.thingIDNumber));
                result.Add(group);
            }
            return result;
        }

        internal static void DrawForMap(Map current)
        {
            if (current != Find.CurrentMap || frame == Time.frameCount) return;
            if (map != current) { Clear(); map = current; }
            frame = Time.frameCount; Pending.Clear();
            var fields = MapComponent_SupernovaField.Get(current).Instances;
            if (fields.Count == 0 && Surfaces.Count == 0) return;
            float dt = Find.TickManager.Paused ? 0f : Mathf.Clamp(Time.deltaTime, 0f, .1f);
            var groups = Group(current.listerThings.AllThings.Where(t => Eligible(t, fields)));
            // Release obsolete topology immediately: never draw old wall unions over a destroyed gap.
            var keys = new HashSet<int>(groups.Select(g => g[0].thingIDNumber));
            foreach (int key in Surfaces.Keys.ToArray())
                if (Surfaces[key].Members.Any(t => !t.Spawned || t.Map != current) ||
                    (fields.Count > 0 && !keys.Contains(key))) { Surfaces[key].Dispose(); Surfaces.Remove(key); }
            foreach (var group in groups)
            {
                int key = group[0].thingIDNumber;
                if (!Surfaces.TryGetValue(key, out var surface))
                {
                    if (Surfaces.Count >= MaxSurfaces) continue;
                    surface = new Surface(); Surfaces.Add(key, surface);
                }
                surface.Dirty |= !surface.Members.SequenceEqual(group);
                surface.Members = group; surface.Seen = frame;
                surface.Dynamic = group.Any(t => t.def.drawerType != DrawerType.MapMeshOnly);
                surface.Source = SupernovaFieldUtility.FindInstigatorForCell(group[0].Position, fields).DrawPos;
                surface.Fade = Mathf.Min(1f, surface.Fade + dt / Mathf.Max(.01f, SupernovaConstants.PlasmaFadeInSec));
            }
            int captures = 0;
            foreach (var surface in Surfaces.Values.OrderBy(s => s.Captured))
            {
                if (surface.Seen != frame) surface.Fade -= dt / Mathf.Max(.01f, SupernovaConstants.PlasmaFadeOutSec);
                if (surface.Fade <= 0f) continue;
                if (captures < 4 && (surface.Dirty || frame - surface.Captured >= (surface.Dynamic ? 3 : 30)))
                {
                    captures++;
                    try { Capture(surface); }
                    catch (Exception ex) { surface.Captured=frame; Log.ErrorOnce("[PMP] Building plasma capture: " + ex, surface.Members[0].thingIDNumber ^ 91431); continue; }
                }
                if (!surface.Dirty && surface.Texture != null) Pending.Add(surface);
            }
            foreach (int key in Surfaces.Keys.Where(k => Surfaces[k].Fade <= 0f && Surfaces[k].Seen != frame).ToArray())
            { Surfaces[key].Dispose(); Surfaces.Remove(key); }
        }

        internal static Surface CaptureForAudit(List<Thing> members)
        {
            var surface = new Surface { Members = members, Fade = 1f };
            try { Capture(surface); return surface; } catch { surface.Dispose(); throw; }
        }

        private static void Capture(Surface surface)
        {
            if (MeshDrawSubmission.IsCapturing) return;
            var first = surface.Members[0];
            var layer = new SectionLayer_ThingsGeneral(first.Map.mapDrawer.SectionAt(first.Position));
            var commands = new List<MeshDrawCommand>();
            var previous = Immediate.GetValue(null);
            try
            {
                // Capture both queued DrawMesh and immediate custom draw calls without polluting the map queue.
                Immediate.SetValue(null, commands);
                using (MeshDrawSubmission.BeginCapture(commands))
                {
                    foreach (var building in surface.Members)
                    {
                        if (building.def.drawerType != DrawerType.RealtimeOnly) building.Print(layer);
                        if (building.def.drawerType != DrawerType.MapMeshOnly)
                        {
                            building.DynamicDrawPhaseAt(DrawPhase.EnsureInitialized, building.DrawPos);
                            building.DynamicDrawPhaseAt(DrawPhase.ParallelPreDraw, building.DrawPos);
                            building.DynamicDrawPhaseAt(DrawPhase.Draw, building.DrawPos);
                        }
                    }
                    foreach (var sub in layer.subMeshes)
                    {
                        if (sub.verts.Count == 0 || IsShadow(sub.material.shader.name)) continue;
                        sub.FinalizeMesh(MeshParts.All);
                        sub.mesh.RecalculateBounds();
                        Graphics.DrawMesh(sub.mesh, Matrix4x4.identity, sub.material, 0);
                    }
                }
                Immediate.SetValue(null, previous);
                commands.RemoveAll(c => IsShadow(c.Material.DebugName));
                if (commands.Count == 0) { surface.Captured=Time.frameCount; return; }
                NVector min = new NVector(float.MaxValue), max = new NVector(float.MinValue);
                foreach (var command in commands)
                {
                    var b = command.Mesh.Bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var point = NVector.Transform(new NVector((i&1)==0?b.MinX:b.MaxX,
                            (i&2)==0?b.MinY:b.MaxY,(i&4)==0?b.MinZ:b.MaxZ), NMatrix.Transpose(command.Matrix));
                        min = NVector.Min(min, point); max = NVector.Max(max, point);
                    }
                }
                surface.Center = new Vector3((min.X+max.X)*.5f, 0, (min.Z+max.Z)*.5f);
                // Constant world-space wake margin, never a fraction of a whole long wall.
                surface.HalfSize = Mathf.Max(.5f, Mathf.Max(max.X-min.X, max.Z-min.Z)*.5f + 1.5f);
                int resolution = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(surface.HalfSize*2*32)), 256, 1024);
                int used = Surfaces.Values.Where(s => s != surface && s.Texture != null).Sum(s => s.Resolution*s.Resolution);
                if (used + resolution*resolution > MaxPixels) { surface.Captured=Time.frameCount; return; }
                if (surface.Texture == null || surface.Resolution != resolution)
                {
                    if (surface.Texture != null) { surface.Texture.Release(); UnityEngine.Object.Destroy(surface.Texture); }
                    surface.Texture = new RenderTexture(resolution,resolution,24,RenderTextureFormat.ARGB32)
                    { name="PMP building union plasma", filterMode=FilterMode.Bilinear, wrapMode=TextureWrapMode.Clamp };
                    surface.Texture.Create(); surface.Resolution = resolution;
                    if (surface.Material == null) surface.Material = PersonaMaterials.Create(TargetPlasmaRenderer.MaterialName);
                    surface.Material.mainTexture = surface.Texture;
                }
                if (camera == null) camera = TargetOutlineCaptureCamera.Create();
                // UnityStubs keeps column-vector matrices inside System.Numerics storage.
                var translation = NMatrix.Transpose(NMatrix.CreateTranslation(-surface.Center.x, -(min.Y+max.Y)*.5f, -surface.Center.z));
                camera.RenderTo(surface.Texture, surface.HalfSize, () =>
                {
                    var destination = (List<MeshDrawCommand>)Immediate.GetValue(null);
                    foreach (var c in commands)
                        destination.Add(new MeshDrawCommand(c.Mesh,c.Material,c.PropertyRange,translation*c.Matrix,
                            c.SubmeshIndex,c.PassId,c.Instancing,c.RenderQueueOverride,c.Layer,c.Storage));
                });
                surface.Captured = Time.frameCount; surface.Dirty = false;
            }
            finally { Immediate.SetValue(null, previous); layer.Dispose(); }
        }

        private static bool IsShadow(string name) => name != null && name.IndexOf("shadow",StringComparison.OrdinalIgnoreCase)>=0;

        internal static void Configure(Surface surface, MaterialPropertyBlock block)
        {
            TargetPlasmaRenderer.Configure(block, surface.Center-surface.Source, 1f, surface.Fade,
                GenTicks.TicksGame/60f, 0, surface.HalfSize);
            block.SetVector("_Clock",new Vector4(GenTicks.TicksGame/60f,0,1f/surface.Resolution,surface.HalfSize));
            block.SetVector("_Surface",new Vector4(surface.Center.x,surface.Center.z,surface.Source.x,surface.Source.z));
            block.SetFloat("_WorldSpace",1f);
        }

        internal static void Flush()
        {
            foreach (var surface in Pending)
            {
                if (surface.Members.Any(t => !t.Spawned || t.Map != Find.CurrentMap)) continue;
                Configure(surface, Properties);
                var center=surface.Center; center.y=AltitudeLayer.MoteOverhead.AltitudeFor()+.025f;
                Graphics.DrawMesh(MeshPool.plane10,Matrix4x4.TRS(center,Quaternion.identity,new Vector3(surface.HalfSize*2,1,surface.HalfSize*2)),
                    surface.Material,0,null,0,Properties,UnityEngine.Rendering.ShadowCastingMode.Off,false,renderQueue:3500);
            }
            Pending.Clear();
        }

        internal static void Clear()
        {
            foreach (var surface in Surfaces.Values) surface.Dispose();
            Surfaces.Clear(); Pending.Clear(); map=null; frame=-1;
            if (camera != null) UnityEngine.Object.Destroy(camera.gameObject);
            camera=null;
        }
    }
}
