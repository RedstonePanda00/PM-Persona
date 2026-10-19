using System.Collections.Generic;
using PMP_Persona;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.AirPatrolLegion
{
    // ThingDef search + package filter + virtualized checkbox list (defName + label).
    public static class AirPatrolExtraTargetsSettingsUI
    {
        private const float RowHeight = 26f;
        private const float PackageButtonHeight = 28f;
        private static List<ThingDef> allThingDefsCache;
        private static List<PackageEntry> packageEntries;
        private static List<ThingDef> filteredCache = new List<ThingDef>();
        private static string lastBuiltFilterKey;
        private static Vector2 scrollPos;
        private static readonly QuickSearchWidget QuickSearch = new QuickSearchWidget();

        private struct PackageEntry
        {
            public string PackageId;
            public string DisplayLabel;
        }

        public static void Draw(Rect inRect, PMP_PersonaModSettings settings)
        {
            if (settings == null)
                return;

            EnsureThingDefCache();
            if (allThingDefsCache == null || allThingDefsCache.Count == 0)
            {
                Widgets.Label(inRect, "PMP.AirPatrolSettings.NoDefs".Translate());
                return;
            }

            float y = inRect.y;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), "PMP.AirPatrolSettings.Title".Translate(allThingDefsCache.Count));
            y += 24f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 22f), "PMP.AirPatrolSettings.SearchHint".Translate());
            y += 24f;

            Rect searchRect = new Rect(inRect.x, y, inRect.width, QuickSearchWidget.WidgetHeight);
            QuickSearch.OnGUI(searchRect, () => { scrollPos = Vector2.zero; });
            y += QuickSearchWidget.WidgetHeight + 6f;

            Rect packageRect = new Rect(inRect.x, y, inRect.width, PackageButtonHeight);
            string packageBtn = settings.AirPatrolFilterAllPackages
                ? (string)"PMP.AirPatrolSettings.AllPackages".Translate()
                : FindPackageLabel(settings.airPatrolPackageFilterPackageId);
            if (Widgets.ButtonText(packageRect, packageBtn))
                OpenPackageFloatMenu(settings);
            y += PackageButtonHeight + 6f;

            float listHeight = Mathf.Max(120f, inRect.yMax - y);
            Rect listOuter = new Rect(inRect.x, y, inRect.width, listHeight);

            RebuildFiltered(settings);
            ClampScroll(listOuter, filteredCache.Count);

            QuickSearch.noResultsMatched = QuickSearch.filter.Active && filteredCache.Count == 0;

            float viewW = Mathf.Max(1f, listOuter.width - 20f);
            float contentH = Mathf.Max(1f, filteredCache.Count * RowHeight);
            Rect viewRect = new Rect(0f, 0f, viewW, contentH);

            int firstVisible = Mathf.FloorToInt(scrollPos.y / RowHeight);
            int visibleCount = Mathf.CeilToInt(listOuter.height / RowHeight) + 2;
            int i0 = Mathf.Clamp(firstVisible, 0, Mathf.Max(0, filteredCache.Count - 1));
            int i1 = Mathf.Min(i0 + visibleCount, filteredCache.Count);

            Widgets.BeginScrollView(listOuter, ref scrollPos, viewRect);
            for (int i = i0; i < i1; i++)
            {
                if (i < 0 || i >= filteredCache.Count)
                    continue;
                ThingDef td = filteredCache[i];
                if (td == null)
                    continue;

                Rect rowRect = new Rect(0f, i * RowHeight, viewW, RowHeight);
                bool sel = settings.IsExtraTargetSelected(td.defName);
                bool newVal = sel;
                string label = $"{td.defName}  —  {GetDisplayLabel(td)}";
                if (label.Length > 120)
                    label = label.Substring(0, 117) + "...";
                Widgets.CheckboxLabeled(rowRect, label, ref newVal);
                if (newVal != sel)
                    settings.SetExtraTargetSelected(td.defName, newVal);
            }

            Widgets.EndScrollView();
        }

        private static string FindPackageLabel(string packageId)
        {
            if (packageEntries == null)
                return packageId;
            for (int i = 0; i < packageEntries.Count; i++)
            {
                if (packageEntries[i].PackageId == packageId)
                    return packageEntries[i].DisplayLabel;
            }

            return packageId;
        }

        private static void OpenPackageFloatMenu(PMP_PersonaModSettings settings)
        {
            List<FloatMenuOption> opts = new List<FloatMenuOption>
            {
                new FloatMenuOption((string)"PMP.AirPatrolSettings.AllPackages".Translate(), () =>
                {
                    settings.SetAirPatrolPackageFilter("");
                    lastBuiltFilterKey = null;
                    scrollPos = Vector2.zero;
                })
            };
            if (packageEntries != null)
            {
                for (int i = 0; i < packageEntries.Count; i++)
                {
                    PackageEntry pe = packageEntries[i];
                    opts.Add(new FloatMenuOption(pe.DisplayLabel, () =>
                    {
                        settings.SetAirPatrolPackageFilter(pe.PackageId);
                        lastBuiltFilterKey = null;
                        scrollPos = Vector2.zero;
                    }));
                }
            }

            Find.WindowStack.Add(new FloatMenu(opts));
        }

        private static void EnsureThingDefCache()
        {
            if (allThingDefsCache != null)
                return;
            allThingDefsCache = new List<ThingDef>(DefDatabase<ThingDef>.AllDefsListForReading);
            allThingDefsCache.Sort((a, b) => string.CompareOrdinal(a?.defName ?? "", b?.defName ?? ""));

            HashSet<string> seen = new HashSet<string>();
            packageEntries = new List<PackageEntry>();
            for (int i = 0; i < allThingDefsCache.Count; i++)
            {
                ThingDef td = allThingDefsCache[i];
                if (td == null)
                    continue;
                string id = string.IsNullOrEmpty(td.modContentPack?.PackageId) ? "undef" : td.modContentPack.PackageId;
                if (!seen.Add(id))
                    continue;
                string modName = td.modContentPack?.Name ?? id;
                packageEntries.Add(new PackageEntry
                {
                    PackageId = id,
                    DisplayLabel = $"{modName} ({id})"
                });
            }

            packageEntries.Sort((a, b) => string.CompareOrdinal(a.DisplayLabel, b.DisplayLabel));
        }

        private static void RebuildFiltered(PMP_PersonaModSettings settings)
        {
            string searchText = QuickSearch.filter?.Text ?? "";
            string pkg = settings.airPatrolPackageFilterPackageId ?? "";
            string key = pkg + "\u001f" + searchText;
            if (key == lastBuiltFilterKey)
                return;
            scrollPos = Vector2.zero;
            lastBuiltFilterKey = key;
            filteredCache.Clear();
            string q = searchText.Trim().ToLowerInvariant();
            for (int i = 0; i < allThingDefsCache.Count; i++)
            {
                ThingDef td = allThingDefsCache[i];
                if (td == null)
                    continue;
                if (!MatchesPackageFilter(td, settings))
                    continue;
                if (!MatchesSearch(td, q))
                    continue;
                filteredCache.Add(td);
            }
        }

        private static void ClampScroll(Rect listOuter, int count)
        {
            float contentH = Mathf.Max(1f, count * RowHeight);
            float maxScroll = Mathf.Max(0f, contentH - listOuter.height);
            scrollPos.y = Mathf.Clamp(scrollPos.y, 0f, maxScroll);
        }

        private static bool MatchesPackageFilter(ThingDef td, PMP_PersonaModSettings settings)
        {
            if (settings.AirPatrolFilterAllPackages)
                return true;
            string id = string.IsNullOrEmpty(td.modContentPack?.PackageId) ? "undef" : td.modContentPack.PackageId;
            return id == settings.airPatrolPackageFilterPackageId;
        }

        private static bool MatchesSearch(ThingDef td, string q)
        {
            if (q.Length == 0)
                return true;
            if (td.defName != null && td.defName.ToLowerInvariant().Contains(q))
                return true;
            try
            {
                if (td.LabelCap.ToString().ToLowerInvariant().Contains(q))
                    return true;
            }
            catch
            {
                // Rare bad defs; still allow defName / label match below.
            }

            if (!string.IsNullOrEmpty(td.label) && td.label.ToLowerInvariant().Contains(q))
                return true;
            return false;
        }

        private static string GetDisplayLabel(ThingDef td)
        {
            if (td == null)
                return "";
            try
            {
                string cap = td.LabelCap.ToString().Trim();
                if (!string.IsNullOrEmpty(cap))
                    return cap;
            }
            catch
            {
                // ignore
            }

            if (!string.IsNullOrEmpty(td.label))
                return td.label;
            return td.defName ?? "";
        }
    }
}
