using System;
using System.Collections;
using UnityEngine;
using UnityStubs.Rendering;
using Verse;

namespace PMP_Persona.SingularitySupport
{
    internal static class SingularitySnapshot
    {
        // Read the actual end-of-frame screen, including current lighting and weather.
        internal static Texture2D Capture(Map map, Vector3 center, float halfSize)
        {
            const int size = 1024;
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = "PMP singularity screen sample", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            try
            {
                var pixels = new Color32[size * size];
                if (map == Find.CurrentMap && RimWorld.Planet.WorldRendererUtility.DrawingMap)
                {
                    if (ScreenCaptureBackend.Current == null || !ScreenCaptureBackend.Current.TryReadScreen(out var rgba, out int width, out int height))
                        throw new InvalidOperationException("End-of-frame screen sample unavailable");
                    var camera = Find.CameraDriver.GetComponent<Camera>();
                    // Native screen and texture buffers store top-down rows. North is row 0.
                    Vector3 corner = camera.WorldToScreenPoint(center + new Vector3(-halfSize, 0, halfSize));
                    Vector3 across = camera.WorldToScreenPoint(center + new Vector3(halfSize, 0, halfSize)) - corner;
                    Vector3 down = camera.WorldToScreenPoint(center + new Vector3(-halfSize, 0, -halfSize)) - corner;
                    for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
                    {
                        Vector3 point = corner + across * ((x + .5f) / size) + down * ((y + .5f) / size);
                        // Waylith's WorldToScreenPoint already returns top-down pixel Y.
                        int sx = Mathf.FloorToInt(point.x), sy = Mathf.FloorToInt(point.y);
                        int at = (sy * width + sx) * 4;
                        // Main toolbar and colonist bar never belong to the map sample.
                        // Off-screen pixels stay transparent rather than reconstructing unseen imagery.
                        if (sx < 0 || sx >= width || sy < 80 * Prefs.UIScale || sy >= height - 40 * Prefs.UIScale) continue;
                        pixels[y * size + x] = new Color32(rgba[at], rgba[at+1], rgba[at+2], 255);
                    }
                }
                image.SetPixels32(pixels); image.Apply();
                return image;
            }
            catch { UnityEngine.Object.Destroy(image); throw; }
        }
    }

    // Short-lived capture coroutine; the map component owns gameplay and serialized state.
    public sealed class SingularityCaptureRunner : MonoBehaviour
    {
        private SingularitySession session;
        internal static SingularityCaptureRunner Begin(SingularitySession session)
        {
            var runner = new GameObject("PMP singularity screen sampler").AddComponent<SingularityCaptureRunner>();
            runner.session = session; runner.StartCoroutine(runner.Capture()); return runner;
        }
        private IEnumerator Capture()
        {
            yield return null; // clean frame without the opening rupture baked into the sample
            yield return new WaitForEndOfFrame();
            yield return null;
            ScreenCaptureBackend.Current?.RequestCapture();
            yield return new WaitForEndOfFrame();
            // The native readback is published after frame completion. Resume on the next frame.
            yield return null;
            yield return new WaitForEndOfFrame();
            try { session.CaptureAtEndOfFrame(); }
            finally { session.CaptureFinished(); UnityEngine.Object.Destroy(gameObject); }
        }
    }
}
