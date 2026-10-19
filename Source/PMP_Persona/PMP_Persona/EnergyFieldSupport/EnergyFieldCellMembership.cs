using RimWorld;
using Verse;

namespace PMP_Persona.EnergyFieldSupport
{
    // Geometric disk minus blocked: impassable or not walkable.
    public static class EnergyFieldCellMembership
    {
        public static bool InGeometricDisk(IntVec3 center, IntVec3 cell, float radiusCells)
        {
            return cell.InHorDistOf(center, radiusCells);
        }

        // True when the cell counts as inside the energy field for warp / pawn membership.
        public static bool IsValidFieldCell(Map map, IntVec3 center, IntVec3 cell, float radiusCells)
        {
            if (map == null || !cell.InBounds(map))
                return false;
            if (!InGeometricDisk(center, cell, radiusCells))
                return false;
            return !IsBlockedCell(map, cell);
        }

        private static bool IsBlockedCell(Map map, IntVec3 cell)
        {
            if (cell.Impassable(map))
                return true;
            if (!cell.Walkable(map))
                return true;
            return false;
        }
    }
}
