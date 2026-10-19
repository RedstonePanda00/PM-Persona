using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    public static class LegionBombardmentUtility
    {
        public const int StrikeStaggerTicks = 2;

        private static DamageDef cachedBombDirectHit;
        private static DamageDef cachedBombSuper;

        public static DamageDef BombDirectHitDef =>
            cachedBombDirectHit ?? (cachedBombDirectHit =
                DefDatabase<DamageDef>.GetNamedSilentFail("PMP_BombDirectHit"));

        public static DamageDef BombSuperDef =>
            cachedBombSuper ?? (cachedBombSuper =
                DefDatabase<DamageDef>.GetNamedSilentFail("BombSuper"));

        public static List<IntVec3> BuildCenteredLineCells(IntVec3 target, Vector2 startXZ, float lineLengthCells,
            int count, float lateralOffsetMax, Map map)
        {
            var cells = new List<IntVec3>(count);
            if (map == null || count < 1)
                return cells;

            Vector3 targetV = target.ToVector3Shifted();
            Vector2 targetXZ = new Vector2(targetV.x, targetV.z);
            Vector2 delta = targetXZ - startXZ;
            if (delta.sqrMagnitude < 0.01f)
                delta = Vector2.right;
            Vector2 dir = delta.normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            for (int i = 0; i < count; i++)
            {
                float along = count == 1 ? 0f : (i / (float)(count - 1) - 0.5f) * lineLengthCells;
                float lateral = lateralOffsetMax > 0f ? Rand.Range(-lateralOffsetMax, lateralOffsetMax) : 0f;
                Vector2 world = targetXZ + dir * along + perp * lateral;
                IntVec3 cell = new Vector3(world.x, 0f, world.y).ToIntVec3();
                if (!cell.InBounds(map))
                    cell = target;
                else if (!cell.Standable(map))
                {
                    IntVec3 near = CellFinder.StandableCellNear(cell, map, 2);
                    if (near.IsValid)
                        cell = near;
                }
                cells.Add(cell);
            }

            return cells;
        }

        public static void ApplyDirectHitAtCell(Map map, IntVec3 cell, int amount, Thing instigator)
        {
            if (map == null || amount <= 0)
                return;

            DamageDef directDef = BombDirectHitDef;
            if (directDef == null)
                return;

            List<Thing> things = cell.GetThingList(map);
            for (int i = things.Count - 1; i >= 0; i--)
            {
                if (things[i] is Pawn pawn && !pawn.Dead)
                {
                    var dinfo = new DamageInfo(directDef, amount, 999f, -1f, instigator);
                    pawn.TakeDamage(dinfo);
                }
            }
        }

        public static void DoBombSuperExplosion(Map map, IntVec3 center, float radius, int damAmount, Thing instigator)
        {
            if (map == null)
                return;

            DamageDef bombDef = BombSuperDef ?? DamageDefOf.Bomb;
            GenExplosion.DoExplosion(center, map, radius, bombDef, instigator, damAmount: damAmount);
        }

        public static void ExecuteStrike(Map map, IntVec3 cell, float radius, int explosionDamage, int directDamage,
            Thing instigator)
        {
            if (map == null || !cell.InBounds(map))
                return;

            ApplyDirectHitAtCell(map, cell, directDamage, instigator);
            DoBombSuperExplosion(map, cell, radius, explosionDamage, instigator);
        }
    }
}
