using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.RecomposerSupport
{
    public sealed class RecomposerVisualConfig : DefModExtension
    {
        public int fieldTicks = 48, scanTicks = 60, disassemblyTicks = 90, coreTicks = 42, assemblyTicks = 90, activationTicks = 30;
        public float coreShake = .10f;
    }

    public sealed class RecomposerSubject : IExposable
    {
        public Pawn original, replacement;
        public IntVec3 cell;
        public Rot4 facing;
        public PawnPosture posture;
        public bool completed, gearTransferred;
        public List<Apparel> droppedApparel = new List<Apparel>();
        public void ExposeData()
        {
            Scribe_References.Look(ref original, "original"); Scribe_References.Look(ref replacement, "replacement");
            Scribe_Values.Look(ref cell, "cell"); Scribe_Values.Look(ref facing, "facing");
            Scribe_Values.Look(ref posture, "posture"); Scribe_Values.Look(ref completed, "completed");
            Scribe_Values.Look(ref gearTransferred, "gearTransferred");
            Scribe_Collections.Look(ref droppedApparel, "droppedApparel", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) droppedApparel ??= new List<Apparel>();
        }
    }

    // Both bodies are suspended until the visual hand-off is complete; no second gameplay pawn can act early.
    public class Thing_RecomposerMarker : ThingWithComps, ISuspendableThingHolder
    {
        private ThingOwner<Pawn> contents;
        internal List<RecomposerSubject> Subjects = new List<RecomposerSubject>();
        private int age;
        private bool locked, finished;
        internal RecomposerVisualConfig Config => def.GetModExtension<RecomposerVisualConfig>() ?? new RecomposerVisualConfig();
        internal int Age => age;
        internal int LockTick => Math.Max(1, Config.fieldTicks) + Math.Max(1, Config.scanTicks);
        internal int CoreTick => LockTick + Math.Max(1, Config.disassemblyTicks);
        internal int AssemblyTick => CoreTick + Math.Max(1, Config.coreTicks);
        internal int ActivationTick => AssemblyTick + Math.Max(1, Config.assemblyTicks);
        internal int EndTick => ActivationTick + Math.Max(1, Config.activationTicks);
        internal bool Locked => locked;
        public bool IsContentsSuspended => true;
        public Thing_RecomposerMarker() { contents = new ThingOwner<Pawn>(this, false, LookMode.Deep); }
        public void Configure(IntVec3 target, Map map) { age = 0; }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || finished) return;
            age++;
            if (!locked && age >= LockTick) LockTargets();
            try
            {
                // Original clothing, armor and weapons stay with the reconstructed body.
                if (age >= CoreTick)
                    foreach (var entry in Subjects)
                    {
                        if (entry.gearTransferred) continue;
                        entry.gearTransferred = true; // Rollback also covers a partial third-party equipment transfer.
                        RecomposerPawnUtility.TransferPortableThings(entry.original, entry.replacement, Map, entry.cell);
                        RecomposerVisual.ForgetBody(this, entry.replacement);
                    }
                if (age < EndTick) return;
                RecomposerPawnUtility.CompleteCaptured(Map, Subjects);
                finished = Subjects.All(s => s.completed);
                if (finished) Destroy();
            }
            catch (Exception e)
            {
                Log.Error("[PMP Recomposer] Conversion interrupted; returning remaining originals: " + e);
                Destroy();
            }
        }

        private void LockTargets()
        {
            locked = true;
            RecomposerVisual.Release(this); // Sample the exact facing/apparel at lock, not an earlier scan pose.
            foreach (var pawn in RecomposerPawnUtility.Targets(Map, Position))
            {
                var entry = new RecomposerSubject { original = pawn, cell = pawn.Position, facing = pawn.Rotation, posture = pawn.GetPosture() };
                try
                {
                    entry.replacement = RecomposerPawnUtility.PrepareReplacement(pawn);
                    if (!contents.TryAdd(entry.replacement, false)) throw new InvalidOperationException("Replacement holder rejected pawn");
                    pawn.DeSpawn();
                    if (!contents.TryAdd(pawn, false)) throw new InvalidOperationException("Original holder rejected pawn");
                    Subjects.Add(entry);
                }
                catch (Exception e)
                {
                    if (entry.replacement != null) RecomposerVanillaBypass.ForceDiscard(entry.replacement);
                    if (!pawn.Spawned && !pawn.Discarded)
                    { pawn.holdingOwner?.Remove(pawn); GenSpawn.Spawn(pawn, entry.cell, Map, entry.facing); }
                    Log.Warning("[PMP Recomposer] Target left unchanged: " + e);
                }
            }
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            if (Map != Find.CurrentMap) return;
            if (phase == DrawPhase.EnsureInitialized) RecomposerVisual.Prepare(this);
            else if (phase == DrawPhase.Draw) RecomposerVisual.Draw(this);
        }
        protected override void DrawAt(Vector3 drawLoc, bool flip = false) => RecomposerVisual.Draw(this);
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed) { absorbed = true; }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // Cancellation/debug deletion rolls back uncommitted targets instead of silently erasing them.
            if (contents.Count > 0 && Map == null) return;
            foreach (var entry in Subjects)
            {
                if (entry.completed) continue;
                var original = entry.original;
                if (original != null && original.Discarded && entry.replacement != null)
                {
                    var restored = entry.replacement;
                    restored.holdingOwner?.Remove(restored);
                    if (!restored.Spawned && !RecomposerPawnUtility.TrySpawnSentinel(restored, Map, entry.cell, entry.facing))
                        Map.GetComponent<MapComponent_RecomposerQueue>().Enqueue(restored, entry.cell, entry.facing);
                    entry.completed = true;
                    continue;
                }
                if (entry.gearTransferred && original != null && !original.Destroyed && entry.replacement != null)
                {
                    RecomposerPawnUtility.TransferPortableThings(entry.replacement, original, Map, entry.cell);
                    foreach (var apparel in entry.droppedApparel)
                        if (apparel != null && !apparel.Destroyed && apparel.Spawned && apparel.Map == Map && original.apparel != null)
                        { apparel.DeSpawn(); original.apparel.Wear(apparel); }
                }
                if (original != null && !original.Discarded && !original.Destroyed && !original.Spawned)
                {
                    original.holdingOwner?.Remove(original);
                    GenSpawn.Spawn(original, entry.cell, Map, entry.facing);
                }
                if (entry.replacement != null && !entry.replacement.Spawned)
                    RecomposerVanillaBypass.ForceDiscard(entry.replacement);
            }
            RecomposerVisual.Release(this);
            base.Destroy(mode);
        }
        public ThingOwner GetDirectlyHeldThings() => contents;
        public void GetChildHolders(List<IThingHolder> children) => ThingOwnerUtility.AppendThingHoldersFromThings(children, contents);
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref contents, "recomposerContents", this);
            Scribe_Collections.Look(ref Subjects, "recomposerSubjects", LookMode.Deep);
            Scribe_Values.Look(ref age, "recomposerAge"); Scribe_Values.Look(ref locked, "recomposerLocked");
            Scribe_Values.Look(ref finished, "recomposerFinished");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            { contents ??= new ThingOwner<Pawn>(this, false, LookMode.Deep); Subjects ??= new List<RecomposerSubject>(); }
        }
    }
}
