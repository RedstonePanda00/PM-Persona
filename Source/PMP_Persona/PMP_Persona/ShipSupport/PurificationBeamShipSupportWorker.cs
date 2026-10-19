using RimWorld;
using Verse;

namespace PMP_Persona.ShipSupport
{
    public class PurificationBeamShipSupportWorker : ShipSupportWorker
    {
        protected override void BeginActivation(ShipSupportContext context)
        {
            Map map = context.Map;
            Find.Targeter.BeginTargeting(
                LocationTargetingParameters(map),
                delegate(LocalTargetInfo target)
                {
                    IntVec3 cell = target.Cell;
                    if (!cell.InBounds(map))
                        return;

                    TryCommit(map, delegate
                    {
                        Thing beam = GenSpawn.Spawn(def.activeThingDef, cell, map, WipeMode.Vanish);
                        if (beam == null)
                            return false;
                        beam.SetFactionDirect(Faction.OfPlayer);
                        Find.Selector.ClearSelection();
                        Find.Selector.Select(beam);
                        return true;
                    });
                },
                null,
                null);
            context.Window?.Close();
        }
    }
}
