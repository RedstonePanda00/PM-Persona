using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PMP_Persona.ShipSupport
{
    public static class ShipSupportRuntime
    {
        public const string FleetBeaconDefName = "PMP_FleetBeacon";

        private static int cacheTick = -1;
        private static bool? beaconAvailable;
        private static readonly Dictionary<ThingDef, Thing> ActiveThings = new Dictionary<ThingDef, Thing>();

        public static void Invalidate()
        {
            cacheTick = -1;
            beaconAvailable = null;
            ActiveThings.Clear();
            ShipSupportPaymentService.InvalidateCache();
        }

        public static bool HasFleetBeacon()
        {
            EnsureTick();
            if (beaconAvailable.HasValue)
                return beaconAvailable.Value;

            ThingDef beaconDef = DefDatabase<ThingDef>.GetNamedSilentFail(FleetBeaconDefName);
            if (beaconDef == null || Find.Maps == null)
                return (beaconAvailable = false).Value;

            for (int i = 0; i < Find.Maps.Count; i++)
            {
                Map map = Find.Maps[i];
                List<Building> buildings = map?.listerBuildings?.AllBuildingsColonistOfDef(beaconDef);
                if (buildings == null)
                    continue;
                for (int j = 0; j < buildings.Count; j++)
                {
                    Building building = buildings[j];
                    if (building != null && building.Spawned && !building.Destroyed
                        && building.Faction == Faction.OfPlayer)
                    {
                        beaconAvailable = true;
                        return true;
                    }
                }
            }

            beaconAvailable = false;
            return false;
        }

        public static Thing FindActiveThing(ThingDef thingDef)
        {
            if (thingDef == null)
                return null;
            EnsureTick();

            if (ActiveThings.TryGetValue(thingDef, out Thing cached))
                return cached;

            Thing active = null;
            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count && active == null; i++)
                {
                    Map map = Find.Maps[i];
                    List<Thing> things = map?.listerThings?.ThingsOfDef(thingDef);
                    if (things == null)
                        continue;
                    for (int j = 0; j < things.Count; j++)
                    {
                        Thing thing = things[j];
                        if (thing != null && thing.Spawned && !thing.Destroyed)
                        {
                            active = thing;
                            break;
                        }
                    }
                }
            }

            ActiveThings[thingDef] = active;
            return active;
        }

        public static IntVec3 FindStandableSpawnCell(Map map)
        {
            if (map == null)
                return IntVec3.Invalid;

            IntVec3 center = map.Center;
            if (center.InBounds(map) && center.Standable(map))
                return center;

            IntVec3 near = CellFinder.StandableCellNear(center, map, 15);
            if (near.IsValid && near.InBounds(map) && near.Standable(map))
                return near;

            near = CellFinder.StandableCellNear(center, map, 40);
            if (near.IsValid && near.InBounds(map) && near.Standable(map))
                return near;

            CellRect rect = CellRect.WholeMap(map);
            for (int i = 0; i < 50; i++)
            {
                IntVec3 cell = rect.RandomCell;
                if (cell.Standable(map))
                    return cell;
            }
            return IntVec3.Invalid;
        }

        private static void EnsureTick()
        {
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (cacheTick == tick)
                return;
            cacheTick = tick;
            beaconAvailable = null;
            ActiveThings.Clear();
        }
    }
}
