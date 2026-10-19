namespace PMP_Persona.SupernovaSupport
{
    public static class SupernovaConstants
    {
        public const string SupernovaDefName = "PMP_Supernova";
        public const string ExposureHediffDefName = "PMP_SupernovaExposure";

        public const float FieldRadiusCells = 30f;
        public const int FieldRadiusCellsInt = 30;

        public const int LifetimeTicks = 7200;

        // One-second PMC singularity / rupture intro, including the star's enlargement and tremor.
        public const int WarpDeployTicks = 60;

        public const int MapScanIntervalTicks = 240;
        public const int PendingProcessIntervalTicks = 60;
        public const int HediffPulseIntervalTicks = 15;

        public const float DamagePerPulse = 20f;
        public const int BootstrapHitCount = 4;

        // 4s in field = 240 ticks, pulse every 15 ticks => 16 pulses => severity 2.0 => -200% heat armor.
        public const float ExposureSeverityPerPulse = 0.125f;

        // Linear scaling with severity (no hediff severity cap).
        public const float ExposurePainFactorBase = 1f;
        public const float ExposurePainFactorPerSeverity = 1.25f;
        public const float ExposureHeatArmorOffsetPerSeverity = 1f;

        // Visual cycle durations retained for the native 3D radiative-transfer renderer.
        public const float DefaultShellDiameter = FieldRadiusCells * 2f;
        public const float ShellPreFlashDurationSec = 0.16f;
        public const float ShellExpansionDurationSec = 1.6f;
        public const float ShellInteractionDurationSec = 2.0f;
        public const float ShellDecayDurationSec = 2.4f;
        public const float ShellLoopIdleDurationSec = 0.75f;
        public const float ShellLoopFadeOutDurationSec = 0.35f;

        // Shock front reaches 95% of the visual volume radius.
        public const float ShellShockwaveRadiusFraction = 0.95f;

        // Exposure severity that maps to full plasma intensity.
        public const float PlasmaSeverityForFull = 1.5f;

        // Spawn/despawn fade so masks never pop in/out (seconds).
        public const float PlasmaFadeInSec = 0.45f;
        public const float PlasmaFadeOutSec = 0.6f;

    }
}
