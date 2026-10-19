using UnityEngine;
using Verse;

namespace PMP_Persona
{
    // Left 1 : right 3 layout; left = section tabs, right = section content.
    public static class PMP_PersonaModSettingsUI
    {
        private const float OuterPadding = 12f;
        private const float ColumnGap = 8f;
        private const float LeftColumnFraction = 0.25f;

        public static void Draw(Rect inRect, PMP_PersonaModSettings settings)
        {
            if (settings == null)
                return;

            Rect inner = inRect.ContractedBy(OuterPadding);
            float leftW = inner.width * LeftColumnFraction;
            Rect leftRect = new Rect(inner.x, inner.y, leftW - ColumnGap * 0.5f, inner.height);
            Rect rightRect = new Rect(inner.x + leftW + ColumnGap * 0.5f, inner.y, inner.width - leftW - ColumnGap * 0.5f, inner.height);

            Widgets.DrawBox(leftRect);
            Widgets.DrawBox(rightRect);

            DrawLeftColumn(leftRect.ContractedBy(8f), settings);
            DrawRightColumn(rightRect.ContractedBy(8f), settings);
        }

        private static void DrawLeftColumn(Rect rect, PMP_PersonaModSettings settings)
        {
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 22f), (string)"PMP.Settings.SectionsTitle".Translate());
            float y = rect.y + 26f;
            Rect row = new Rect(rect.x, y, rect.width, 28f);
            bool sel = settings.activeSettingsTab == PMP_PersonaModSettings.TabAirPatrol;
            if (sel)
                Widgets.DrawOptionBackground(row, true);
            if (Widgets.ButtonText(row, (string)"PMP.Settings.Tab.AirPatrol".Translate()))
            {
                settings.activeSettingsTab = PMP_PersonaModSettings.TabAirPatrol;
                settings.Write();
            }
        }

        private static void DrawRightColumn(Rect rect, PMP_PersonaModSettings settings)
        {
            if (settings.activeSettingsTab == PMP_PersonaModSettings.TabAirPatrol)
            {
                AirPatrolLegion.AirPatrolExtraTargetsSettingsUI.Draw(rect, settings);
                return;
            }

            Widgets.Label(rect, "PMP.Settings.NoSectionContent".Translate());
        }
    }
}
