using UnityEngine;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    public static class LegionDroneFlightUtility
    {
        public const float ExitSpeedCellsPerTick = 1.5f;

        private const float ExitPastEdgeCells = 2.5f;

        public static Vector2 GetMapCenterXZ(Map map)
        {
            if (map == null)
                return Vector2.zero;
            Vector3 c = map.Center.ToVector3Shifted();
            return new Vector2(c.x, c.z);
        }

        public static Vector2 MirrorXZ(Vector2 pos, Vector2 mapCenter)
        {
            return 2f * mapCenter - pos;
        }

        /// <summary>
        /// First point along dir from fromXZ that is outside map bounds (screen exit).
        /// </summary>
        public static bool TryComputeExitPoint(Map map, Vector2 fromXZ, Vector2 dir, out Vector2 exitXZ)
        {
            exitXZ = fromXZ;
            if (map == null || dir.sqrMagnitude < 1e-8f)
                return false;

            dir.Normalize();

            if (TryRayExitSlab(map, fromXZ, dir, out exitXZ))
                return true;

            return TryMarchUntilOutOfBounds(map, fromXZ, dir, out exitXZ);
        }

        private static bool TryRayExitSlab(Map map, Vector2 fromXZ, Vector2 dir, out Vector2 exitXZ)
        {
            exitXZ = fromXZ;
            float minX = -ExitPastEdgeCells;
            float maxX = map.Size.x + ExitPastEdgeCells;
            float minZ = -ExitPastEdgeCells;
            float maxZ = map.Size.z + ExitPastEdgeCells;

            float tExit = float.PositiveInfinity;

            if (dir.x > 1e-6f)
                tExit = Mathf.Min(tExit, (maxX - fromXZ.x) / dir.x);
            else if (dir.x < -1e-6f)
                tExit = Mathf.Min(tExit, (minX - fromXZ.x) / dir.x);

            if (dir.y > 1e-6f)
                tExit = Mathf.Min(tExit, (maxZ - fromXZ.y) / dir.y);
            else if (dir.y < -1e-6f)
                tExit = Mathf.Min(tExit, (minZ - fromXZ.y) / dir.y);

            if (float.IsPositiveInfinity(tExit) || tExit < 0.05f)
                return false;

            exitXZ = fromXZ + dir * (tExit + ExitPastEdgeCells * 0.5f);
            return IsWorldXZOutOfBounds(map, exitXZ);
        }

        private static bool TryMarchUntilOutOfBounds(Map map, Vector2 fromXZ, Vector2 dir, out Vector2 exitXZ)
        {
            exitXZ = fromXZ;
            float maxDist = (Mathf.Max(map.Size.x, map.Size.z) + 16f) * 1.5f;
            const float step = 0.5f;
            for (float d = step; d <= maxDist; d += step)
            {
                Vector2 p = fromXZ + dir * d;
                if (!IsWorldXZOutOfBounds(map, p))
                    continue;
                exitXZ = p;
                return true;
            }

            return false;
        }

        private static bool IsWorldXZOutOfBounds(Map map, Vector2 worldXZ)
        {
            IntVec3 cell = new Vector3(worldXZ.x, 0f, worldXZ.y).ToIntVec3();
            return !cell.InBounds(map);
        }

        public static int DurationTicksForDistance(float dist, float speedCellsPerTick)
        {
            if (speedCellsPerTick <= 0f)
                return 10;
            return Mathf.Clamp(Mathf.CeilToInt(dist / speedCellsPerTick), 8, 280);
        }
    }
}
