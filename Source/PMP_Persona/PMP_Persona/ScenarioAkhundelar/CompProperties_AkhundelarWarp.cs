using Verse;

namespace PMP_Persona.ScenarioAkhundelar
{
    // Shared fleck tuning (mirrors ProtossMech.CompProperties_WarpIn for scenario beacons).
    public class CompProperties_AkhundelarWarpVisual : CompProperties
    {
        public FleckDef strikeFleck;
        public FleckDef continuousFleck;
        public float fleckScale = 1f;
        public int fleckSpawnInterval = 45;
        public int fleckExtraTicksBeforeStaticPulse = 2;
        public float fleckOffsetX;
        public float fleckOffsetY;
        public float fleckOffsetZ = 6f;
        public float fleckStrikeSpawnForward = 48f;
        public int fleckStrikeDurationTicks = 15;
    }

    public class CompProperties_AkhundelarAnchorBeacon : CompProperties_AkhundelarWarpVisual
    {
        public int ticksToPylon = 180;
        public string buildingDefName = "Pylon";

        public CompProperties_AkhundelarAnchorBeacon()
        {
            compClass = typeof(CompAkhundelarAnchorBeacon);
        }
    }

    public class CompProperties_AkhundelarPawnWarp : CompProperties_AkhundelarWarpVisual
    {
        public int warpTicks = 180;

        public CompProperties_AkhundelarPawnWarp()
        {
            compClass = typeof(CompAkhundelarPawnWarp);
        }
    }
}
