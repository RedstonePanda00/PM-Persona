using PMP_Persona.ShipSupport;
using RimWorld;
using Verse;

namespace PMP_Persona.RecomposerSupport
{
    public class RecomposerShipSupportWorker : ShipSupportWorker
    {
        protected override void BeginActivation(ShipSupportContext context)
        {
            Map map = context.Map;
            RecomposerTargeting.Begin(map);
            Find.Targeter.BeginTargeting(
                LocationTargetingParameters(map),
                delegate(LocalTargetInfo target)
                {
                    RecomposerTargeting.Clear();
                    IntVec3 cell = target.Cell;
                    if (!cell.InBounds(map))
                        return;

                    TryCommit(map, delegate
                    {
                        Thing_RecomposerMarker marker =
                            ThingMaker.MakeThing(def.activeThingDef) as Thing_RecomposerMarker;
                        if (marker == null)
                            return false;
                        marker.Configure(cell, map);
                        GenSpawn.Spawn(marker, cell, map, WipeMode.Vanish);
                        return marker.Spawned;
                    });
                },
                null,
                RecomposerTargeting.Clear);
            context.Window?.Close();
        }
    }
}
