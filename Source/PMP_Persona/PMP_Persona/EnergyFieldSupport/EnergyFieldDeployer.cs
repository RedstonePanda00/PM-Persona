using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.EnergyFieldSupport
{
    public static class EnergyFieldDeployer
    {
        private static FleckDef CachedStrikeFleck;

        public static bool TryDeployAt(Map map, IntVec3 desiredCell, Faction faction)
        {
            if (map == null || faction == null)
                return false;
            if (!desiredCell.InBounds(map))
                return false;
            GameComponent_EnergyFieldManager mgr = Current.Game?.GetComponent<GameComponent_EnergyFieldManager>();
            if (mgr == null)
                return false;

            bool had = mgr.TryGetEnergyField(faction, out _, out IntVec3 oldCenter);
            List<Pawn> toMove = had ? mgr.SnapshotFriendliesInField(faction) : new List<Pawn>();
            IntVec3 oldAnchorForOffset = had ? oldCenter : desiredCell;

            mgr.SetEnergyField(faction, map, desiredCell);

            UnfogDisk(map, desiredCell);
            HashSet<IntVec3> newMask = EnergyFieldPlacementUtility.BuildValidFieldCellSet(desiredCell, map);

            FleckDef strike = ResolveStrikeFleck();
            for (int i = 0; i < toMove.Count; i++)
            {
                Pawn p = toMove[i];
                if (p == null || p.Destroyed || p.Dead)
                    continue;
                IntVec3 dest = EnergyFieldPlacementUtility.ResolveDestinationCell(
                    map,
                    desiredCell,
                    oldAnchorForOffset,
                    p.Position,
                    newMask);
                if (strike != null)
                {
                    // Energy-field deploy strike only: world Z +4 for PM_WarpInStrike visual.
                    Vector3 fleckPos = dest.ToVector3Shifted();
                    fleckPos.z += 6f;
                    FleckMaker.Static(fleckPos, map, strike, 1.2f);
                }

                mgr.ScheduleTeleport(p, faction, map, dest, EnergyFieldConstants.TeleportAfterStrikeTicks);
            }

            return true;
        }

        private static void UnfogDisk(Map map, IntVec3 center)
        {
            if (map?.fogGrid == null)
                return;
            foreach (IntVec3 c in EnergyFieldPlacementUtility.GeometricDiskCells(center, map))
            {
                if (!c.InBounds(map))
                    continue;
                map.fogGrid.Unfog(c);
            }
        }

        private static FleckDef ResolveStrikeFleck()
        {
            if (CachedStrikeFleck == null)
                CachedStrikeFleck = DefDatabase<FleckDef>.GetNamedSilentFail("PM_WarpInStrike");
            return CachedStrikeFleck;
        }
    }
}
