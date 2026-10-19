using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    public partial class Building_CarrierOrbiter
    {
        private static Material warpStreakMaterial;
        private static Mesh warpQuad;
        private static readonly MaterialPropertyBlock WarpProperties = new MaterialPropertyBlock();

        private void ApplyDroneWarpVisual(DroneState d, ref Vector3 scale, float lifeVisibility)
        {
            if (d.flightState != 4 && d.flightState != 6 && d.flightState != 8) return;
            bool entering = d.flightState == 6;
            float p = Mathf.Clamp01((Find.TickManager.TicksGame - d.warpStartTick) / (float)(entering ? WarpInTicks : WarpOutTicks));
            float visibility = entering ? Mathf.SmoothStep(0, 1, p / .35f)
                : 1 - Mathf.SmoothStep(0, 1, (p - .55f) / .45f);
            // Local Z is the aircraft's flight axis. The distortion is directional, not an idle size pulse.
            scale.x *= entering ? Mathf.Lerp(.12f, 1, Mathf.SmoothStep(0, 1, p)) : Mathf.Lerp(1, .08f, p);
            scale.z *= entering ? Mathf.Lerp(3, 1, Mathf.SmoothStep(0, 1, p)) : Mathf.Lerp(1, 3, p);
            scale.x *= visibility; scale.z *= visibility;
            float intensity = Mathf.Sin(p * Mathf.PI) * lifeVisibility;
            if (intensity <= .001f) return;

            if (warpStreakMaterial == null)
            {
                // Reuse Zeta's native GLSL FTL streak; no permanent duplicate ship overlay.
                warpStreakMaterial = PersonaMaterials.Create("PMP17_VanguardMaterial");
                warpStreakMaterial.SetTexture("_MainTex", BaseContent.WhiteTex);
                warpQuad = new Mesh { name = "Legion warp streak" };
                warpQuad.vertices = new[] { new Vector3(-.5f, 0, -.5f), new Vector3(-.5f, 0, .5f),
                    new Vector3(.5f, 0, .5f), new Vector3(.5f, 0, -.5f) };
                warpQuad.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
                warpQuad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                warpQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 }; warpQuad.RecalculateBounds();
            }
            float length = d.scale * 15, width = d.scale * 4;
            Vector2 direction = d.flightDirXZ;
            Vector2 center = d.position - direction * (length * .4f);
            float yaw = -Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg + 180;
            WarpProperties.SetVector("_Effect", new Vector4(p, 1, intensity, 7));
            WarpProperties.SetVector("_Shape", new Vector4(length / width, 0, 0, 0));
            Graphics.DrawMesh(warpQuad, Matrix4x4.TRS(new Vector3(center.x, AltitudeLayer.MoteOverhead.AltitudeFor() + .01f, center.y),
                Quaternion.Euler(0, yaw, 0), new Vector3(length, 1, width)), warpStreakMaterial, 0, null, 0,
                WarpProperties, ShadowCastingMode.Off, false);
        }

        internal int HeadFlightState => flockIndex[0].flightState;
        internal int HeadWarpStart => flockIndex[0].warpStartTick;
        internal void PrepareWarpAuditPose(int phase, int age)
        {
            var d = flockIndex[0];
            d.position = new Vector2(Map.Center.x + .5f, Map.Center.z + .5f);
            d.flightState = (byte)phase; d.warpStartTick = Find.TickManager.TicksGame - age;
            d.flightDirXZ = Vector2.right; d.velocity = Vector2.right * .3f;
            // Isolate the inspected member in the opt-in screenshot scene.
            if (phase == 4)
                for (int i = 1; i < flockIndex.Length; i++)
                    if (Vector2.Distance(flockIndex[i].position, d.position) < 22)
                        flockIndex[i].position = KeepInside(d.position + new Vector2(35 + i % 12, 35 + i % 9));
        }
        internal void AuditResumeWarp()
        {
            var d = flockIndex[0];
            int now = Find.TickManager.TicksGame;
            bool entered = false;
            for (int t = 1; t < 300 && d.flightState != 0; t++)
            {
                ageTicks++; TickFlock(now + t);
                entered |= d.flightState == 6;
            }
            if (!entered || d.flightState != 0) throw new System.Exception("Saved transit failed to warp in/rejoin");
        }
    }
}
