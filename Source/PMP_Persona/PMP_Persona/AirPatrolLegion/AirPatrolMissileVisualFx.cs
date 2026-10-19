using RimWorld;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace PMP_Persona.AirPatrolLegion
{
    // Shared Great Wyrm missile orb + trail VFX (orbandtail bundle, smoke fleck fallback).
    [StaticConstructorOnStartup]
    public sealed class AirPatrolMissileVisualFx
    {
        private const int TrailRingCapacity = 32;
        private const float PlaneDrawScale = 10.5f;
        private const float MissileOrbSize = 0.55f;
        private const float OrbIntensity = 3f;
        private const float TrailIntensity = 2.4f;
        private const float MissileTrailWidthFactor = 0.45f;
        private const float TrailWidthFactor = 1f;
        private const float MissileTrailTipWidthFactor = 0.18f;
        private const float MissileTrailSegmentLengthFactor = 1.05f;
        private const float MissileTrailSmokeSizeMin = 1f;
        private const float MissileTrailSolidTimeOverride = 1f;

        private static readonly Color MissileOrbColor = new Color(0.12f, 0.85f, 1f, 1f);
        private static readonly Color MissileTrailColor = new Color(0.12f, 0.85f, 1f, 1f);
        private static readonly Color MissileFleckColor = new Color(0.15f, 1f, 1f);

        private static readonly MaterialPropertyBlock OrbMpb = new MaterialPropertyBlock();
        private static readonly MaterialPropertyBlock TrailMpb = new MaterialPropertyBlock();
        private static readonly int CoreSizeId = Shader.PropertyToID("_CoreSize");
        private static readonly int GlowFalloffId = Shader.PropertyToID("_GlowFalloff");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        private Vector3[] trailRing;
        private int trailWrite;
        private int trailFill;
        private Mesh trailMesh;
        private int fleckEmitCounter;

        public void Init(string meshSuffix)
        {
            Dispose();
            trailRing = new Vector3[TrailRingCapacity];
            trailWrite = 0;
            trailFill = 0;
            trailMesh = new Mesh
            {
                name = "PMP_MissileTrail_" + meshSuffix
            };
            trailMesh.MarkDynamic();
            fleckEmitCounter = 0;
        }

        public void PushSample(Vector3 pos)
        {
            if (trailRing == null)
                return;

            if (trailFill > 0)
            {
                int newest = (trailWrite - 1 + trailRing.Length) % trailRing.Length;
                if ((trailRing[newest] - pos).sqrMagnitude < 0.0004f)
                    return;
            }

            trailRing[trailWrite] = pos;
            trailWrite = (trailWrite + 1) % trailRing.Length;
            if (trailFill < trailRing.Length)
                trailFill++;
        }

        public void TickFleckTrail(Map map, Vector3 pos)
        {
            if (map == null || AirPatrolOrbAndTailFx.TryGetMaterials(out _, out _))
                return;

            fleckEmitCounter++;
            if ((fleckEmitCounter & 1) == 1)
                return;

            float size = Mathf.Max(MissileTrailSmokeSizeMin, PlaneDrawScale * 0.08f);
            float scale = Rand.Range(0.9f, 1.3f) * size;
            FleckCreationData data = FleckMaker.GetDataStatic(pos, map, FleckDefOf.Smoke, scale);
            data.rotationRate = Rand.Range(-20f, 20f);
            data.velocity = Vector3.zero;
            data.velocitySpeed = 0f;
            data.instanceColor = MissileFleckColor;
            data.solidTimeOverride = MissileTrailSolidTimeOverride;
            map.flecks.CreateFleck(data);
        }

        public void Draw()
        {
            if (trailRing == null || trailFill < 1)
                return;

            if (!AirPatrolOrbAndTailFx.TryGetMaterials(out Material orbMat, out Material trailMat))
                return;

            Vector3 head = trailRing[(trailWrite - 1 + trailRing.Length) % trailRing.Length];
            float orbSize = Mathf.Max(0.06f, PlaneDrawScale * 0.06f * Mathf.Max(0.2f, MissileOrbSize));
            float trailWidth = Mathf.Max(
                0.015f,
                PlaneDrawScale * MissileTrailTipWidthFactor * MissileTrailWidthFactor * Mathf.Max(0.2f, TrailWidthFactor));

            if (trailFill >= 2)
            {
                int cap = trailRing.Length;
                int fill = Mathf.Min(trailFill, cap);
                int oldest = (trailWrite - fill + cap) % cap;
                DrawOrbTrailMesh(
                    trailRing,
                    oldest,
                    fill,
                    cap,
                    trailWidth,
                    MissileTrailSegmentLengthFactor,
                    MissileTrailTipWidthFactor,
                    0.12f,
                    0.95f,
                    MissileTrailColor,
                    TrailIntensity,
                    trailMat,
                    trailMesh);
            }

            DrawGlowOrb(head, orbSize, MissileOrbColor, OrbIntensity, orbMat);
        }

        public void Dispose()
        {
            if (trailMesh != null)
            {
                Object.Destroy(trailMesh);
                trailMesh = null;
            }

            trailRing = null;
            trailWrite = 0;
            trailFill = 0;
        }

        private static void DrawGlowOrb(Vector3 pos, float size, Color color, float intensity, Material orbMat)
        {
            OrbMpb.Clear();
            OrbMpb.SetColor(ShaderPropertyIDs.Color, color);
            OrbMpb.SetFloat(CoreSizeId, 0.25f);
            OrbMpb.SetFloat(GlowFalloffId, 2.5f);
            OrbMpb.SetFloat(IntensityId, Mathf.Max(0.2f, intensity));
            Graphics.DrawMesh(
                MeshPool.plane10,
                Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size, 1f, size)),
                orbMat,
                0,
                null,
                0,
                OrbMpb);
        }

        private static void DrawOrbTrailMesh(
            Vector3[] ring,
            int oldest,
            int fill,
            int cap,
            float width,
            float lenFactor,
            float tipWidthFactor,
            float aMin,
            float aMax,
            Color baseColor,
            float intensity,
            Material mat,
            Mesh mesh)
        {
            if (mesh == null || fill < 2)
                return;
            if (!UpdateTrailMeshFromRing(mesh, ring, oldest, fill, cap, width, lenFactor, tipWidthFactor))
                return;

            Color c = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(aMax));
            TrailMpb.Clear();
            TrailMpb.SetColor(ShaderPropertyIDs.Color, c);
            TrailMpb.SetFloat(IntensityId, Mathf.Max(0.05f, intensity));
            Graphics.DrawMesh(mesh, Matrix4x4.identity, mat, 0, null, 0, TrailMpb);
        }

        private static bool UpdateTrailMeshFromRing(
            Mesh trailMesh,
            Vector3[] ring,
            int oldest,
            int fill,
            int cap,
            float width,
            float lenFactor,
            float tipWidthFactor)
        {
            int pointCount = Mathf.Min(fill, cap);
            if (pointCount < 2)
                return false;

            Vector3[] points = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++)
                points[i] = ring[(oldest + i) % cap];

            int vertCount = pointCount * 2;
            int triCount = (pointCount - 1) * 6;
            Vector3[] verts = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];
            Color[] colors = new Color[vertCount];
            int[] tris = new int[triCount];

            for (int i = 0; i < pointCount; i++)
            {
                float t = pointCount > 1 ? i / (float)(pointCount - 1) : 0f;
                float widthAt = width * Mathf.Lerp(1f, tipWidthFactor, t);

                Vector3 dir;
                if (i == 0)
                    dir = points[1] - points[0];
                else if (i == pointCount - 1)
                    dir = points[i] - points[i - 1];
                else
                    dir = points[i + 1] - points[i - 1];

                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = Vector3.right;
                dir.Normalize();

                Vector3 right = Vector3.Cross(dir, Vector3.up);
                if (right.sqrMagnitude < 0.0001f)
                    right = Vector3.forward;
                right.Normalize();

                int vi = i * 2;
                float age = 1f - t;
                verts[vi] = points[i] - right * (widthAt * 0.5f);
                verts[vi + 1] = points[i] + right * (widthAt * 0.5f);
                uvs[vi] = new Vector2(0f, t * lenFactor);
                uvs[vi + 1] = new Vector2(1f, t * lenFactor);
                Color vc = new Color(age, 0f, 0f, 1f);
                colors[vi] = vc;
                colors[vi + 1] = vc;
            }

            int ti = 0;
            for (int i = 0; i < pointCount - 1; i++)
            {
                int v0 = i * 2;
                int v1 = v0 + 1;
                int v2 = v0 + 2;
                int v3 = v0 + 3;
                tris[ti++] = v0;
                tris[ti++] = v2;
                tris[ti++] = v1;
                tris[ti++] = v1;
                tris[ti++] = v2;
                tris[ti++] = v3;
            }

            trailMesh.Clear();
            trailMesh.vertices = verts;
            trailMesh.uv = uvs;
            trailMesh.colors = colors;
            trailMesh.triangles = tris;
            trailMesh.RecalculateBounds();
            return true;
        }
    }
}
