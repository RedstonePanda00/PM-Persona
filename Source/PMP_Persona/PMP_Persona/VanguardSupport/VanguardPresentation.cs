using System;
using RimWorld;
using UnityEngine;
using Verse;
using PMP_Persona.Rendering;

namespace PMP_Persona.VanguardSupport
{
    // Side/above sprite presentation for orbital support.
    internal sealed class VanguardPresentation : IDisposable
    {
        private Texture2D hull, cannons;
        private Material hullMaterial, clouds, portal, warpRibbon, sensor, plasmaMaterial, thermalBody;
        private readonly OrbitalSensorCapture worldCapture = new OrbitalSensorCapture();
        private RenderTexture feed;
        private int captureFrame = -1, captureCacheFailures;
        private bool hasSensorFrame;
        private readonly OrbitalHudRenderer hud = new OrbitalHudRenderer();
        internal float MissileFlash;
        private readonly int[] recoilTick = { -999999, -999999 };
        private int nextCannon;
        private Vector2 cannonSway, swayVelocity;
        internal Vector2 CannonSway => cannonSway;
        internal float CannonKick(int side) => CannonKick(side, Find.TickManager.TicksGame);
        internal float CannonKick(int side, int tick)
        {
            // Kick on the actual shot tick, recover before the next 15-tick firing interval.
            return 1 - Ease((tick - recoilTick[side]) / 14f);
        }
        internal void FireCannon(int tick) { recoilTick[nextCannon] = tick; nextCannon = 1 - nextCannon; }
        internal void Advance(float dt)
        {
            MissileFlash = Mathf.Max(0, MissileFlash - dt * 3);
        }
        internal void MoveCannons(Vector2 movement, float dt)
        {
            // Damped spring: X = roll in clockwise degrees, Y = vertical offset in UI pixels.
            Vector2 target = new Vector2(movement.x * 5, movement.y * 7);
            float remaining = Mathf.Min(dt, .1f);
            while (remaining > 0)
            {
                float step = Mathf.Min(remaining, 1f / 120);
                swayVelocity += ((target - cannonSway) * 65 - swayVelocity * 11) * step;
                cannonSway += swayVelocity * step; remaining -= step;
            }
        }
        internal const float WarpStart = 1.6f, WarpArrival = 1.74f, ApproachStart = 1.84f, EntryEnd = 6.3f;
        internal const float ApproachEnd = EntryEnd;
        internal const float WarpStartX = 1.04f, WarpDistance = .58f / 3;
        internal const float ArrivalHeight = 32, SettledHeight = 18, PlasmaWakeLength = 3.2f;
        internal static float ApproachProgress(float t) => Ease((t - ApproachStart) / (ApproachEnd - ApproachStart));
        internal static float ArrivalBlack(float t) => Ease((t - (EntryEnd - .20f)) / .15f);
        internal static Vector2 ArrivalPosition(float t)
        {
            // Arrival transit followed by shared descent and camera approach.
            float transit = Mathf.Clamp01((t - WarpStart) / (WarpArrival - WarpStart));
            // Ease arrival braking and descent.
            transit = 1 - Mathf.Pow(1 - transit, 3);
            float approach = ApproachProgress(t);
            var warpStart = new Vector2(WarpStartX, -.04f);
            var warpEnd = warpStart + new Vector2(-WarpDistance, WarpDistance * .7f);
            return Vector2.Lerp(Vector2.Lerp(warpStart, warpEnd, transit), new Vector2(.5f, .5f), approach);
        }
        internal static Vector2 ArrivalWarp(float t) => new Vector2(Mathf.Lerp(1f / 3, 1,
            Ease((t - WarpArrival) / (ApproachStart - WarpArrival))), 1);
        internal static float ArrivalPitch(float t) => 10 * Ease((t - WarpArrival) / (ApproachStart - WarpArrival)) * (1 - ApproachProgress(t));
        internal static float CameraApproach(float t) => ApproachProgress(t);
        internal static float WarpLight(float t)
        {
            if (t < WarpStart || t >= ApproachStart) return 0;
            float streak = Ease((t - WarpStart) / .025f) * (1 - Ease((t - WarpArrival) / (ApproachStart - WarpArrival)));
            float burst = Mathf.Exp(-Mathf.Pow((t - WarpArrival) / .04f, 2));
            return Mathf.Clamp01(streak * .75f + burst);
        }
        internal static Vector3 ArrivalWorld(OrbitalSupportSession ship, float t)
        {
            Vector2 p = ArrivalPosition(t);
            return new Vector3(p.x * ship.Map.Size.x, Mathf.Lerp(ArrivalHeight, SettledHeight, ApproachProgress(t)),
                (1 - p.y) * ship.Map.Size.z);
        }
        internal static float ArrivalShakeAmplitude(float t) => Mathf.Lerp(10, 2.5f, ApproachProgress(t));
        internal static Vector2 ArrivalShake(float time, float duration)
        {
            float t = time / Mathf.Max(.01f, duration) * 7;
            float amplitude = ArrivalShakeAmplitude(t) * Ease(time / .15f);
            return new Vector2(Mathf.Sin(time * 73) + .45f * Mathf.Sin(time * 113),
                Mathf.Sin(time * 89 + .7f) + .4f * Mathf.Sin(time * 137)) * amplitude;
        }
        internal static Rect ProjectHull(Vector3 center, float width, float height)
        {
            // Compute positive screen extents across the camera plane.
            Vector2 at = UI.MapToUIPosition(center);
            Vector2 left = UI.MapToUIPosition(center - Vector3.right * width * .5f);
            Vector2 right = UI.MapToUIPosition(center + Vector3.right * width * .5f);
            Vector2 top = UI.MapToUIPosition(center + Vector3.forward * height * .5f);
            Vector2 bottom = UI.MapToUIPosition(center - Vector3.forward * height * .5f);
            float w = Mathf.Abs(right.x - left.x), h = Mathf.Abs(bottom.y - top.y);
            return new Rect(at.x - w * .5f, at.y - h * .5f, w, h);
        }
        private static readonly Color White = new Color(.87f, .92f, .94f, .9f);
        private static readonly Color Amber = new Color(1, .76f, .37f, .95f);
        internal static Rect PauseRect => new Rect(UI.screenWidth - 150, 16, 134, 28);
        internal static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        private void EnsureMaterials()
        {
            if (hullMaterial != null) return;
            hull = ContentFinder<Texture2D>.Get("UI/Vanguard/ArrivalHull");
            cannons = ContentFinder<Texture2D>.Get("UI/Vanguard/CannonForeground");
            hullMaterial = PersonaMaterials.Create("PMP17_VanguardMaterial");
            clouds = PersonaMaterials.Create("PMP17_VanguardMaterial");
            portal = PersonaMaterials.Create("PMP17_VanguardMaterial");
            warpRibbon = PersonaMaterials.Create("PMP17_VanguardMaterial");
            sensor = PersonaMaterials.Create("PMP17_VanguardMaterial");
            thermalBody = PersonaMaterials.Create("PMP17_VanguardMaterial");
            plasmaMaterial = PersonaMaterials.Create(TargetPlasmaRenderer.MaterialName);
        }

        internal void CaptureWorld(OrbitalSupportSession ship)
        {
            if (ship.Phase == OrbitalSupportSession.SupportPhase.Arrival || captureFrame == Time.frameCount) return;
            EnsureMaterials();
            if (ship.Infrared)
                foreach (var pawn in ship.Map.mapPawns.AllPawnsSpawned)
                    if (!pawn.Dead && Find.CameraDriver.CurrentViewRect.Contains(pawn.Position))
                        TargetPlasmaRenderer.VisibleCapture(pawn);
            int width = Mathf.Min(Screen.width, 1600);
            int height = Mathf.Max(1, Mathf.RoundToInt((float)Screen.height * width / Screen.width));
            if (feed == null || feed.width != width || feed.height != height)
            {
                if (feed != null) RenderTexture.ReleaseTemporary(feed);
                feed = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
                feed.filterMode = FilterMode.Bilinear; hasSensorFrame = false;
            }
            Camera source = Find.CameraDriver.MyCamera;
            var previous = RenderTexture.active;
            try
            {
                if (!worldCapture.Render(source, feed, new Color(.025f, .03f, .035f))) return;
                captureFrame = Time.frameCount;
                hasSensorFrame = true; captureCacheFailures = 0;
            }
            catch (ArgumentException ex) when (captureCacheFailures < 2 &&
                ex.Message.Contains("Waylith.Rendering.VulkanShaderLayoutCache+PipelineLayoutKey"))
            {
                // Retain the last sensor frame and retry failed captures.
                captureCacheFailures++; captureFrame = Time.frameCount;
                if (captureCacheFailures == 1) Log.Warning("[PMP] Native sensor pipeline cache raced initialization; retrying next frame.");
            }
            finally { RenderTexture.active = previous; }
        }

        private static void Effect(Rect rect, Texture texture, Material material, int mode, float time, float amount, float opacity = 1)
        {
            material.SetVector("_Effect", new Vector4(time, amount, opacity, mode));
            material.SetVector("_Shape", new Vector4(rect.width / Mathf.Max(1, rect.height), 0, 0, 0));
            Graphics.DrawTexture(rect, texture, material);
        }

        internal void Draw(OrbitalSupportSession ship)
        {
            // Departure never renders a sensor feed/HUD, even on its first or last frame.
            if (ship.Phase == OrbitalSupportSession.SupportPhase.Departure)
            {
                Widgets.DrawBoxSolid(new Rect(0, 0, UI.screenWidth, UI.screenHeight), Color.black);
                return;
            }
            EnsureMaterials();
            // Capture after native ParticleSystem frame submission, immediately before sensor GUI.
            if (Event.current.type == EventType.Repaint) CaptureWorld(ship);
            float w = UI.screenWidth, h = UI.screenHeight;
            var full = new Rect(0, 0, w, h);
            var savedColor = GUI.color; var savedFont = Text.Font; var savedAnchor = Text.Anchor;
            var savedMatrix = GUI.matrix;
            try
            {
                GUI.color = Color.white;
                if (ship.Phase == OrbitalSupportSession.SupportPhase.Arrival)
                {
                    var shake = ArrivalShake(ship.EntryElapsed, ship.EntryDuration);
                    GUI.matrix = savedMatrix * Matrix4x4.Translate(new Vector3(shake.x, shake.y, 0));
                    DrawArrival(ship, full);
                }
                else
                {
                    if (Event.current.type == EventType.Repaint && feed != null && hasSensorFrame)
                        Effect(full, feed, sensor, ship.Infrared ? 2 : 4, Time.realtimeSinceStartup, 1);
                    if (ship.Infrared && Event.current.type == EventType.Repaint) DrawThermalTargets(ship);
                    DrawFireControl(ship, full);
                }
            }
            finally { GUI.matrix = savedMatrix; GUI.color = savedColor; Text.Font = savedFont; Text.Anchor = savedAnchor; }
        }

        private void DrawArrival(OrbitalSupportSession ship, Rect full)
        {
            float t = ship.EntryElapsed / ship.EntryDuration * 7;
            float w = full.width, h = full.height;
            float progress = ApproachProgress(t);
            // Once covered, stop submitting the close hull entirely before camera handoff.
            if (ArrivalBlack(t) >= 1) {
                Widgets.DrawBoxSolid(new Rect(-32, -32, w + 64, h + 64), Color.black);return;
            }
            float fog = Ease(t / 1.7f) * (1 - Ease((progress - .55f) / .45f));
            if (Event.current.type == EventType.Repaint)
            {
                Effect(full, BaseContent.WhiteTex, clouds, 1, t, fog);
                // Project the fixed-size hull during the approach to the sensor position.
                Vector3 center = ArrivalWorld(ship, t);
                Vector2 warp = ArrivalWarp(t);
                float worldWidth = VanguardFireControl.OverviewSize(ship) * 2 * w / h * .195f;
                float worldHeight = worldWidth * hull.height / hull.width;
                var rect = ProjectHull(center, worldWidth * warp.x, worldHeight * warp.y);
                float appear = Ease((t - WarpStart) / .07f);
                float opacity = appear;
                float warpLight = WarpLight(t);
                if (warpLight > 0)
                {
                    // Render the directional departure streak and exit flare.
                    var ribbon = new Rect(rect.center.x - w * .09f, rect.center.y - h * .10f, w * .9f, h * .20f);
                    Effect(ribbon, BaseContent.WhiteTex, warpRibbon, 7, t - WarpStart, warpLight);
                }
                var shadowRect = ProjectHull(new Vector3(center.x + center.y * .35f, 0, center.z - center.y * .6f),
                    worldWidth * warp.x * .9f, worldHeight * warp.y * .9f);
                float pitch = ArrivalPitch(t);
                var previousMatrix = GUI.matrix;
                var pivot = new Vector3(rect.center.x, rect.center.y, 0);
                GUI.matrix = previousMatrix * Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, pitch)) * Matrix4x4.Translate(-pivot);
                Effect(shadowRect, hull, portal, 5, t, 0, opacity * .65f);
                Effect(rect, hull, hullMaterial, 0, t, 0, opacity);
                Vector2 velocity = (ArrivalPosition(t + .01f) - ArrivalPosition(t - .01f)) / .02f;
                velocity = new Vector2(velocity.x * w, velocity.y * h);
                if (velocity.sqrMagnitude < .001f) velocity = Vector2.up;
                float radians = pitch * Mathf.Deg2Rad;
                velocity = new Vector2(Mathf.Cos(radians) * velocity.x + Mathf.Sin(radians) * velocity.y,
                    -Mathf.Sin(radians) * velocity.x + Mathf.Cos(radians) * velocity.y);
                // Shader UV Y runs upward: opposite screen-space motion is (-vx,+vy).
                Vector2 hullFlow = new Vector2(-velocity.x / Mathf.Max(1, rect.width), velocity.y / Mathf.Max(1, rect.height)).normalized;
                float plasma = appear * Mathf.Lerp(1, .55f, progress);
                float padX = rect.width * .15f, padY = rect.height * .15f;
                float tailX = hullFlow.x * PlasmaWakeLength * rect.width;
                float tailY = -hullFlow.y * PlasmaWakeLength * rect.height;
                var plasmaRect = Rect.MinMaxRect(rect.x - padX + Mathf.Min(0, tailX), rect.y - padY + Mathf.Min(0, tailY),
                    rect.xMax + padX + Mathf.Max(0, tailX), rect.yMax + padY + Mathf.Max(0, tailY));
                Vector2 flow = new Vector2(-velocity.x / Mathf.Max(1, plasmaRect.width), velocity.y / Mathf.Max(1, plasmaRect.height)).normalized;
                plasmaMaterial.SetVector("_MaskUV", new Vector4(plasmaRect.width / rect.width, plasmaRect.height / rect.height,
                    (plasmaRect.x - rect.x) / rect.width, (rect.yMax - plasmaRect.yMax) / rect.height));
                plasmaMaterial.SetFloat("_WakeLength", PlasmaWakeLength);
                plasmaMaterial.SetVector("_Flow", new Vector4(flow.x, flow.y, 1, plasma * opacity));
                plasmaMaterial.SetVector("_Clock", new Vector4(t, 17, 1f / hull.width, 3));
                Graphics.DrawTexture(plasmaRect, hull, plasmaMaterial);
                GUI.matrix = previousMatrix;
                // A second, independently moving cloud layer occludes the hull during descent.
                GUI.color = new Color(.8f, .88f, .95f, .2f * fog);
                Effect(full, BaseContent.WhiteTex, clouds, 1, t + 18, fog * .22f);
                GUI.color = Color.white;
            }
            Widgets.DrawBoxSolid(new Rect(-12, -12, w + 24, h * .065f + 12), new Color(0, 0, 0, .85f * Ease(t * 2)));
            Widgets.DrawBoxSolid(new Rect(-12, h * .935f, w + 24, h * .065f + 12), new Color(0, 0, 0, .85f * Ease(t * 2)));
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = White;
            string key = t < WarpStart ? "PMP.Vanguard.Acquiring" : progress < .8f ? "PMP.Vanguard.Arriving" : "PMP.Vanguard.Connecting";
            Widgets.Label(new Rect(0, h * .88f, w, 32), key.Translate());
            // Cover the close hull before switching to the independently sized sensor camera.
            float black = ArrivalBlack(t);
            if (black > 0) Widgets.DrawBoxSolid(new Rect(-32, -32, w + 64, h + 64), new Color(0, 0, 0, black));
        }

        private void DrawFireControl(OrbitalSupportSession ship, Rect full)
        {
            float w = full.width, h = full.height;
            float flash = ship.Map.GetComponent<VanguardImpactEffects>().ScreenFlash;
            if (flash > .002f) Widgets.DrawBoxSolid(full, new Color(1, .92f, .75f, flash));
            if (!ship.IsPrism)
            {
            // Preserve the asset aspect and bottom anchoring on ultrawide and 4:3 displays.
            float foreHeight = w * cannons.height / cannons.width;
            foreHeight = Mathf.Min(foreHeight, h);
            // Pad below and beside the viewport for five-degree banking; retain top alignment.
            float bankMargin = w * .055f + 10;
            float foreWidth = w * (1 + bankMargin / foreHeight);
            foreHeight += bankMargin;
            for (int side = 0; side < 2; side++)
            {
                float kick = CannonKick(side);
                var matrix = GUI.matrix;
                // Both barrels bank as one mount. Recoil stays independent and shot-timed.
                Vector3 pivot = new Vector3(w * .5f, h, 0);
                GUI.matrix = matrix * Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, cannonSway.x)) * Matrix4x4.Translate(-pivot);
                GUI.DrawTextureWithTexCoords(new Rect((w - foreWidth) * .5f + side * foreWidth / 2 + (side == 0 ? -1 : 1) * kick * 9,
                    h - foreHeight + bankMargin + kick * 25 + cannonSway.y, foreWidth / 2, foreHeight), cannons, new Rect(side * .5f, 0, .5f, 1), true);
                GUI.matrix = matrix;
            }
            }
            Text.Font = GameFont.Small; Text.Anchor = TextAnchor.MiddleLeft; GUI.color = White;
            Label(w * .065f, h * .79f, 260, 28, (ship.IsPrism ? "PMP.Prism.Name" : "PMP.Vanguard.Name").Translate());
            Label(w * .065f, h * .83f, 260, 28, ship.Unlimited ? "PMP.Prism.Unlimited".Translate() : "PMP.Vanguard.SupportRemaining".Translate(Mathf.CeilToInt(ship.RemainingLifeTicks / 60f)));
            hud.Bar(new Rect(w * .065f, h * .875f, w * .2f, 4), ship.RemainingFraction, White);

            hud.Weapons(ship, full);
            Text.Anchor = TextAnchor.MiddleCenter;
            Label(w * .12f, h - 42, w * .76f, 28, (ship.IsPrism ? "PMP.Prism.Controls" : "PMP.Vanguard.Controls").Translate());
            Label(w * .33f, 92, w * .34f, 24, (ship.Infrared ? "PMP.Vanguard.Infrared" : "PMP.Vanguard.Optical").Translate());
            hud.Compass(w, ship.Bearing);

            // Mark visible spawned pawns without changing targeting.
            foreach (var pawn in ship.Map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || pawn.Position.Fogged(ship.Map) || pawn.IsHiddenFromPlayer()) continue;
                if (pawn.Faction != Faction.OfPlayer && !pawn.HostileTo(Faction.OfPlayer)) continue;
                Vector2 p = UI.MapToUIPosition(pawn.DrawPos);
                if (p.x < 18 || p.x > w - 18 || p.y < 65 || p.y > h - 55) continue;
                Brackets(p, pawn.Faction == Faction.OfPlayer ? new Color(.4f, .78f, .85f, .7f) : new Color(1, .32f, .28f, .85f));
            }
            Vector2 aim = UI.MousePositionOnUIInverted;
            if (aim.x < 0 || aim.x > w || aim.y < 0 || aim.y > h) aim = new Vector2(w / 2, h / 2);
            hud.Reticle(aim, MissileFlash > 0);
            GUI.color = White;
            Widgets.DrawBoxSolid(PauseRect, new Color(.025f, .035f, .045f, .7f));
            Widgets.Label(PauseRect, (Find.TickManager.Paused ? "PMP.Vanguard.Resume" : "PMP.Vanguard.Pause").Translate());
            if (Widgets.ButtonInvisible(PauseRect))
                VanguardFireControl.TogglePause();
        }
        private void DrawThermalTargets(OrbitalSupportSession ship)
        {
            foreach (var pawn in ship.Map.mapPawns.AllPawnsSpawned)
            {
                if (pawn.Dead || !Find.CameraDriver.CurrentViewRect.Contains(pawn.Position)) continue;
                var body = TargetPlasmaRenderer.VisibleCapture(pawn);
                if (body == null) continue;
                var center = pawn.DrawPos;
                var a = UI.MapToUIPosition(center + new Vector3(-body.HalfSize, 0, body.HalfSize));
                var b = UI.MapToUIPosition(center + new Vector3(body.HalfSize, 0, -body.HalfSize));
                Effect(new Rect(a.x, a.y, b.x - a.x, b.y - a.y), body.Texture, thermalBody, 6, 0, 1);
            }
        }
        private static Vector2 Circle(float a) => new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        private static void Label(float x, float y, float w, float h, string text)
        {
            Color color = GUI.color;
            GUI.color = new Color(0, 0, 0, .8f); Widgets.Label(new Rect(x + 1, y + 1, w, h), text);
            GUI.color = color; Widgets.Label(new Rect(x, y, w, h), text);
        }
        private static void Line(Vector2 a, Vector2 b, Color color, float width) => Widgets.DrawLine(a, b, color, width);
        private static void Bar(Rect rect, float value, Color color)
        {
            Widgets.DrawBoxSolid(rect, new Color(.5f, .55f, .6f, .25f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value), rect.height), color);
        }
        private static void Missile(Vector2 p, Color color)
        {
            Line(p + new Vector2(0, -20), p + new Vector2(-3, -13), color, 1);
            Line(p + new Vector2(0, -20), p + new Vector2(3, -13), color, 1);
            Line(p + new Vector2(-3, -13), p + new Vector2(-3, 12), color, 1);
            Line(p + new Vector2(3, -13), p + new Vector2(3, 12), color, 1);
            Line(p + new Vector2(-3, 0), p + new Vector2(-8, 14), color, 1);
            Line(p + new Vector2(3, 0), p + new Vector2(8, 14), color, 1);
            Line(p + new Vector2(-8, 14), p + new Vector2(8, 14), color, 1);
        }
        private static void Brackets(Vector2 p, Color color)
        {
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 })
            {
                Vector2 c = p + new Vector2(x * 10, y * 13);
                Line(c, c - new Vector2(x * 5, 0), color, 1);
                Line(c, c - new Vector2(0, y * 5), color, 1);
            }
        }
        public void Dispose()
        {
            hud.Dispose();
            if (feed != null) RenderTexture.ReleaseTemporary(feed); feed = null;
            foreach (var mat in new[] { hullMaterial, clouds, portal, warpRibbon, sensor, plasmaMaterial, thermalBody })
                if (mat != null) UnityEngine.Object.Destroy(mat);
            hullMaterial = clouds = portal = warpRibbon = sensor = plasmaMaterial = thermalBody = null;
        }
    }
}
