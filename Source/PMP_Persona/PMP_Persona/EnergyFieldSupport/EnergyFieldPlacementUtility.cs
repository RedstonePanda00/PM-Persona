using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PMP_Persona.EnergyFieldSupport
{
    public static class EnergyFieldPlacementUtility
    {
        public static float FieldRadiusCells => EnergyFieldConstants.FieldRadiusCells;

        // Single source for warp / teleport / pawn membership: geometric disk minus blocked.
        public static bool IsCellInEnergyFieldMask(Map map, IntVec3 center, IntVec3 cell, float radiusCells)
        {
            return EnergyFieldCellMembership.IsValidFieldCell(map, center, cell, radiusCells);
        }

        public static bool IsCellInEnergyFieldMask(Map map, IntVec3 center, IntVec3 cell)
        {
            return IsCellInEnergyFieldMask(map, center, cell, FieldRadiusCells);
        }

        // Full geometric disk (for unfog); includes blocked cells.
        public static IEnumerable<IntVec3> GeometricDiskCells(IntVec3 center, Map map)
        {
            float r = FieldRadiusCells;
            int ri = (int)System.Math.Ceiling(r);
            CellRect bounds = CellRect.WholeMap(map);
            foreach (IntVec3 c in CellRect.CenteredOn(center, ri))
            {
                if (!bounds.Contains(c))
                    continue;
                if (EnergyFieldCellMembership.InGeometricDisk(center, c, r))
                    yield return c;
            }
        }

        public static HashSet<IntVec3> BuildValidFieldCellSet(IntVec3 center, Map map)
        {
            HashSet<IntVec3> set = new HashSet<IntVec3>();
            float r = FieldRadiusCells;
            foreach (IntVec3 c in GeometricDiskCells(center, map))
            {
                if (EnergyFieldCellMembership.IsValidFieldCell(map, center, c, r))
                    set.Add(c);
            }
            return set;
        }

        public static IntVec3 ResolveDestinationCell(
            Map destMap,
            IntVec3 newCenter,
            IntVec3 oldCenter,
            IntVec3 pawnCellOnOldMap,
            HashSet<IntVec3> newValidFieldCells)
        {
            float r = FieldRadiusCells;
            IntVec3 desired = newCenter + (pawnCellOnOldMap - oldCenter);
            if (desired.InBounds(destMap) && desired.Standable(destMap) && IsCellInEnergyFieldMask(destMap, newCenter, desired, r))
                return desired;
            IntVec3 nearDesired = CellFinder.StandableCellNear(desired, destMap, 48);
            if (nearDesired.IsValid && nearDesired.InBounds(destMap) && nearDesired.Standable(destMap) &&
                IsCellInEnergyFieldMask(destMap, newCenter, nearDesired, r))
                return nearDesired;
            List<IntVec3> diskList = new List<IntVec3>(newValidFieldCells);
            for (int s = 0; s < diskList.Count; s++)
            {
                int j = Rand.Range(0, diskList.Count);
                (diskList[s], diskList[j]) = (diskList[j], diskList[s]);
            }

            for (int i = 0; i < diskList.Count; i++)
            {
                IntVec3 c = diskList[i];
                if (c.InBounds(destMap) && c.Standable(destMap))
                    return c;
            }

            for (int attempt = 0; attempt < 120; attempt++)
            {
                IntVec3 c = newCenter + new IntVec3(Rand.RangeInclusive(-40, 40), 0, Rand.RangeInclusive(-40, 40));
                if (c.InBounds(destMap) && c.Standable(destMap) && IsCellInEnergyFieldMask(destMap, newCenter, c, r))
                    return c;
            }

            return CellFinder.StandableCellNear(newCenter, destMap, 96);
        }
    }
}
