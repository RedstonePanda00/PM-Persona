using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public class MapComponent_SupernovaField : MapComponent
    {
        private readonly List<Thing_Supernova> instances = new List<Thing_Supernova>(4);
        private readonly HashSet<Pawn> pendingPawns = new HashSet<Pawn>();
        private static readonly List<Pawn> tmpPendingSnapshot = new List<Pawn>(128);
        private static readonly List<Pawn> tmpPrune = new List<Pawn>(128);

        private int tickCounter;

        public MapComponent_SupernovaField(Map map)
            : base(map)
        {
        }

        public static MapComponent_SupernovaField Get(Map map)
        {
            if (map == null)
                return null;

            var existing = map.GetComponent<MapComponent_SupernovaField>();
            if (existing != null)
                return existing;

            existing = new MapComponent_SupernovaField(map);
            map.components.Add(existing);
            return existing;
        }

        public IReadOnlyList<Thing_Supernova> Instances => instances;

        // Targets remain visible when the source star is outside the camera, and fade after it expires.
        public override void MapComponentDraw()
        {
            SupernovaPlasmaDrawer.DrawForMap(map);
            Rendering.BuildingPlasmaRenderer.DrawForMap(map);
        }

        public void RegisterInstance(Thing_Supernova supernova)
        {
            if (supernova == null || instances.Contains(supernova))
                return;
            instances.Add(supernova);
        }

        public void UnregisterInstance(Thing_Supernova supernova)
        {
            if (supernova == null)
                return;
            instances.Remove(supernova);
            if (instances.Count == 0)
                pendingPawns.Clear();
        }

        public void AddToPendingPawns(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
                return;
            if (SupernovaFieldUtility.HasExposureHediff(pawn))
                return;
            pendingPawns.Add(pawn);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            PruneDeadInstances();

            if (instances.Count == 0)
            {
                pendingPawns.Clear();
                return;
            }

            tickCounter++;

            if (tickCounter % SupernovaConstants.MapScanIntervalTicks == 0)
                ScanMapIntoPending();

            if (tickCounter % SupernovaConstants.PendingProcessIntervalTicks == 0)
                ProcessPendingPawns();
        }

        private void PruneDeadInstances()
        {
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                Thing_Supernova sn = instances[i];
                if (sn == null || !sn.Spawned || sn.Destroyed)
                    instances.RemoveAt(i);
            }
        }

        private void ScanMapIntoPending()
        {
            Faction playerFaction = Faction.OfPlayer;
            HediffDef exposureDef = SupernovaFieldUtility.ExposureHediffDef;

            tmpPrune.Clear();
            foreach (Pawn pawn in pendingPawns)
            {
                if (ShouldRemoveFromPending(pawn, playerFaction, exposureDef))
                    tmpPrune.Add(pawn);
            }

            for (int i = 0; i < tmpPrune.Count; i++)
                pendingPawns.Remove(tmpPrune[i]);
            tmpPrune.Clear();

            IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < allPawns.Count; i++)
            {
                Pawn pawn = allPawns[i];
                if (!SupernovaFieldUtility.IsValidHostilePawn(pawn, playerFaction))
                    continue;
                if (exposureDef != null && pawn.health.hediffSet.HasHediff(exposureDef))
                    continue;
                if (!SupernovaFieldUtility.IsPawnInAnySupernovaField(pawn, instances))
                    continue;
                pendingPawns.Add(pawn);
            }

            DamageHostileBuildingsInField(playerFaction);
        }

        private static bool ShouldRemoveFromPending(Pawn pawn, Faction playerFaction, HediffDef exposureDef)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
                return true;
            if (!SupernovaFieldUtility.IsValidHostilePawn(pawn, playerFaction))
                return true;
            if (exposureDef != null && pawn.health.hediffSet.HasHediff(exposureDef))
                return true;
            return false;
        }

        private void ProcessPendingPawns()
        {
            HediffDef exposureDef = SupernovaFieldUtility.ExposureHediffDef;
            if (exposureDef == null)
                return;

            tmpPendingSnapshot.Clear();
            tmpPendingSnapshot.AddRange(pendingPawns);

            for (int i = 0; i < tmpPendingSnapshot.Count; i++)
            {
                Pawn pawn = tmpPendingSnapshot[i];
                if (pawn == null || pawn.Destroyed || pawn.Dead)
                {
                    pendingPawns.Remove(pawn);
                    continue;
                }

                if (SupernovaFieldUtility.GetExposureHediff(pawn) != null)
                {
                    pendingPawns.Remove(pawn);
                    continue;
                }

                if (!SupernovaFieldUtility.IsPawnInAnySupernovaField(pawn, instances))
                {
                    pendingPawns.Remove(pawn);
                    continue;
                }

                Thing_Supernova instigator = SupernovaFieldUtility.FindInstigatorForCell(pawn.Position, instances);
                pawn.health.AddHediff(exposureDef);
                SupernovaFieldUtility.ApplyBootstrapHits(pawn, instigator);
                pendingPawns.Remove(pawn);
            }

            tmpPendingSnapshot.Clear();
        }

        private void DamageHostileBuildingsInField(Faction playerFaction)
        {
            List<Thing> allThings = map.listerThings.AllThings;
            for (int i = 0; i < allThings.Count; i++)
            {
                Thing thing = allThings[i];
                if (!SupernovaFieldUtility.IsValidHostileBuilding(thing, playerFaction))
                    continue;
                if (!SupernovaFieldUtility.IsCellInAnySupernovaField(thing.Position, instances))
                    continue;

                Thing_Supernova instigator = SupernovaFieldUtility.FindInstigatorForCell(thing.Position, instances);
                SupernovaFieldUtility.ApplyVaporizeDamage(thing, instigator);
            }
        }
    }
}
