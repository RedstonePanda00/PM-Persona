using System;
using System.Collections.Generic;
using System.Reflection;
using ProtossMech;
using RimWorld;
using Verse;

namespace PMP_Persona.ShipSupport
{
    public sealed class ShipSupportPaymentReceipt
    {
        internal readonly List<HyperspaceStorageDelta> storageDeltas = new List<HyperspaceStorageDelta>();
        internal readonly List<HeldPhysicalResource> heldPhysical = new List<HeldPhysicalResource>();
        private bool completed;

        internal sealed class HeldPhysicalResource
        {
            public Thing thing;
            public Map map;
            public IntVec3 cell;
        }

        public void Commit()
        {
            if (completed)
                return;
            completed = true;
            for (int i = 0; i < heldPhysical.Count; i++)
            {
                Thing thing = heldPhysical[i].thing;
                if (thing != null && !thing.Destroyed)
                    thing.Destroy(DestroyMode.Vanish);
            }
            heldPhysical.Clear();
            storageDeltas.Clear();
            ShipSupportPaymentService.InvalidateCache();
        }

        public void Rollback()
        {
            if (completed)
                return;
            completed = true;

            for (int i = storageDeltas.Count - 1; i >= 0; i--)
            {
                HyperspaceStorageDelta delta = storageDeltas[i];
                if (delta.Delta >= 0L)
                    continue;
                if (!HyperspaceStorageApi.TryDeposit(
                        delta.StorageId,
                        delta.ThingDef,
                        -delta.Delta,
                        out long accepted)
                    || accepted != -delta.Delta)
                {
                    Log.Error("[PMP ShipSupport] Could not fully roll back hyperspace payment for "
                              + delta.ThingDef?.defName + ".");
                }
            }

            for (int i = 0; i < heldPhysical.Count; i++)
            {
                HeldPhysicalResource held = heldPhysical[i];
                Thing thing = held.thing;
                if (thing == null || thing.Destroyed)
                    continue;
                if (held.map != null && !held.map.Disposed)
                {
                    IntVec3 cell = held.cell.InBounds(held.map) ? held.cell : held.map.Center;
                    if (!GenPlace.TryPlaceThing(thing, cell, held.map, ThingPlaceMode.Near))
                        Log.Error("[PMP ShipSupport] Could not restore physical payment " + thing.ToStringSafe());
                }
                else
                {
                    thing.Destroy(DestroyMode.Vanish);
                    Log.Error("[PMP ShipSupport] Payment rollback map no longer exists; discarded "
                              + thing.ToStringSafe());
                }
            }

            heldPhysical.Clear();
            storageDeltas.Clear();
            ShipSupportPaymentService.InvalidateCache();
        }
    }

    public static class ShipSupportPaymentService
    {
        private static readonly MethodInfo ResourceCounterShouldCountMethod =
            typeof(ResourceCounter).GetMethod(
                "ShouldCount",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Thing) },
                null);

        private static int affordabilityCacheTick = -1;
        private static readonly Dictionary<string, bool> AffordabilityCache = new Dictionary<string, bool>();

        private sealed class PaymentPlan
        {
            public readonly List<HyperspaceStorageRequest> storageRequests =
                new List<HyperspaceStorageRequest>();
            public readonly List<PhysicalAllocation> physicalAllocations =
                new List<PhysicalAllocation>();
        }

        private sealed class PhysicalAllocation
        {
            public Thing thing;
            public int count;
        }

        public static void InvalidateCache()
        {
            affordabilityCacheTick = -1;
            AffordabilityCache.Clear();
        }

        public static bool CanAfford(ShipSupportDef support, Map map)
        {
            if (support == null)
                return false;
            int tick = Find.TickManager?.TicksGame ?? -1;
            if (affordabilityCacheTick != tick)
            {
                affordabilityCacheTick = tick;
                AffordabilityCache.Clear();
            }

            string key = support.defName + ":" + (map?.uniqueID ?? -1);
            if (AffordabilityCache.TryGetValue(key, out bool cached))
                return cached;

            bool result = TryResolvePlan(support.costList, map, out PaymentPlan _);
            AffordabilityCache[key] = result;
            return result;
        }

        public static bool TryBeginPayment(
            ShipSupportDef support,
            Map map,
            out ShipSupportPaymentReceipt receipt)
        {
            receipt = null;
            if (support == null || !TryResolvePlan(support.costList, map, out PaymentPlan plan))
                return false;

            receipt = new ShipSupportPaymentReceipt();
            if (plan.storageRequests.Count > 0)
            {
                if (!HyperspaceStorageApi.TryConsumeBatch(
                        plan.storageRequests,
                        HyperspaceStorageScope.AllPlayerMaps(),
                        out List<HyperspaceStorageDelta> deltas))
                {
                    receipt = null;
                    return false;
                }
                if (deltas != null)
                    receipt.storageDeltas.AddRange(deltas);
            }

            try
            {
                for (int i = 0; i < plan.physicalAllocations.Count; i++)
                {
                    PhysicalAllocation allocation = plan.physicalAllocations[i];
                    Thing source = allocation.thing;
                    if (source == null || source.Destroyed || !source.Spawned
                        || source.stackCount < allocation.count)
                    {
                        receipt.Rollback();
                        receipt = null;
                        return false;
                    }

                    Map sourceMap = source.Map;
                    IntVec3 sourceCell = source.Position;
                    Thing held = source.SplitOff(allocation.count);
                    if (held == null)
                        throw new InvalidOperationException("SplitOff returned null.");
                    if (held.Spawned)
                        held.DeSpawn(DestroyMode.Vanish);
                    receipt.heldPhysical.Add(new ShipSupportPaymentReceipt.HeldPhysicalResource
                    {
                        thing = held,
                        map = sourceMap,
                        cell = sourceCell
                    });
                }
            }
            catch (Exception exception)
            {
                Log.Error("[PMP ShipSupport] Could not prepare physical payment: " + exception);
                receipt.Rollback();
                receipt = null;
                return false;
            }

            InvalidateCache();
            return true;
        }

        public static string GetCostString(ShipSupportDef support)
        {
            if (support?.costList == null || support.costList.Count == 0)
                return "PMP.Support.Cost.Free".Translate();

            List<string> parts = new List<string>(support.costList.Count);
            for (int i = 0; i < support.costList.Count; i++)
            {
                ShipSupportCost cost = support.costList[i];
                if (cost?.thingDef != null && cost.count > 0)
                    parts.Add(cost.thingDef.LabelCap + " ×" + cost.count);
            }
            return string.Join(", ", parts.ToArray());
        }

        private static bool TryResolvePlan(List<ShipSupportCost> costs, Map map, out PaymentPlan plan)
        {
            plan = new PaymentPlan();
            if (!TryAggregate(costs, out Dictionary<ThingDef, long> aggregated))
                return false;

            foreach (KeyValuePair<ThingDef, long> cost in aggregated)
            {
                long stored = HyperspaceStorageApi.GetTotal(
                    cost.Key,
                    HyperspaceStorageScope.AllPlayerMaps());
                if (stored >= cost.Value)
                {
                    plan.storageRequests.Add(new HyperspaceStorageRequest(cost.Key, cost.Value));
                    continue;
                }

                if (!TryAllocatePhysical(map, cost.Key, cost.Value, plan.physicalAllocations))
                {
                    plan = null;
                    return false;
                }
            }

            return true;
        }

        private static bool TryAggregate(
            List<ShipSupportCost> costs,
            out Dictionary<ThingDef, long> aggregated)
        {
            aggregated = new Dictionary<ThingDef, long>();
            if (costs == null)
                return true;

            for (int i = 0; i < costs.Count; i++)
            {
                ShipSupportCost cost = costs[i];
                if (cost == null || cost.thingDef == null || cost.count < 0)
                    return false;
                if (cost.count == 0)
                    continue;
                aggregated.TryGetValue(cost.thingDef, out long current);
                try
                {
                    aggregated[cost.thingDef] = checked(current + cost.count);
                }
                catch (OverflowException)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TryAllocatePhysical(
            Map map,
            ThingDef thingDef,
            long requested,
            List<PhysicalAllocation> output)
        {
            if (requested == 0L)
                return true;
            if (map == null || thingDef == null)
                return false;

            int start = output.Count;
            long remaining = requested;
            List<Thing> things = map.listerThings.ThingsOfDef(thingDef);
            for (int i = 0; i < things.Count && remaining > 0L; i++)
            {
                Thing thing = things[i];
                if (thing == null || !thing.Spawned || thing.Destroyed
                    || !IsCountedPhysicalResource(map, thing))
                {
                    continue;
                }

                int take = (int)Math.Min(remaining, thing.stackCount);
                if (take <= 0)
                    continue;
                output.Add(new PhysicalAllocation { thing = thing, count = take });
                remaining -= take;
            }

            if (remaining <= 0L)
                return true;
            output.RemoveRange(start, output.Count - start);
            return false;
        }

        private static bool IsCountedPhysicalResource(Map map, Thing thing)
        {
            if (map?.resourceCounter == null || thing == null)
                return false;
            if (ResourceCounterShouldCountMethod == null)
            {
                Log.ErrorOnce(
                    "[PMP ShipSupport] Could not resolve ResourceCounter.ShouldCount; physical payment is disabled.",
                    195260391);
                return false;
            }

            try
            {
                return (bool)ResourceCounterShouldCountMethod.Invoke(map.resourceCounter, new object[] { thing });
            }
            catch (Exception exception)
            {
                Log.ErrorOnce("[PMP ShipSupport] ResourceCounter.ShouldCount failed: " + exception, 195260392);
                return false;
            }
        }
    }
}
