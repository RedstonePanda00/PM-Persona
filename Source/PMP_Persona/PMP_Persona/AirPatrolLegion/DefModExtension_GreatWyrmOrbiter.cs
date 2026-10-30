using System.Collections.Generic;
using Verse;

namespace PMP_Persona.AirPatrolLegion
{
    // Config on PMP_GreatWyrmOrbiter ThingDef: tuning only (extra targets: see AirPatrolExtraTargetsDef XML).
    public class DefModExtension_GreatWyrmOrbiter : DefModExtension
    {
        public float formationForwardSpacing = 7f;
        public float formationLateralSpacing = 7f;
        public float speedCellsPerTick = 3.0f;
        public float planeDrawScale = 2.8f;
        public float planeDrawDepthFactor = 0.75f;
        public float mapEdgeMarginCells = 4f;

        // Wing-tip motion trails (ring buffer; drawn as transparent ribbons).
        public bool wingTrailEnabled = true;
        public int wingTrailRingCapacity = 28;
        public int wingTrailSampleIntervalTicks = 0;
        public float wingTrailWidthFactor = 0.04f;
        public float wingTrailSegmentLengthFactor = 1.7f;
        public float wingTrailSpanFactor = 0.4f;
        public float wingTrailAftFactor = 0f;
        public float wingTrailAlphaMin = 0.18f;
        public float wingTrailAlphaMax = 0.98f;
        public float wingTrailYOffset = 0.04f;
        public float orbSize = 1.0f;
        public float orbIntensity = 3.0f;
        public float trailIntensity = 2.0f;
        public float missileOrbSize = 0.55f;
        public float trailWidthFactor = 1.0f;
        public float missileTrailWidthFactor = 0.45f;

        // Missile steering parameters.
        public float missileTurnLerp = 0.18f;
        public float missileInitialForwardBias = 0.65f;

        // Missile trail segment parameters.
        public float missileTrailMaxSegmentCells = 0.6f;
        public int missileTrailMaxSubsteps = 6;
        public float missileTrailTipWidthFactor = 0.18f;
        public float missileTrailSegmentLengthFactor = 1.05f;
    }
}

