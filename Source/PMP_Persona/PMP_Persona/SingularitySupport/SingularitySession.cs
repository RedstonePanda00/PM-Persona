using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ProtossMech;
using RimWorld;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;
using PMP_Persona.ShipSupport;

namespace PMP_Persona.SingularitySupport
{
    public sealed class SingularitySession : CustomMapComponent, IThingHolder
    {
        public IntVec3 Center;
        private float radius = 8, halfSize = 10;
        private float shakeStrength = .18f;
        private int age, rupture = 60, absorption = 300, closure = 60, captureFailures;
        private bool captured, consumed, completed;
        private string snapshotPng;
        private ShipSupportDef paidSupport;
        private ThingOwner<Thing> contents;
        private Texture2D snapshot;
        private Material material;
        private SingularityCaptureRunner captureRunner;
        private readonly MaterialPropertyBlock warpProperties = new MaterialPropertyBlock();
        public bool Completed => completed;
        public IThingHolder ParentHolder => map;
        internal int Age => age;
        internal bool Captured => captured;
        internal int HeldCount => contents.Count;
        internal Texture2D Snapshot => snapshot;
        public SingularitySession(Map map) : base(map) { contents = new ThingOwner<Thing>(this, false, LookMode.Deep); }

        internal static bool Start(Map map, IntVec3 cell, SingularityConfig config, ShipSupportDef paidSupport = null)
        {
            if (map == null || !cell.InBounds(map) || map.GetComponent<SingularitySession>() != null) return false;
            var effect = new SingularitySession(map) { Center = cell, radius = Mathf.Max(1, config.radius),
                rupture = Mathf.Max(1, config.ruptureTicks), absorption = Mathf.Max(1, config.absorptionTicks),
                closure = Mathf.Max(1, config.closureTicks), shakeStrength = Mathf.Max(0, config.shakeStrength), paidSupport = paidSupport,
                material = PersonaMaterials.Create("PMP17_SingularityMaterial") };
            map.components.Add(effect); return true;
        }

        internal static List<Thing> Collect(Map map, IntVec3 center, float radius)
        {
            var found = new HashSet<Thing>();
            foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
                if (cell.InBounds(map))
                    foreach (var thing in cell.GetThingList(map))
                        if (thing.Spawned && !thing.Destroyed && thing.def.destroyable) found.Add(thing);
            return found.OrderBy(t => t.thingIDNumber).ToList();
        }

        internal void PrepareSnapshot()
        {
            if (captured || completed || age < rupture || captureRunner != null) return;
            captureRunner = SingularityCaptureRunner.Begin(this);
        }
        internal void CaptureFinished() { captureRunner = null; }
        internal void CaptureAtEndOfFrame()
        {
            if (completed || captured || map.Disposed) return;
            var targets = Collect(map, Center, radius);
            var origin = Center.ToVector3Shifted();
            halfSize = radius + 2;
            var extra = new List<CellRect>();
            foreach (var t in targets)
            {
                var rect = t.OccupiedRect();
                if (t.def.category == ThingCategory.Building) extra.Add(rect);
                halfSize = Mathf.Max(halfSize, Mathf.Abs(rect.minX - origin.x) + 2, Mathf.Abs(rect.maxX + 1 - origin.x) + 2,
                    Mathf.Abs(rect.minZ - origin.z) + 2, Mathf.Abs(rect.maxZ + 1 - origin.z) + 2);
            }
            Texture2D image = null;
            try
            {
                image = SingularitySnapshot.Capture(map, origin, halfSize);
                var pixels = image.GetPixels32(); int size = image.width;
                for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                {
                    float dx = ((x + .5f) / size * 2 - 1) * halfSize;
                    float dz = (1 - (y + .5f) / size * 2) * halfSize;
                    float distance = Mathf.Sqrt(dx * dx + dz * dz);
                    float coverage = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(radius - .18f, radius + .08f, distance));
                    if (coverage < 1)
                    {
                        var cell = new Vector3(origin.x + dx, 0, origin.z + dz).ToIntVec3();
                        foreach (var rect in extra) if (rect.Contains(cell)) { coverage = 1; break; }
                    }
                    pixels[y * size + x].a = (byte)(pixels[y * size + x].a * coverage);
                }
                image.SetPixels32(pixels); image.Apply();
                snapshotPng = Convert.ToBase64String(image.EncodeToPNG());
                snapshot = image; image = null;
            }
            catch (Exception ex)
            {
                if (image != null) UnityEngine.Object.Destroy(image);
                if (++captureFailures == 1) Log.Warning("[PMP Singularity] Snapshot unavailable; retrying before touching objects: " + ex.Message);
                if (captureFailures >= 3)
                    FailBeforeConsumption();
                return;
            }
            // Capture finishes before any geometry or ownership changes. Each object is transferred exactly once.
            var transferred = new List<(Thing thing, IntVec3 cell, Rot4 rotation)>();
            try
            {
                foreach (var t in targets)
                {
                    if (!t.Spawned || t.Map != map || t.Destroyed) continue;
                    transferred.Add((t, t.Position, t.Rotation));
                    t.DeSpawn(DestroyMode.Vanish);
                    if (!t.Destroyed && !contents.TryAdd(t, false))
                        throw new InvalidOperationException("Singularity could not contain " + t);
                }
            }
            catch (Exception ex)
            {
                foreach (var entry in transferred)
                {
                    if (entry.thing.Destroyed || entry.thing.Spawned) continue;
                    contents.Remove(entry.thing);
                    GenSpawn.Spawn(entry.thing, entry.cell, map, entry.rotation);
                }
                Log.Error("[PMP Singularity] Capture aborted and held objects restored: " + ex);
                FailBeforeConsumption();
                return;
            }
            foreach (var cell in GenRadial.RadialCellsAround(Center, radius, true))
                if (cell.InBounds(map)) map.roofGrid.SetRoof(cell, null);
            captured = true;
        }

        private void FailBeforeConsumption()
        {
            completed = true;
            // Payment was committed at deployment. Return physical resources if capture could not start.
            if (paidSupport != null)
                foreach (var cost in paidSupport.costList)
                    for (int remaining = cost.count; remaining > 0;)
                    {
                        var refund = ThingMaker.MakeThing(cost.thingDef);
                        refund.stackCount = Math.Min(remaining, cost.thingDef.stackLimit);
                        remaining -= refund.stackCount;
                        GenPlace.TryPlaceThing(refund, Center, map, ThingPlaceMode.Near);
                    }
            paidSupport = null;
            ShipSupportPaymentService.InvalidateCache();
            Messages.Message("PMP.Singularity.CaptureFailed".Translate(), MessageTypeDefOf.RejectInput, false);
        }

        public override void MapComponentTick()
        {
            if (completed) return;
            if (!captured && age >= rupture) return;
            age++;
            if (captured && !consumed && age >= rupture + absorption)
            { DestroyContents(); consumed = true; }
            if (age >= rupture + absorption + closure) completed = true;
        }

        private static void DestroyTree(Thing thing, HashSet<Thing> visited)
        {
            if (thing == null || thing.Destroyed || !visited.Add(thing)) return;
            if (thing is IThingHolder holder)
            {
                var children = new List<IThingHolder>(); holder.GetChildHolders(children);
                var direct = holder.GetDirectlyHeldThings();
                if (direct != null) foreach (var child in direct.ToList()) DestroyTree(child, visited);
                foreach (var childHolder in children)
                {
                    var held = childHolder.GetDirectlyHeldThings();
                    if (held != null) foreach (var child in held.ToList()) DestroyTree(child, visited);
                }
            }
            if (!thing.Destroyed && thing.def.destroyable) thing.Destroy(DestroyMode.Vanish);
        }
        private void DestroyContents()
        {
            var visited = new HashSet<Thing>();
            foreach (var thing in contents.ToList()) DestroyTree(thing, visited);
            contents.Clear();
        }

        public override void MapComponentUpdate()
        {
            if (completed || map != Find.CurrentMap || !RimWorld.Planet.WorldRendererUtility.DrawingMap) return;
            Vector3 center = Center.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
            if (!captured)
            {
                if (captureRunner != null) return;
                WarpBlackHoleVisual.TryDraw(center, radius * 2, Rot4.North, .099f * Mathf.Clamp01(age / (float)rupture),
                    0, age / 60f, warpProperties);
                return;
            }
            if (snapshot == null && !string.IsNullOrEmpty(snapshotPng))
            {
                snapshot = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                snapshot.LoadImage(Convert.FromBase64String(snapshotPng));
                snapshot.wrapMode = TextureWrapMode.Clamp; snapshot.filterMode = FilterMode.Bilinear;
            }
            material ??= PersonaMaterials.Create("PMP17_SingularityMaterial");
            float closing = Mathf.Clamp01((age - rupture - absorption) / (float)closure);
            if (Find.CameraDriver.CurrentViewRect.Contains(Center))
                Find.CameraDriver.shaker.SetMinShake(shakeStrength * (1 - closing) * Prefs.ScreenShakeIntensity);
            material.SetTexture("_MainTex", snapshot);
            material.SetVector("_State", new Vector4(Mathf.Clamp01((age - rupture) / (float)absorption),
                closing, Mathf.Clamp01((age - rupture) / (float)(absorption + closure)), radius / halfSize));
            center.y += .03f;
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(center, Quaternion.identity, new Vector3(halfSize * 2, 1, halfSize * 2)),
                material, 0, null, 0, (MaterialPropertyBlock)null, ShadowCastingMode.Off, false, renderQueue: 3505);
        }

        public ThingOwner GetDirectlyHeldThings() => contents;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, contents);
        public override void ExposeData()
        {
            Scribe_Values.Look(ref Center, "center"); Scribe_Values.Look(ref radius, "radius", 8);
            Scribe_Values.Look(ref halfSize, "halfSize", 10); Scribe_Values.Look(ref age, "age");
            Scribe_Values.Look(ref shakeStrength, "shakeStrength", .18f);
            Scribe_Values.Look(ref rupture, "rupture", 60); Scribe_Values.Look(ref absorption, "absorption", 300);
            Scribe_Values.Look(ref closure, "closure", 60); Scribe_Values.Look(ref captured, "captured");
            Scribe_Values.Look(ref consumed, "consumed"); Scribe_Values.Look(ref completed, "completed");
            Scribe_Values.Look(ref snapshotPng, "snapshotPng"); Scribe_Deep.Look(ref contents, "contents", this);
            Scribe_Defs.Look(ref paidSupport, "paidSupport");
        }
        internal void DisposeVisuals()
        {
            if (captureRunner != null) UnityEngine.Object.Destroy(captureRunner.gameObject);
            captureRunner = null;
            if (snapshot != null) UnityEngine.Object.Destroy(snapshot);
            if (material != null) UnityEngine.Object.Destroy(material);
            snapshot = null; material = null;
        }
        public override void MapRemoved() { DestroyContents(); DisposeVisuals(); }
    }

    public sealed class SingularityDriver : GameComponent
    {
        public SingularityDriver(Game game) { }
        public override void GameComponentUpdate()
        {
            foreach (var map in Find.Maps)
            {
                var effect = map.GetComponent<SingularitySession>();
                if (effect == null) continue;
                effect.PrepareSnapshot();
                if (!effect.Completed) continue;
                effect.DisposeVisuals(); map.components.Remove(effect); ShipSupportRuntime.Invalidate();
            }
        }
    }
    [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
    internal static class SingularityCleanup
    {
        static void Prefix()
        {
            SingularityShipSupportWorker.TargetMap = null;
            foreach (var map in Find.Maps) map.GetComponent<SingularitySession>()?.DisposeVisuals();
        }
    }
}
