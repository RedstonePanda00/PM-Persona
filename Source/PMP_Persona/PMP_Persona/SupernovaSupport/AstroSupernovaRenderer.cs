using System;
using UnityEngine;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    // Rendering only. The field's radius, lifetime, targeting and damage remain in SupernovaConstants.
    [StaticConstructorOnStartup]
    internal static class AstroSupernovaRenderer
    {
        private static Material material;
        private static Mesh bounds;
        private static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        internal const string MaterialName = "PMP17_AstroSupernova";

        internal static Vector4 Evolution(SupernovaShellStageState stage)
        {
            float age = Mathf.Clamp01((stage.progress * stage.totalDuration - stage.preFlashDuration) / Mathf.Max(.01f, stage.totalDuration - stage.preFlashDuration));
            // Dimensionless Sedov-Taylor shock R ~ t^(2/5), and freely expanding ejecta R ~ t.
            float shock = .045f + .905f * Mathf.Pow(age, .4f);
            float ejecta = .035f + .715f * age;
            // Mass conservation in homologous ejecta: rho ~ R^-3; radiation-dominated adiabatic T ~ R^-1.
            float density = Mathf.Pow(.25f / ejecta, 3f);
            float temperature = Mathf.Clamp(18000f * .12f / ejecta, 2800f, 24000f);
            return new Vector4(shock, ejecta, density, temperature);
        }

        internal static Mesh BoundsMesh
        {
            get
            {
                if (bounds != null) return bounds;
                var vertices = new Vector3[24]; var normals = new Vector3[24]; var triangles = new int[36];
                Vector3[] directions = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                for (int face = 0; face < 6; face++)
                {
                    Vector3 normal = directions[face];
                    Vector3 tangent = Math.Abs(normal.y) > .5f ? Vector3.right : Vector3.up;
                    Vector3 bitangent = Vector3.Cross(normal, tangent);
                    int v = face * 4, t = face * 6;
                    vertices[v] = normal - tangent - bitangent;
                    vertices[v + 1] = normal + tangent - bitangent;
                    vertices[v + 2] = normal + tangent + bitangent;
                    vertices[v + 3] = normal - tangent + bitangent;
                    for (int i = 0; i < 4; i++) normals[v + i] = normal;
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                    triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                }
                bounds = new Mesh { name = "PMP astronomical volume bounds", vertices = vertices, normals = normals, triangles = triangles };
                bounds.RecalculateBounds();
                return bounds;
            }
        }

        internal static void Configure(MaterialPropertyBlock properties, Matrix4x4 matrix, Camera camera,
            float radius, float stellarRadius, SupernovaShellStageState stage, float seed, bool groundClip)
        {
            properties.Clear();
            Matrix4x4 inverse = matrix.inverse;
            Vector3 eye = inverse.MultiplyPoint3x4(camera.transform.position);
            Vector3 direction = inverse.MultiplyVector(camera.transform.forward).normalized;
            properties.SetVector("_CameraLocal", new Vector4(eye.x, eye.y, eye.z, camera.orthographic ? 1 : 0));
            properties.SetVector("_RayDirection", new Vector4(direction.x, direction.y, direction.z, 0));
            properties.SetVector("_Evolution", Evolution(stage));
            properties.SetVector("_Geometry", new Vector4(stellarRadius / radius, radius, matrix.m13, groundClip ? 1 : 0));
            properties.SetVector("_Clock", new Vector4(stage.localTime, seed, stage.visibility, stage.progress));
            properties.SetVector("_Phases", new Vector4(stage.preFlash, stage.expansion, stage.interaction, stage.decay));
        }

        internal static void Draw(Vector3 center, float radius, float stellarRadius, SupernovaShellStageState stage, int seed,
            float appearance = 1f, float centerHeight = -1f)
        {
            Camera camera = Find.Camera;
            if (camera == null) return;
            material ??= PersonaMaterials.Create(MaterialName);
            // Transform volume rays into object-local coordinates.
            center.y = centerHeight >= 0f ? centerHeight : AltitudeLayer.MoteOverhead.AltitudeFor() + stellarRadius;
            var matrix = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * radius);
            Configure(block, matrix, camera, radius, stellarRadius, stage, seed % 997, true);
            block.SetFloat("_Appearance", appearance);
            Graphics.DrawMesh(BoundsMesh, matrix, material, 0, camera, 0, block);
        }

        internal static void DrawPreview(Material preview, Camera camera, SupernovaShellStageState stage, float starFraction, int seed,
            Matrix4x4? previewMatrix = null, float appearance = 1f)
        {
            var matrix = previewMatrix ?? Matrix4x4.identity;
            float radius = matrix.lossyScale.x;
            Configure(block, matrix, camera, radius, starFraction * radius, stage, seed, false);
            preview.SetFloat("_Appearance", appearance);
            // DrawMeshNow has no property-block overload; use a private preview material.
            foreach (string name in new[] { "_CameraLocal", "_RayDirection", "_Evolution", "_Geometry", "_Clock", "_Phases" })
                preview.SetVector(name, block.GetVector(name));
            if (!preview.SetPass(0)) throw new InvalidOperationException("Astronomical volume shader SetPass failed.");
            Graphics.DrawMeshNow(BoundsMesh, matrix);
        }
    }
}
