using System;
using UnityEngine;
using Verse;

namespace PMP_Persona.VanguardSupport
{
    internal sealed class OrbitalHudRenderer : IDisposable
    {
        private Material material;
        private readonly Color solar = new Color(1, .78f, .16f);
        private void Draw(Rect rect, int mode, float amount, float bearing, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (material == null) material = PersonaMaterials.Create("PMP17_OrbitalHudMaterial");
            material.SetVector("_State", new Vector4(Time.realtimeSinceStartup, mode, amount, bearing));
            material.SetVector("_Layout", new Vector4(rect.width, rect.height, amount, 0));
            material.SetVector("_Tint", new Vector4(color.r, color.g, color.b, color.a));
            Graphics.DrawTexture(rect, BaseContent.WhiteTex, material);
        }
        internal void Reticle(Vector2 at, bool flash) => Draw(new Rect(at.x - 160, at.y - 62, 320, 124), 2, 1, 0,
            flash ? solar : new Color(.87f, .92f, .94f));
        internal void Bar(Rect rect, float fraction, Color color) => Draw(rect, 3, fraction, 0, color);
        internal void Compass(float width, float bearing)
        {
            float span = Mathf.Min(480, width * .45f);
            var rect = new Rect((width - span) * .5f, 30, span, 34);
            Draw(rect, 1, 0, bearing, new Color(.87f, .92f, .94f));
            var saved = Text.Anchor; Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(width * .5f - 50, 6, 100, 25), Mathf.RoundToInt(bearing % 360).ToString("000") + "°");
            int first = Mathf.FloorToInt((bearing - span / 8) / 15) * 15;
            for (int angle = first; angle <= bearing + span / 8; angle += 15)
            {
                float x = width * .5f + (angle - bearing) * 4;
                if (x < rect.x + 20 || x > rect.xMax - 20) continue;
                int wrapped = (angle % 360 + 360) % 360;
                string label = wrapped % 90 == 0 ? new[] { "N", "E", "S", "W" }[wrapped / 90] : wrapped.ToString("000");
                Widgets.Label(new Rect(x - 22, 62, 44, 24), label);
            }
            Text.Anchor = saved;
        }
        internal void Weapons(OrbitalSupportSession ship, Rect full)
        {
            float width = Mathf.Clamp(full.width * .235f, 230, 360), height = ship.IsPrism ? 88 : 120;
            int count = ship.IsPrism ? 3 : 1;
            float x = full.width - width - 24, top = full.height * .38f;
            for (int i = 0; i < count; i++)
            {
                int weapon = i + 1;
                Rect card = new Rect(x, top + i * (height + 12), width, height);
                bool selected = !ship.IsPrism || ship.SelectedWeapon == weapon;
                Draw(card, 0, selected ? 1 : 0, 0, solar);
                Text.Anchor = TextAnchor.MiddleLeft;
                string label = ship.IsPrism ? ship.Config.weapons[i].labelKey.Translate() : "PMP.Vanguard.Missiles".Translate();
                var savedFont = Text.Font;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(x + 18, card.y + 3, width - 110, 36), label);
                Text.Font = savedFont;
                Text.Anchor = TextAnchor.MiddleRight;
                string ammo = ship.IsPrism ? ship.Ammo(weapon).ToString("00") + " / " + ship.Capacity(weapon).ToString("00")
                    : (ship.CooldownTicks(2) == 0 ? "05 / 05" : "00 / 05");
                Widgets.Label(new Rect(card.xMax - 96, card.y + 7, 80, 32), ammo);
                Text.Anchor = TextAnchor.MiddleLeft;
                int cooldown = ship.CooldownTicks(ship.IsPrism ? weapon : 2);
                if (!ship.IsPrism) Draw(new Rect(x + 18, card.y + 37, width - 36, 30), 4, cooldown == 0 ? 1 : 0, 0, solar);
                string status = cooldown > 0 ? "PMP.Prism.Reload".Translate((cooldown / 60f).ToString("F1")) : "PMP.Vanguard.Ready".Translate();
                Widgets.Label(new Rect(x + 18, card.y + (ship.IsPrism ? 38 : 73), width - 36, 24), status);
                float fraction = 1 - ship.CooldownFraction(ship.IsPrism ? weapon : 2);
                Bar(new Rect(x + 18, card.yMax - 15, width - 36, 4), fraction, solar);
            }
        }
        public void Dispose() { if (material != null) UnityEngine.Object.Destroy(material); material = null; }
    }
}
