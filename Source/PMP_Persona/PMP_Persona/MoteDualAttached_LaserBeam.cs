using System;
using System.IO;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    [StaticConstructorOnStartup]
    public class MoteDualAttached_LaserBeam : MoteDualAttached
    {
        private const float BeamWidthScale = 2.8f;

        private static readonly MaterialPropertyBlock BeamMpb = new MaterialPropertyBlock();
        private static Material laserRuntimeMat;
        private static Mesh beamPlaneMesh;

        static MoteDualAttached_LaserBeam()
        {
            EnsureLoaded();
            beamPlaneMesh = CreateBeamPlaneMeshWithSwappedUv();
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            UpdatePositionAndRotation();

            Material customMat = ResolveRuntimeMat();
            if (customMat == null)
            {
                base.DrawAt(drawLoc, flip);
                return;
            }

            float width = Mathf.Max(0.03f, linearScale.x * BeamWidthScale);
            float length = Mathf.Max(0.01f, linearScale.z);

            float pulse = 1f + Mathf.Sin(Time.time * 26f + thingIDNumber * 0.1f) * 0.05f;
            width *= pulse;

            Vector3 pos = exactPosition;
            pos.y = def.altitudeLayer.AltitudeFor();

            Matrix4x4 matrix = Matrix4x4.TRS(
                pos,
                Quaternion.AngleAxis(exactRotation, Vector3.up),
                new Vector3(width, 1f, length)
            );

            float totalSec = def.mote.fadeInTime + def.mote.solidTime + def.mote.fadeOutTime;
            float lifePhase = totalSec > 0f
                ? Mathf.Clamp01((Find.TickManager.TicksGame - spawnTick) / 60f / totalSec)
                : 0.5f;

            BeamMpb.Clear();
            BeamMpb.SetFloat("_LifePhase", lifePhase);

            Graphics.DrawMesh(beamPlaneMesh ?? MeshPool.plane10, matrix, customMat, 0, null, 0, BeamMpb);
        }

        // LaserBeam shader expects uv.x = beam length, uv.y = beam width.
        // plane10 maps u along local X (width) and v along local Z (length) — swap UV only, keep world orientation.
        private static Mesh CreateBeamPlaneMeshWithSwappedUv()
        {
            Mesh src = MeshPool.plane10;
            if (src == null)
                return null;

            Mesh mesh = new Mesh
            {
                name = "PMP_LaserBeamPlane_SwappedUv"
            };
            mesh.vertices = src.vertices;
            mesh.triangles = src.triangles;
            mesh.normals = src.normals;

            Vector2[] srcUv = src.uv;
            Vector2[] swappedUv = new Vector2[srcUv.Length];
            for (int i = 0; i < srcUv.Length; i++)
                swappedUv[i] = new Vector2(srcUv[i].y, srcUv[i].x);
            mesh.uv = swappedUv;

            return mesh;
        }

        private static Material ResolveRuntimeMat()
        {
            EnsureLoaded();
            return laserRuntimeMat;
        }

        private static void EnsureLoaded()
        {
            if (laserRuntimeMat == null)
                laserRuntimeMat = PersonaMaterials.Create("PMP17_Mat_LaserBeam");
        }
    }
}
