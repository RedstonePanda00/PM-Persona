using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RsPandaLibrary;
using Verse;

namespace PMP_Persona
{
    // Load pooled-pawn components before World/FactionManager; defer player-faction queries.
    [HarmonyPatch(typeof(Pawn_NeedsTracker), "ShouldHaveNeed")]
    internal static class PooledHeroNeedsDuringLoad
    {
        private static bool Prefix(Pawn ___pawn, ref bool __result)
        {
            if (Scribe.mode != LoadSaveMode.LoadingVars || Current.Game?.World != null ||
                ___pawn?.def?.HasModExtension<PersonaHeroLifecycleExtension>() != true) return true;
            __result = false; return false;
        }
    }
    public class PersonaHeroLifecycleExtension : DefModExtension
    {
    }

    public class PersonaHeroSnapshot : IExposable
    {
        private Name name;
        private Dictionary<RecordDef, float> records = new Dictionary<RecordDef, float>();
        private List<RecordDef> tmpRecordDefs;
        private List<float> tmpRecordValues;

        public static PersonaHeroSnapshot Capture(Pawn pawn)
        {
            var snapshot = new PersonaHeroSnapshot
            {
                name = pawn?.Name
            };

            if (pawn?.records == null)
                return snapshot;

            foreach (RecordDef recordDef in DefDatabase<RecordDef>.AllDefsListForReading)
            {
                if (recordDef == null)
                    continue;

                float value = pawn.records.GetValue(recordDef);
                if (Math.Abs(value) > 0.0001f)
                    snapshot.records[recordDef] = value;
            }

            return snapshot;
        }

        public void ApplyTo(Pawn pawn)
        {
            if (pawn == null)
                return;

            if (name != null)
                pawn.Name = name;

            if (pawn.records == null || records == null)
                return;

            foreach (KeyValuePair<RecordDef, float> entry in records)
            {
                if (entry.Key == null)
                    continue;

                float current = pawn.records.GetValue(entry.Key);
                float difference = entry.Value - current;
                if (Math.Abs(difference) > 0.0001f)
                    pawn.records.AddTo(entry.Key, difference);
            }
        }

        public void ExposeData()
        {
            Scribe_Deep.Look(ref name, "name");
            Scribe_Collections.Look(ref records, "records", LookMode.Def, LookMode.Value,
                ref tmpRecordDefs, ref tmpRecordValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && records == null)
                records = new Dictionary<RecordDef, float>();
        }
    }

    public class GameComponent_PersonaHeroPersistence : GameComponent
    {
        private Dictionary<string, Pawn> pooledPawns =
            new Dictionary<string, Pawn>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, PersonaHeroSnapshot> snapshots =
            new Dictionary<string, PersonaHeroSnapshot>(StringComparer.OrdinalIgnoreCase);

        private List<string> tmpPooledKeys;
        private List<Pawn> tmpPooledValues;
        private List<string> tmpSnapshotKeys;
        private List<PersonaHeroSnapshot> tmpSnapshotValues;

        public GameComponent_PersonaHeroPersistence(Game game)
        {
        }

        public bool HasStoredPersona(string defName) => snapshots.ContainsKey(defName) ||
            (pooledPawns.TryGetValue(defName, out Pawn pawn) && pawn != null && !pawn.Destroyed);

        public bool CanStorePawn(string targetDefName, Pawn pawn)
        {
            if (string.IsNullOrEmpty(targetDefName) || pawn == null || pawn.Destroyed)
                return false;

            if (!pooledPawns.TryGetValue(targetDefName, out Pawn existing))
                return true;

            return existing == null || existing.Destroyed || existing.Dead || ReferenceEquals(existing, pawn);
        }

        public bool StorePawn(string targetDefName, Pawn pawn)
        {
            if (!CanStorePawn(targetDefName, pawn))
            {
                Log.Error($"[PMP_Persona] Refusing to overwrite a valid pooled hero for '{targetDefName}'.");
                return false;
            }

            pooledPawns[targetDefName] = pawn;
            snapshots.Remove(targetDefName);
            return true;
        }

        public Pawn TakePooledPawn(string targetDefName, bool allowDead = false)
        {
            if (string.IsNullOrEmpty(targetDefName) ||
                !pooledPawns.TryGetValue(targetDefName, out Pawn pawn))
                return null;

            pooledPawns.Remove(targetDefName);
            if (pawn == null || pawn.Destroyed || (!allowDead && pawn.Dead))
                return null;

            return pawn;
        }

        public Pawn PeekPooledPawn(string targetDefName, bool allowDead = false)
        {
            if (string.IsNullOrEmpty(targetDefName) ||
                !pooledPawns.TryGetValue(targetDefName, out Pawn pawn))
                return null;

            if (pawn == null || pawn.Destroyed || (!allowDead && pawn.Dead))
            {
                pooledPawns.Remove(targetDefName);
                return null;
            }

            return pawn;
        }

        public void CaptureSnapshot(string targetDefName, Pawn pawn)
        {
            if (string.IsNullOrEmpty(targetDefName) || pawn == null)
                return;

            if (pooledPawns.TryGetValue(targetDefName, out Pawn pooled) && ReferenceEquals(pooled, pawn))
                pooledPawns.Remove(targetDefName);

            snapshots[targetDefName] = PersonaHeroSnapshot.Capture(pawn);
        }

        public bool ApplyAndConsumeSnapshot(string targetDefName, Pawn pawn)
        {
            if (string.IsNullOrEmpty(targetDefName) || pawn == null ||
                !snapshots.TryGetValue(targetDefName, out PersonaHeroSnapshot snapshot) || snapshot == null)
                return false;

            snapshots.Remove(targetDefName);
            snapshot.ApplyTo(pawn);
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pooledPawns, "pooledPawns", LookMode.Value, LookMode.Deep,
                ref tmpPooledKeys, ref tmpPooledValues);
            Scribe_Collections.Look(ref snapshots, "snapshots", LookMode.Value, LookMode.Deep,
                ref tmpSnapshotKeys, ref tmpSnapshotValues);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (pooledPawns == null)
                    pooledPawns = new Dictionary<string, Pawn>(StringComparer.OrdinalIgnoreCase);
                if (snapshots == null)
                    snapshots = new Dictionary<string, PersonaHeroSnapshot>(StringComparer.OrdinalIgnoreCase);
                CleanupInvalidPawns();
            }
        }

        private void CleanupInvalidPawns()
        {
            if (pooledPawns == null || pooledPawns.Count == 0)
                return;

            var invalidKeys = new List<string>();
            foreach (KeyValuePair<string, Pawn> entry in pooledPawns)
            {
                // Allow lifecycle owners to inspect dead pooled pawns for health-state repair.
                if (entry.Value == null || entry.Value.Destroyed)
                    invalidKeys.Add(entry.Key);
            }

            for (int i = 0; i < invalidKeys.Count; i++)
                pooledPawns.Remove(invalidKeys[i]);
        }
    }

    internal sealed class PersonaHeroAutoTransformExtension : IAutoTransformExtension
    {
        public Pawn TryTakeStoredPawn(AutoTransformContext context)
        {
            return Current.Game?.GetComponent<GameComponent_PersonaHeroPersistence>()
                ?.TakePooledPawn(context.TargetDef.defName);
        }

        public void PreparePawnForActivation(AutoTransformContext context, Pawn pawn, bool reusedStoredPawn)
        {
            if (pawn == null)
                return;

            if (reusedStoredPawn)
            {
                if (pawn.Spawned && pawn.Map != null)
                    pawn.DeSpawn(DestroyMode.Vanish);
                StabilizePooledPawn(pawn);
                return;
            }

            Current.Game?.GetComponent<GameComponent_PersonaHeroPersistence>()
                ?.ApplyAndConsumeSnapshot(context.TargetDef.defName, pawn);
        }

        public bool TryHandleKill(Pawn pawn, DamageInfo? dinfo, Hediff exactCulprit)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead || pawn.Faction != Faction.OfPlayer ||
                pawn.def?.defName == null)
                return false;

            GameComponent_PersonaHeroPersistence persistence =
                Current.Game?.GetComponent<GameComponent_PersonaHeroPersistence>();
            if (persistence == null || !persistence.CanStorePawn(pawn.def.defName, pawn))
                return false;

            StabilizePooledPawn(pawn);
            if (pawn.Spawned && pawn.Map != null)
                pawn.DeSpawn(DestroyMode.Vanish);

            return persistence.StorePawn(pawn.def.defName, pawn);
        }

        public void NotifyBeforeRemoval(Pawn pawn, AutoTransformRemovalReason reason)
        {
            if (pawn == null || pawn.Faction != Faction.OfPlayer || pawn.def?.defName == null)
                return;

            Current.Game?.GetComponent<GameComponent_PersonaHeroPersistence>()
                ?.CaptureSnapshot(pawn.def.defName, pawn);
        }

        private static void StabilizePooledPawn(Pawn pawn)
        {
            HediffSet set = pawn?.health?.hediffSet;
            if (set?.hediffs == null)
                return;

            var injuries = new List<Hediff_Injury>();
            var missingParts = new List<Hediff_MissingPart>();
            for (int i = 0; i < set.hediffs.Count; i++)
            {
                if (set.hediffs[i] is Hediff_Injury injury)
                    injuries.Add(injury);
                else if (set.hediffs[i] is Hediff_MissingPart missingPart)
                    missingParts.Add(missingPart);
            }

            for (int i = 0; i < injuries.Count; i++)
                pawn.health.RemoveHediff(injuries[i]);

            for (int i = 0; i < missingParts.Count; i++)
            {
                if (missingParts[i]?.Part != null)
                    pawn.health.RestorePart(missingParts[i].Part);
            }

            set.DirtyCache();
        }
    }

    [StaticConstructorOnStartup]
    public static class PersonaHeroLifecycleBootstrap
    {
        private static readonly PersonaHeroAutoTransformExtension Handler =
            new PersonaHeroAutoTransformExtension();

        static PersonaHeroLifecycleBootstrap()
        {
            foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (thingDef?.race == null ||
                    thingDef.GetModExtension<PersonaHeroLifecycleExtension>() == null)
                    continue;

                GameComponent_AutoTransformCoordinator.RegisterExtension(thingDef.defName, Handler);
            }
        }

        public static bool IsManagedHero(Pawn pawn)
        {
            return pawn?.def?.GetModExtension<PersonaHeroLifecycleExtension>() != null;
        }
    }
}
