namespace PMP_Persona.EnergyFieldSupport
{
    // Player-deployed energy field radius (cells). Distinct from vanilla Pylon warp scan radius in ProtossMech.
    public static class EnergyFieldConstants
    {
        public const float FieldRadiusCells = 10f;
        public const int PawnTrackingIntervalTicks = 240;
        // Fixed delay after strike fleck spawn before pawn teleport (avoid relying on fleck lifetime / NRE).
        public const int TeleportAfterStrikeTicks = 90;
    }
}
