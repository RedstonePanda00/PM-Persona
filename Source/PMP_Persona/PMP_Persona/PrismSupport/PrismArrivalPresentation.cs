using System;
using HarmonyLib;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using PMP_Persona.VanguardSupport;

namespace PMP_Persona.PrismSupport
{
    internal sealed class PrismArrivalPresentation : IDisposable
    {
        private Material space, hullMaterial;
        private readonly Material[] jets = new Material[3];
        private Texture2D hull;
        private bool saved;
        private WorldRenderMode returnMode;
        private Quaternion returnRotation;
        private float returnAltitude;
        private Matrix4x4 returnProjection;
        private Vector4 planetDisc;
        private PlanetLayer returnLayer;
        private static readonly Action<WorldCameraDriver> ApplyWorld = AccessTools.MethodDelegate<Action<WorldCameraDriver>>(
            AccessTools.Method(typeof(WorldCameraDriver), "ApplyPositionToGameObject"));
        internal static float Timeline(OrbitalSupportSession ship) => ship.EntryElapsed / ship.EntryDuration * 10;
        internal static bool WantsWorld(OrbitalSupportSession ship) => Timeline(ship) >= 2.4f && Timeline(ship) < 8.2f;
        private Rect BaseHull(float w, float h) => new Rect(w * .42f, h * .40f, w * .48f,
            w * .48f * (hull != null ? (float)hull.height / hull.width : 942f / 1670));
        private Vector2 TransitFocus(float w, float h)
        {
            var rect = BaseHull(w, h);
            Vector2 direction = Vector2.Scale(new Vector2(.67f, .78f), rect.size).normalized;
            Vector2 bow = rect.position + Vector2.Scale(new Vector2(.13f, .04f), rect.size);
            return bow - direction * ((bow.x - w * .20f) / direction.x);
        }
        internal void ApplyCamera(OrbitalSupportSession ship)
        {
            if (!saved)
            {
                returnMode = Find.World.renderer.wantedMode;
                returnRotation = Find.WorldCameraDriver.sphereRotation;
                returnAltitude = Find.WorldCameraDriver.altitude;
                returnProjection = Find.WorldCamera.projectionMatrix;
                returnLayer = PlanetLayer.Selected; saved = true;
            }
            float t = Timeline(ship);
            if (WantsWorld(ship))
            {
                Find.World.renderer.wantedMode = WorldRenderMode.Planet;
                var camera = Find.WorldCameraDriver;
                camera.JumpTo(ship.Map.Tile);
                float p = VanguardPresentation.Ease((t - 3.8f) / 4.3f);
                camera.altitude = Mathf.Exp(Mathf.Lerp(Mathf.Log(950), Mathf.Log(WorldCameraDriver.MinAltitude), p));
                ApplyWorld(camera);
                float approach = VanguardPresentation.Ease((t - 3.8f) / 1.7f);
                float appear = VanguardPresentation.Ease((t - 2.5f) / 1.3f);
                float magnification = Mathf.Lerp(Mathf.Lerp(.002f, .25f, appear), 1, approach);
                Vector2 focus = TransitFocus(UI.screenWidth, UI.screenHeight);
                Vector2 center = Vector2.Lerp(new Vector2(focus.x / UI.screenWidth, focus.y / UI.screenHeight),
                    new Vector2(.5f, .5f), approach);
                var native = Find.WorldCamera; native.ResetProjectionMatrix();
                native.projectionMatrix = Matrix4x4.Translate(new Vector3(center.x * 2 - 1, center.y * 2 - 1, 0))
                    * Matrix4x4.Scale(new Vector3(magnification, magnification, 1)) * native.projectionMatrix;
                Vector3 projected = native.WorldToViewportPoint(PlanetLayer.Selected.Origin);
                float distance = Vector3.Distance(native.transform.position, PlanetLayer.Selected.Origin);
                float radius = PlanetLayer.Selected.Radius;
                float disc = radius / Mathf.Sqrt(Mathf.Max(1, distance * distance - radius * radius))
                    * Mathf.Abs(native.projectionMatrix.m11) * .5f;
                planetDisc = new Vector4(projected.x, 1 - projected.y, disc * 1.025f, t >= 2.5f ? 1 : 0);
            }
            else
            {
                Find.WorldCamera.projectionMatrix = returnProjection;
                planetDisc = Vector4.zero;
                Find.World.renderer.wantedMode = WorldRenderMode.None;
                float p = VanguardPresentation.Ease((t - 8.2f) / 1.6f);
                Find.CameraDriver.SetRootPosAndSize(ship.ViewPosition,
                    Mathf.Exp(Mathf.Lerp(Mathf.Log(100), Mathf.Log(VanguardFireControl.SensorSize), p)));
            }
        }
        internal void RestoreWorld()
        {
            if (!saved || Find.World == null) return;
            Find.World.renderer.wantedMode = returnMode;
            Find.WorldCameraDriver.sphereRotation = returnRotation;
            Find.WorldCameraDriver.altitude = returnAltitude;
            Find.WorldCamera.projectionMatrix = returnProjection;
            if (returnLayer != null) PlanetLayer.Selected = returnLayer;
            ApplyWorld(Find.WorldCameraDriver); saved = false;
        }
        internal void Draw(OrbitalSupportSession ship)
        {
            float t = Timeline(ship), w = UI.screenWidth, h = UI.screenHeight;
            var full = new Rect(0, 0, w, h);
            if (space == null)
            {
                space = PersonaMaterials.Create("PMP17_PrismArrivalMaterial");
                hullMaterial = PersonaMaterials.Create("PMP17_VanguardMaterial");
                hull = ContentFinder<Texture2D>.Get("UI/Prism/ArrivalHull");
                for (int i = 0; i < 3; i++) jets[i] = PersonaMaterials.Create("PMP17_PrismArrivalMaterial");
            }
            var color = GUI.color; var anchor = Text.Anchor;
            GUI.color = Color.white;
            if (Event.current.type == EventType.Repaint && t < 5.5f)
            {
                float visibility = 1 - VanguardPresentation.Ease((t - 3.8f) / 1.7f);
                space.SetVector("_Clock", new Vector4(t, 0, visibility, w / h));
                space.SetVector("_Planet", planetDisc);
                // The hull is a stable alpha sprite. All motion, jets and scenery are procedural.
                Vector2 tremor = new Vector2(Mathf.Sin(t * 47) + .4f * Mathf.Sin(t * 79), Mathf.Sin(t * 61)) * 1.5f;
                Rect shipRect = BaseHull(w, h);
                Vector2 focus = TransitFocus(w, h);
                float approach = VanguardPresentation.Ease((t - 3.8f) / 1.7f);
                float zoom = Mathf.Lerp(1, 4, approach);
                // Advancing towards the planet naturally carries the foreground hull out of
                // frame. No transparency or blur is applied to the ship at any point.
                shipRect.position = focus + (shipRect.position - focus) * zoom + tremor;
                shipRect.size *= zoom;
                Vector2[] nozzles = { new Vector2(.686f, .903f), new Vector2(.810f, .824f), new Vector2(.897f, .730f) };
                // Project the sprite's longitudinal axis, including its aspect ratio. The wake
                // remains parallel to the hull on every screen aspect and shares its tremor.
                Vector2 direction = Vector2.Scale(new Vector2(.67f, .78f), shipRect.size).normalized;
                space.SetVector("_Jet", new Vector4(focus.x / w, focus.y / h, 0, 0));
                Graphics.DrawTexture(full, BaseContent.WhiteTex, space);
                for (int i = 0; i < 3; i++)
                {
                    Vector2 at = shipRect.position + Vector2.Scale(nozzles[i], shipRect.size);
                    jets[i].SetVector("_Clock", new Vector4(t + i * 3.7f, 1, 1, w / h));
                    jets[i].SetVector("_Jet", new Vector4(at.x / w, at.y / h, direction.x, direction.y));
                    Graphics.DrawTexture(full, BaseContent.WhiteTex, jets[i]);
                }
                hullMaterial.SetVector("_Effect", new Vector4(t, 0, 1, 0));
                Graphics.DrawTexture(shipRect, hull, hullMaterial);
            }
            // A short iris/black handoff hides the world's minimum altitude limit.
            float black = t < .3f ? 1 - VanguardPresentation.Ease(t / .3f)
                : t >= 7.95f && t < 8.2f ? VanguardPresentation.Ease((t - 7.95f) / .25f)
                : t >= 8.2f && t < 8.45f ? 1 - VanguardPresentation.Ease((t - 8.2f) / .25f)
                : t > 9.8f ? VanguardPresentation.Ease((t - 9.8f) / .12f) : 0;
            if (black > 0) Widgets.DrawBoxSolid(full, new Color(0, 0, 0, black));
            if (black < .99f)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(new Rect(0, h - 60, w, 30), (t < 4 ? "PMP.Prism.Transit" : t < 8.2f ? "PMP.Prism.WorldLock" : "PMP.Vanguard.Connecting").Translate());
            }
            GUI.color = color; Text.Anchor = anchor;
        }
        public void Dispose()
        {
            if (space != null) UnityEngine.Object.Destroy(space);
            if (hullMaterial != null) UnityEngine.Object.Destroy(hullMaterial);
            foreach (var jet in jets) if (jet != null) UnityEngine.Object.Destroy(jet);
            space = hullMaterial = null;
        }
    }
    [HarmonyPatch(typeof(WorldCameraDriver), nameof(WorldCameraDriver.Update))]
    internal static class PrismWorldCameraLock
    {
        static bool Prefix()
        {
            var controller = VanguardFireControl.CurrentController;
            if (!VanguardFireControl.OwnsView || controller.Ship?.IsPrism != true || controller.Ship.Ready) return true;
            if (!PrismArrivalPresentation.WantsWorld(controller.Ship)) return true;
            controller.PrismArrival.ApplyCamera(controller.Ship); return false;
        }
    }
}
