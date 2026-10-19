using UnityEngine;
using UnityEngine.Rendering;
using Verse;
using PMP_Persona.SupernovaSupport;

namespace PMP_Persona.PrismSupport
{
    // Shared projected volume for overhead map cameras. Ground anchors stay exact.
    internal static class OrbitalBeamVolume
    {
        internal static void Draw(Material material, Vector3 point, float halfWidth, Vector4 beam)
        {
            if (material == null || beam.z <= 0) return;
            var camera = Find.Camera;
            point.y = .1f;
            float height = Mathf.Max(2, camera.transform.position.z + camera.orthographicSize + 3 - point.z);
            var projection = Matrix4x4.identity;
            projection.m21 = 1; projection.m11 = .10f;
            var matrix = Matrix4x4.Translate(point) * projection * Matrix4x4.TRS(
                new Vector3(0, height * .5f, 0), Quaternion.identity, new Vector3(halfWidth, height * .5f, halfWidth));
            var inverse = matrix.inverse;
            Vector3 eye = inverse.MultiplyPoint3x4(camera.transform.position);
            Vector3 direction = inverse.MultiplyVector(camera.transform.forward).normalized;
            material.SetVector("_CameraLocal", new Vector4(eye.x, eye.y, eye.z, camera.orthographic ? 1 : 0));
            material.SetVector("_RayDirection", new Vector4(direction.x, direction.y, direction.z, 0));
            material.SetVector("_Beam", beam);
            Graphics.DrawMesh(AstroSupernovaRenderer.BoundsMesh, matrix, material, 0, null, 0,
                (MaterialPropertyBlock)null, ShadowCastingMode.Off, false, renderQueue: 3105);
        }
    }
}
