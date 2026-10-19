using RimWorld;
using Verse;

namespace PMP_Persona
{
    // One vanilla WeatherEvent_LightningStrike per cell in a circular radius (GenRadial.RadialCellsAround).
    public class CompAbilityEffect_ZeratulLandingLightning : CompAbilityEffect
    {
        public new CompProperties_ZeratulLandingLightning Props => (CompProperties_ZeratulLandingLightning)props;

        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            base.Apply(target, dest);
            Map map = parent.pawn.Map;
            if (map == null)
            {
                return;
            }

            IntVec3 center = target.Cell;
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, Props.lightningRadius, useCenter: true))
            {
                if (!cell.InBounds(map))
                {
                    continue;
                }

                map.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(map, cell));
            }

            TrySpawnMiniTornado(map, center);
        }

        private void TrySpawnMiniTornado(Map map, IntVec3 center)
        {
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail("PMPH_MiniTornado");
            if (def == null)
            {
                return;
            }

            PMPH_MiniTornado tornado = (PMPH_MiniTornado)ThingMaker.MakeThing(def);
            tornado.effectRadius = Props.lightningRadius;
            tornado.remainingDurationTicks = GenTicks.SecondsToTicks(Props.tornadoDurationSeconds);
            tornado.instigator = parent.pawn;
            GenSpawn.Spawn(tornado, center, map);
        }
    }

    public class CompProperties_ZeratulLandingLightning : CompProperties_AbilityEffect
    {
        public float lightningRadius = 6f;

        public float tornadoDurationSeconds = 1f;

        public CompProperties_ZeratulLandingLightning()
        {
            compClass = typeof(CompAbilityEffect_ZeratulLandingLightning);
        }
    }
}
