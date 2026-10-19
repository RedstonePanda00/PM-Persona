using PMP_Persona.ShipSupport;
using RimWorld;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    public class CarrierShipSupportWorker : ShipSupportWorker
    {
        protected override void BeginActivation(ShipSupportContext context)
        {
            Map map = context.Map;
            IntVec3 cell = ShipSupportRuntime.FindStandableSpawnCell(map);
            if (!cell.IsValid)
            {
                Messages.Message("PMP.Support.Error.NoSpawnCell".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return;
            }

            if (TryCommit(map, delegate
                {
                    Thing spawned = GenSpawn.Spawn(def.activeThingDef, cell, map, WipeMode.Vanish);
                    Building_CarrierOrbiter orbiter = spawned as Building_CarrierOrbiter;
                    if (orbiter == null)
                    {
                        spawned?.Destroy(DestroyMode.Vanish);
                        return false;
                    }
                    orbiter.SetFactionDirect(Faction.OfPlayer);
                    orbiter.SetupOrbit(map.Center);
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(orbiter);
                    SupportSummonLetterUtility.TrySendSummonLetter(def.activeThingDef, orbiter);
                    return true;
                }))
            {
                context.Window?.Close();
            }
        }
    }
}
