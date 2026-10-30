using System;
using System.Collections.Generic;
using PMP_Persona;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.PrismSupport
{
    // Orbital laser (Weapon 3): two-step targeting (start, then end on ring). Draws ring and thick line while choosing end.
    public static class OrbitalLaserTargeter
    {
        // Laser travel length in cells.
        private const int RingRadius = 24;
        private const int RingRadiusSqMin = (RingRadius - 1) * (RingRadius - 1);
        private const int RingRadiusSqMax = (RingRadius + 1) * (RingRadius + 1);

        public static Map Map { get; private set; }
        public static IntVec3? StartCell { get; private set; }
        public static bool IsActive => Map != null && StartCell.HasValue;

        private static ThingDef _beamDef;
        private static bool _secondPhaseActive;

        // Called when targeter is stopped (e.g. right-click cancel); clears state and stops drawing.
        public static void OnTargetingEnded()
        {
            if (_secondPhaseActive)
                Clear();
        }

        public static void Clear()
        {
            Map = null;
            StartCell = null;
            _beamDef = null;
            _secondPhaseActive = false;
        }

        public static void BeginTargeting(Map map, ThingDef beamDef, Action<IntVec3, IntVec3> onSpawnBeam)
        {
            if (map == null || beamDef == null || onSpawnBeam == null)
                return;
            Clear();

            var startParms = new TargetingParameters
            {
                canTargetLocations = true,
                canTargetSelf = false,
                canTargetPawns = false,
                canTargetBuildings = false
            };

            Find.Targeter.BeginTargeting(startParms, startTarget =>
            {
                IntVec3 start = startTarget.Cell;
                if (!start.InBounds(map))
                    return;

                StartCell = start;
                Map = map;
                _beamDef = beamDef;

                var endParms = new TargetingParameters
                {
                    canTargetLocations = true,
                    canTargetSelf = false,
                    canTargetPawns = false,
                    canTargetBuildings = false
                };

                _secondPhaseActive = true;
                Find.Targeter.BeginTargeting(endParms, endTarget =>
                {
                    IntVec3 end = endTarget.Cell;
                    Clear();
                    IntVec3 endCorrected = SnapEndToRing(start, end);
                    if (!endCorrected.InBounds(map))
                        return;
                    onSpawnBeam(start, endCorrected);
                }, null, null, null);
            }, null, null, null);
        }

        // Snap chosen end onto the ring (radius RingRadius) in the same direction from start.
        private static IntVec3 SnapEndToRing(IntVec3 start, IntVec3 end)
        {
            int dx = end.x - start.x;
            int dz = end.z - start.z;
            int lenSq = dx * dx + dz * dz;
            if (lenSq <= 0)
                return start + new IntVec3(RingRadius, 0, 0);
            float len = Mathf.Sqrt(lenSq);
            float ux = dx / len;
            float uz = dz / len;
            int rx = (int)Mathf.Round(ux * RingRadius);
            int rz = (int)Mathf.Round(uz * RingRadius);
            return new IntVec3(start.x + rx, 0, start.z + rz);
        }

        // Draw thick line from start to mouse cell when choosing end (no ring; end is auto-corrected to ring).
        public static void Draw()
        {
            if (!IsActive || Map == null || !StartCell.HasValue)
                return;
            IntVec3 start = StartCell.Value;
            IntVec3 end = UI.MouseCell();
            if (end.InBounds(Map))
                DrawThickLineBetween(start, end);
        }

        // Beam damage radius (cells) for draw thickness; from PMP_OrbitalLaser beamRadiusOverride, default 3.
        private static int BeamRadiusForDraw => _beamDef?.GetModExtension<MobileBeamExtension>()?.beamRadiusOverride ?? 3;

        // Draw grid-aligned rectangle from start to end; width = beam damage diameter (2 * radius).
        private static void DrawThickLineBetween(IntVec3 start, IntVec3 end)
        {
            int halfWidth = BeamRadiusForDraw;
            float alt = AltitudeLayer.MetaOverlays.AltitudeFor();
            Vector3 startV = start.ToVector3ShiftedWithAltitude(alt);
            Vector3 endV = end.ToVector3ShiftedWithAltitude(alt);
            Vector3 dir = (endV - startV);
            float len = new Vector2(dir.x, dir.z).magnitude;
            if (len < 0.001f)
                return;
            dir = dir / len;
            Vector3 perp = new Vector3(-dir.z, 0f, dir.x) * halfWidth;
            Vector3 p0 = startV + perp;
            Vector3 p1 = startV - perp;
            Vector3 p2 = endV - perp;
            Vector3 p3 = endV + perp;
            GenDraw.DrawLineBetween(p0, p3);
            GenDraw.DrawLineBetween(p3, p2);
            GenDraw.DrawLineBetween(p2, p1);
            GenDraw.DrawLineBetween(p1, p0);
        }

        private static IEnumerable<IntVec3> CellsOnLine(IntVec3 a, IntVec3 b)
        {
            int x0 = a.x, z0 = a.z;
            int x1 = b.x, z1 = b.z;
            int dx = Math.Abs(x1 - x0), dz = Math.Abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1, sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;

            while (true)
            {
                yield return new IntVec3(x0, 0, z0);
                if (x0 == x1 && z0 == z1)
                    break;
                int e2 = 2 * err;
                if (e2 > -dz) { err -= dz; x0 += sx; }
                if (e2 < dx) { err += dx; z0 += sz; }
            }
        }
    }
}
