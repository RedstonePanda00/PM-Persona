using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PMP_Persona
{
    public static class SpawnedPawnCountCache
    {
        private static readonly HashSet<string> RegisteredDefNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<Map, Dictionary<string, int>> Cache = new Dictionary<Map, Dictionary<string, int>>();
        private static int _lastRefreshTick = -1;
        private const int CacheRefreshInterval = 250;

        public static void ForceRefreshOnNextCheck()
        {
            _lastRefreshTick = -1;
        }

        public static void RegisterDefName(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return;
            if (RegisteredDefNames.Add(defName))
                _lastRefreshTick = -1; // force refresh when new def registered
        }

        // Total spawned count for def on map (any faction).
        public static int GetCount(Map map, string defName)
        {
            if (map == null || string.IsNullOrEmpty(defName)) return 0;
            RegisterDefName(defName);
            RefreshIfNeeded();
            if (Cache.TryGetValue(map, out var counts) && counts.TryGetValue(defName, out int c))
                return c;
            return 0;
        }

        // Spawned count for def in given faction; 0 if faction null. Used for backup-body counts.
        public static int GetCountInFaction(Map map, string defName, Faction faction)
        {
            if (map == null || string.IsNullOrEmpty(defName) || faction == null) return 0;
            RegisterDefName(defName);
            RefreshIfNeeded();
            string factionKey = FactionCacheKey(defName, faction);
            if (Cache.TryGetValue(map, out var counts) && counts.TryGetValue(factionKey, out int c))
                return c;
            return 0;
        }

        private static string FactionCacheKey(string defName, Faction faction)
        {
            return defName + "|" + (faction?.GetUniqueLoadID() ?? "n");
        }

        private static void RefreshIfNeeded()
        {
            if (Current.Game == null || Find.TickManager == null) return;
            int tick = Find.TickManager.TicksGame;
            if (_lastRefreshTick >= 0 && tick - _lastRefreshTick < CacheRefreshInterval) return;
            _lastRefreshTick = tick;
            Refresh();
        }

        private static void Refresh()
        {
            Cache.Clear();
            if (Find.Maps == null) return;
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (string defName in RegisteredDefNames)
                    counts[defName] = 0;
                foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
                {
                    string defName = p.def?.defName;
                    if (string.IsNullOrEmpty(defName) || !RegisteredDefNames.Contains(defName)) continue;
                    counts[defName] = counts[defName] + 1;
                    string factionKey = FactionCacheKey(defName, p.Faction);
                    if (!counts.ContainsKey(factionKey))
                        counts[factionKey] = 0;
                    counts[factionKey] = counts[factionKey] + 1;
                }
                Cache[map] = counts;
            }
        }
    }
}
