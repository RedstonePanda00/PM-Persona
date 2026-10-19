using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PMP_Persona.EnergyFieldSupport
{
    public class GameComponent_EnergyFieldManager : GameComponent
    {
        public GameComponent_EnergyFieldManager(Game game)
        {
        }

        private List<int> factionLoadIds = new List<int>();
        private List<int> fieldMapUniqueIds = new List<int>();
        private List<IntVec3> fieldCenters = new List<IntVec3>();

        private readonly Dictionary<int, HashSet<int>> factionToPawnIds = new Dictionary<int, HashSet<int>>();
        private int lastPruneTick = -1;

        private List<int> pendingPawnThingIds = new List<int>();
        private List<int> pendingMapUniqueIds = new List<int>();
        private List<IntVec3> pendingDestCells = new List<IntVec3>();
        private List<int> pendingDueTicks = new List<int>();

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            ProcessPendingTeleports();
            if (Find.TickManager.TicksGame - lastPruneTick < 240)
                return;
            lastPruneTick = Find.TickManager.TicksGame;
            PruneAndRefreshTrackedPawns();
        }

        public void ScheduleTeleport(Pawn pawn, Faction faction, Map map, IntVec3 dest, int delayTicks)
        {
            if (pawn == null || map == null || !dest.IsValid)
                return;
            int due = Find.TickManager.TicksGame + delayTicks;
            pendingPawnThingIds.Add(pawn.thingIDNumber);
            pendingMapUniqueIds.Add(map.uniqueID);
            pendingDestCells.Add(dest);
            pendingDueTicks.Add(due);
        }

        public void SetEnergyField(Faction faction, Map map, IntVec3 center)
        {
            if (faction == null || map == null)
                return;
            int idx = IndexOfFaction(faction.loadID);
            if (idx >= 0)
            {
                fieldMapUniqueIds[idx] = map.uniqueID;
                fieldCenters[idx] = center;
            }
            else
            {
                factionLoadIds.Add(faction.loadID);
                fieldMapUniqueIds.Add(map.uniqueID);
                fieldCenters.Add(center);
            }
            RescanFriendliesIntoSet(faction);
        }

        public bool RemoveEnergyField(Faction faction)
        {
            if (faction == null)
                return false;
            int idx = IndexOfFaction(faction.loadID);
            if (idx < 0)
                return false;

            factionLoadIds.RemoveAt(idx);
            fieldMapUniqueIds.RemoveAt(idx);
            fieldCenters.RemoveAt(idx);
            if (factionToPawnIds.TryGetValue(faction.loadID, out HashSet<int> tracked))
                tracked.Clear();
            factionToPawnIds.Remove(faction.loadID);
            return true;
        }

        public bool TryGetEnergyField(Faction faction, out Map map, out IntVec3 center)
        {
            map = null;
            center = IntVec3.Invalid;
            if (faction == null)
                return false;
            int idx = IndexOfFaction(faction.loadID);
            if (idx < 0)
                return false;
            map = FindMapByUniqueId(fieldMapUniqueIds[idx]);
            center = fieldCenters[idx];
            return map != null && center.InBounds(map);
        }

        public bool CellCoveredByFactionField(Map map, IntVec3 cell, Faction faction)
        {
            if (faction == null || map == null || !cell.InBounds(map))
                return false;
            if (!TryGetEnergyField(faction, out Map fieldMap, out IntVec3 center))
                return false;
            if (fieldMap != map)
                return false;
            return EnergyFieldPlacementUtility.IsCellInEnergyFieldMask(map, center, cell);
        }

        public List<Pawn> SnapshotFriendliesInField(Faction faction)
        {
            List<Pawn> list = new List<Pawn>();
            if (faction == null)
                return list;
            if (!TryGetEnergyField(faction, out Map map, out IntVec3 center))
                return list;
            float r = EnergyFieldConstants.FieldRadiusCells;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Faction != faction)
                    continue;
                if (EnergyFieldCellMembership.IsValidFieldCell(map, center, pawn.Position, r))
                    list.Add(pawn);
            }
            return list;
        }

        public bool IsPawnTrackedForEnergyField(Pawn pawn)
        {
            if (pawn == null || pawn.Faction == null)
                return false;
            if (!factionToPawnIds.TryGetValue(pawn.Faction.loadID, out HashSet<int> set))
                return false;
            return set.Contains(pawn.thingIDNumber);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref factionLoadIds, "efm_factionLoadIds", LookMode.Value);
            Scribe_Collections.Look(ref fieldMapUniqueIds, "efm_fieldMapUniqueIds", LookMode.Value);
            Scribe_Collections.Look(ref fieldCenters, "efm_fieldCenters", LookMode.Value);
            Scribe_Collections.Look(ref pendingPawnThingIds, "efm_pendPawnIds", LookMode.Value);
            Scribe_Collections.Look(ref pendingMapUniqueIds, "efm_pendMapUids", LookMode.Value);
            Scribe_Collections.Look(ref pendingDestCells, "efm_pendDests", LookMode.Value);
            Scribe_Collections.Look(ref pendingDueTicks, "efm_pendDueTicks", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (factionLoadIds == null)
                    factionLoadIds = new List<int>();
                if (fieldMapUniqueIds == null)
                    fieldMapUniqueIds = new List<int>();
                if (fieldCenters == null)
                    fieldCenters = new List<IntVec3>();
                if (pendingPawnThingIds == null)
                    pendingPawnThingIds = new List<int>();
                if (pendingMapUniqueIds == null)
                    pendingMapUniqueIds = new List<int>();
                if (pendingDestCells == null)
                    pendingDestCells = new List<IntVec3>();
                if (pendingDueTicks == null)
                    pendingDueTicks = new List<int>();
                NormalizeFactionLists();
                NormalizePendingLists();
            }
        }

        private void NormalizeFactionLists()
        {
            int n = factionLoadIds.Count;
            if (fieldMapUniqueIds.Count > n)
                fieldMapUniqueIds.RemoveRange(n, fieldMapUniqueIds.Count - n);
            while (fieldMapUniqueIds.Count < n)
                fieldMapUniqueIds.Add(-1);
            if (fieldCenters.Count > n)
                fieldCenters.RemoveRange(n, fieldCenters.Count - n);
            while (fieldCenters.Count < n)
                fieldCenters.Add(IntVec3.Invalid);
        }

        private void NormalizePendingLists()
        {
            int n = Math.Min(pendingPawnThingIds.Count, pendingMapUniqueIds.Count);
            n = Math.Min(n, pendingDestCells.Count);
            n = Math.Min(n, pendingDueTicks.Count);
            if (pendingPawnThingIds.Count > n)
                pendingPawnThingIds.RemoveRange(n, pendingPawnThingIds.Count - n);
            if (pendingMapUniqueIds.Count > n)
                pendingMapUniqueIds.RemoveRange(n, pendingMapUniqueIds.Count - n);
            if (pendingDestCells.Count > n)
                pendingDestCells.RemoveRange(n, pendingDestCells.Count - n);
            if (pendingDueTicks.Count > n)
                pendingDueTicks.RemoveRange(n, pendingDueTicks.Count - n);
        }

        private void ProcessPendingTeleports()
        {
            int now = Find.TickManager.TicksGame;
            for (int i = pendingPawnThingIds.Count - 1; i >= 0; i--)
            {
                if (now < pendingDueTicks[i])
                    continue;
                TryExecuteTeleportAtIndex(i);
                RemovePendingAt(i);
            }
        }

        private void TryExecuteTeleportAtIndex(int i)
        {
            int pawnId = pendingPawnThingIds[i];
            Map map = FindMapByUniqueId(pendingMapUniqueIds[i]);
            IntVec3 dest = pendingDestCells[i];
            if (map == null || !dest.InBounds(map))
                return;
            Pawn pawn = FindPawnByThingIdAnyMap(pawnId);
            if (pawn == null || pawn.Destroyed || pawn.Dead || !pawn.Spawned)
                return;
            if (!dest.Standable(map))
            {
                dest = CellFinder.StandableCellNear(dest, map, 48);
                if (!dest.IsValid || !dest.InBounds(map))
                    return;
            }
            if (pawn.Map != map)
            {
                pawn.DeSpawn(DestroyMode.Vanish);
                GenSpawn.Spawn(pawn, dest, map, pawn.Rotation, WipeMode.Vanish, false);
                pawn.Notify_Teleported();
                return;
            }
            pawn.pather?.StopDead();
            pawn.Position = dest;
            pawn.Notify_Teleported();
        }

        private static Pawn FindPawnByThingIdAnyMap(int thingId)
        {
            if (Find.Maps == null)
                return null;
            for (int mi = 0; mi < Find.Maps.Count; mi++)
            {
                Map m = Find.Maps[mi];
                if (m == null)
                    continue;
                foreach (Pawn p in m.mapPawns.AllPawnsSpawned)
                {
                    if (p != null && p.thingIDNumber == thingId)
                        return p;
                }
            }
            return null;
        }

        private void RemovePendingAt(int index)
        {
            pendingPawnThingIds.RemoveAt(index);
            pendingMapUniqueIds.RemoveAt(index);
            pendingDestCells.RemoveAt(index);
            pendingDueTicks.RemoveAt(index);
        }

        private int IndexOfFaction(int loadId)
        {
            for (int i = 0; i < factionLoadIds.Count; i++)
            {
                if (factionLoadIds[i] == loadId)
                    return i;
            }
            return -1;
        }

        private static Map FindMapByUniqueId(int uniqueId)
        {
            if (Find.Maps == null)
                return null;
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map m = Find.Maps[i];
                if (m != null && m.uniqueID == uniqueId)
                    return m;
            }
            return null;
        }

        private void RescanFriendliesIntoSet(Faction faction)
        {
            if (faction == null)
                return;
            if (!TryGetEnergyField(faction, out Map map, out IntVec3 center))
                return;
            HashSet<int> set = GetOrCreatePawnSet(faction.loadID);
            set.Clear();
            float r = EnergyFieldConstants.FieldRadiusCells;
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || !pawn.Spawned || pawn.Dead || pawn.Faction != faction)
                    continue;
                if (EnergyFieldCellMembership.IsValidFieldCell(map, center, pawn.Position, r))
                    set.Add(pawn.thingIDNumber);
            }
        }

        private void PruneAndRefreshTrackedPawns()
        {
            for (int i = 0; i < factionLoadIds.Count; i++)
            {
                int loadId = factionLoadIds[i];
                Faction fac = Find.FactionManager.AllFactions.FirstOrDefault(f => f.loadID == loadId);
                if (fac == null)
                {
                    if (factionToPawnIds.TryGetValue(loadId, out HashSet<int> dead))
                        dead.Clear();
                    continue;
                }
                RescanFriendliesIntoSet(fac);
            }
        }

        private HashSet<int> GetOrCreatePawnSet(int factionLoadId)
        {
            if (!factionToPawnIds.TryGetValue(factionLoadId, out HashSet<int> set))
            {
                set = new HashSet<int>();
                factionToPawnIds[factionLoadId] = set;
            }
            return set;
        }
    }
}
