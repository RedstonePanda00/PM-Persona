using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PMP_Persona.RecomposerSupport
{
    public class MapComponent_RecomposerQueue : MapComponent, IThingHolder
    {
        private ThingOwner<Pawn> pendingPawns;
        private List<IntVec3> targetCells = new List<IntVec3>();
        private List<Rot4> rotations = new List<Rot4>();
        private List<int> attempts = new List<int>();
        private List<int> nextAttemptTicks = new List<int>();

        public MapComponent_RecomposerQueue(Map map) : base(map)
        {
            pendingPawns = new ThingOwner<Pawn>(this, false, LookMode.Deep);
        }

        public void Enqueue(Pawn pawn, IntVec3 targetCell, Rot4 rotation)
        {
            if (pawn == null)
                return;
            if (!pendingPawns.TryAdd(pawn))
            {
                Log.Error("[PMP Recomposer] Could not queue generated sentinel " + pawn.ToStringSafe());
                RecomposerVanillaBypass.ForceDiscard(pawn);
                return;
            }

            targetCells.Add(targetCell);
            rotations.Add(rotation);
            attempts.Add(0);
            nextAttemptTicks.Add(Find.TickManager.TicksGame + RecomposerConstants.RetryIntervalTicks);
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            int now = Find.TickManager.TicksGame;
            for (int i = pendingPawns.Count - 1; i >= 0; i--)
            {
                if (i >= nextAttemptTicks.Count || now < nextAttemptTicks[i])
                    continue;
                RetryAt(i, now);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref pendingPawns, "pmpRecomposerPendingPawns", this);
            Scribe_Collections.Look(ref targetCells, "pmpRecomposerTargetCells", LookMode.Value);
            Scribe_Collections.Look(ref rotations, "pmpRecomposerRotations", LookMode.Value);
            Scribe_Collections.Look(ref attempts, "pmpRecomposerAttempts", LookMode.Value);
            Scribe_Collections.Look(ref nextAttemptTicks, "pmpRecomposerNextAttemptTicks", LookMode.Value);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pendingPawns == null)
                    pendingPawns = new ThingOwner<Pawn>(this, false, LookMode.Deep);
                if (targetCells == null)
                    targetCells = new List<IntVec3>();
                if (rotations == null)
                    rotations = new List<Rot4>();
                if (attempts == null)
                    attempts = new List<int>();
                if (nextAttemptTicks == null)
                    nextAttemptTicks = new List<int>();
                NormalizeLists();
            }
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            return pendingPawns;
        }

        public IThingHolder ParentHolder => null;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, pendingPawns);
        }

        private void RetryAt(int index, int now)
        {
            Pawn pawn = pendingPawns[index];
            IntVec3 cell = targetCells[index];
            Rot4 rotation = rotations[index];
            int attempt = attempts[index] + 1;

            pendingPawns.Remove(pawn);
            RemoveMetadataAt(index);

            if (RecomposerPawnUtility.TrySpawnSentinel(pawn, map, cell, rotation))
                return;

            if (attempt >= RecomposerConstants.MaxQueuedAttempts)
            {
                Log.Error("[PMP Recomposer] Sentinel spawn failed after " + attempt +
                          " queued attempts at " + cell + " on map " + map.uniqueID + ". Discarding generated sentinel.");
                RecomposerVanillaBypass.ForceDiscard(pawn);
                return;
            }

            if (!pendingPawns.TryAdd(pawn))
            {
                Log.Error("[PMP Recomposer] Could not requeue generated sentinel " + pawn.ToStringSafe());
                RecomposerVanillaBypass.ForceDiscard(pawn);
                return;
            }

            targetCells.Add(cell);
            rotations.Add(rotation);
            attempts.Add(attempt);
            nextAttemptTicks.Add(now + RecomposerConstants.RetryIntervalTicks);
        }

        private void NormalizeLists()
        {
            int count = pendingPawns.Count;
            Trim(targetCells, count);
            Trim(rotations, count);
            Trim(attempts, count);
            Trim(nextAttemptTicks, count);
            while (targetCells.Count < count) targetCells.Add(map.Center);
            while (rotations.Count < count) rotations.Add(Rot4.South);
            while (attempts.Count < count) attempts.Add(0);
            while (nextAttemptTicks.Count < count)
                nextAttemptTicks.Add(Find.TickManager.TicksGame + RecomposerConstants.RetryIntervalTicks);
        }

        private void RemoveMetadataAt(int index)
        {
            targetCells.RemoveAt(index);
            rotations.RemoveAt(index);
            attempts.RemoveAt(index);
            nextAttemptTicks.RemoveAt(index);
        }

        private static void Trim<T>(List<T> list, int count)
        {
            if (list.Count > count)
                list.RemoveRange(count, list.Count - count);
        }
    }
}
