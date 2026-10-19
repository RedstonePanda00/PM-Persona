using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PMP_Persona.ScenarioAkhundelar
{
    internal static class AkhundelarArrivalUtility
    {
        internal static IntVec3 PylonWarpHub(IntVec3 pylonBottomLeft, Map map)
        {
            CellRect rect = new CellRect(pylonBottomLeft.x, pylonBottomLeft.z, 2, 2);
            IntVec3 hub = rect.CenterCell;
            return hub.InBounds(map) ? hub : pylonBottomLeft;
        }

        internal static IntVec3 OffsetStandable(Map map, IntVec3 origin, int dx, int dz)
        {
            IntVec3 t = origin + new IntVec3(dx, 0, dz);
            if (t.InBounds(map) && t.Standable(map))
            {
                return t;
            }

            for (int r = 0; r < 8; r++)
            {
                foreach (IntVec3 c in GenRadial.RadialCellsAround(origin + new IntVec3(dx, 0, dz), r, true))
                {
                    if (c.InBounds(map) && c.Standable(map))
                    {
                        return c;
                    }
                }
            }

            return origin;
        }

        // Preferred grid offset from hub; if taken or invalid, spirals outward until a free standable cell is found.
        internal static IntVec3 OffsetStandableExclusive(Map map, IntVec3 origin, int dx, int dz, HashSet<IntVec3> reserved)
        {
            IntVec3 target = origin + new IntVec3(dx, 0, dz);
            for (int d = 0; d < 16; d++)
            {
                foreach (IntVec3 c in GenRadial.RadialCellsAround(target, d, true))
                {
                    if (!c.InBounds(map) || !c.Standable(map) || reserved.Contains(c))
                    {
                        continue;
                    }

                    reserved.Add(c);
                    return c;
                }
            }

            IntVec3 fallback = OffsetStandable(map, origin, dx, dz);
            if (!reserved.Contains(fallback))
            {
                reserved.Add(fallback);
            }

            return fallback;
        }

        internal static bool TryFindPylonBottomLeftNear(Map map, IntVec3 near, ThingDef pylonDef, out IntVec3 bottomLeft)
        {
            for (int d = 0; d < 40; d++)
            {
                foreach (IntVec3 origin in GenRadial.RadialCellsAround(near, d, true))
                {
                    if (!origin.InBounds(map))
                    {
                        continue;
                    }

                    if (GenConstruct.CanPlaceBlueprintAt(pylonDef, origin, Rot4.North, map).Accepted)
                    {
                        bottomLeft = origin;
                        return true;
                    }
                }
            }

            bottomLeft = IntVec3.Invalid;
            return false;
        }

        internal static void TryAddOverseerRelations(Pawn overseer, IEnumerable<Pawn> subjects)
        {
            if (overseer == null || subjects == null)
            {
                return;
            }

            foreach (Pawn mech in subjects)
            {
                if (mech == null || mech.Destroyed || mech.OverseerSubject == null)
                {
                    continue;
                }

                if (!MechanitorUtility.IsMechanitor(overseer) || !overseer.mechanitor.CanOverseeSubject(mech))
                {
                    continue;
                }

                if (mech.relations.DirectRelationExists(PawnRelationDefOf.Overseer, overseer))
                {
                    continue;
                }

                overseer.relations.AddDirectRelation(PawnRelationDefOf.Overseer, mech);
            }
        }
    }
}
