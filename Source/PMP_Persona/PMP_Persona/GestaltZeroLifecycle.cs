using System;
using System.Collections.Generic;
using HarmonyLib;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona.GestaltZero
{
    public class GestaltZeroLifecycleExtension : DefModExtension
    {
        public XenotypeDef forcedXenotype;
        public BodyTypeDef forcedBodyType;
        public GeneDef forcedSkinGene;
        public GeneDef forcedHairGene;
        public int returnDelayTicks = 18000;
        public ThingDef beaconDef;
    }

    internal static class GestaltZeroUtility
    {
        public const string LifecycleKey = "PMPH_GestaltZero";

        [ThreadStatic]
        private static Pawn interceptingPawn;

        [ThreadStatic]
        private static Pawn recoveringPawn;

        private static readonly AccessTools.FieldRef<Pawn_HealthTracker, PawnHealthState>
            HealthStateField =
                AccessTools.FieldRefAccess<Pawn_HealthTracker, PawnHealthState>("healthState");

        public static GestaltZeroLifecycleExtension ExtensionFor(Pawn pawn)
        {
            return pawn?.kindDef?.GetModExtension<GestaltZeroLifecycleExtension>();
        }

        public static bool IsZero(Pawn pawn)
        {
            return ExtensionFor(pawn) != null;
        }

        public static bool IsIntercepting(Pawn pawn)
        {
            return ReferenceEquals(interceptingPawn, pawn) || ReferenceEquals(recoveringPawn, pawn);
        }

        public static bool IsPooledZero(Pawn pawn)
        {
            if (pawn == null || Current.Game == null)
                return false;

            Pawn pooled = Current.Game.GetComponent<GameComponent_PersonaHeroPersistence>()
                ?.PeekPooledPawn(LifecycleKey, true);
            return ReferenceEquals(pooled, pawn);
        }

        public static void ApplyCanonicalAppearance(Pawn pawn, GestaltZeroLifecycleExtension extension = null)
        {
            extension = extension ?? ExtensionFor(pawn);
            if (pawn == null || extension == null)
                return;

            if (ModsConfig.BiotechActive && pawn.genes != null)
            {
                XenotypeDef xenotype = extension.forcedXenotype ??
                    DefDatabase<XenotypeDef>.GetNamedSilentFail("Baseliner");
                if (xenotype != null)
                    pawn.genes.SetXenotypeDirect(xenotype);

                SetEndogene(pawn, extension.forcedSkinGene, EndogeneCategory.Melanin);
                SetEndogene(pawn, extension.forcedHairGene, EndogeneCategory.HairColor);
            }

            if (pawn.story != null)
            {
                if (extension.forcedBodyType != null)
                    pawn.story.bodyType = extension.forcedBodyType;
                if (extension.forcedSkinGene?.skinColorBase != null)
                    pawn.story.SkinColorBase = extension.forcedSkinGene.skinColorBase.Value;
                if (extension.forcedHairGene?.hairColorOverride != null)
                    pawn.story.HairColor = extension.forcedHairGene.hairColorOverride.Value;
            }

            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }

        private static void SetEndogene(Pawn pawn, GeneDef desired, EndogeneCategory category)
        {
            if (desired == null || pawn.genes == null)
                return;

            List<Gene> genes = pawn.genes.GenesListForReading;
            for (int i = genes.Count - 1; i >= 0; i--)
            {
                Gene gene = genes[i];
                if (gene?.def == null || gene.def.endogeneCategory != category || gene.def == desired)
                    continue;
                pawn.genes.RemoveGene(gene);
            }

            if (!pawn.genes.HasEndogene(desired))
                pawn.genes.AddGene(desired, false);
        }

        public static bool TryInterceptDeath(Pawn pawn)
        {
            GestaltZeroLifecycleExtension extension = ExtensionFor(pawn);
            if (extension == null || pawn == null || pawn.Destroyed || pawn.Dead ||
                pawn.Faction != Faction.OfPlayer)
                return false;

            if (IsIntercepting(pawn))
                return true;

            GameComponent_GestaltZeroReturn component =
                Current.Game?.GetComponent<GameComponent_GestaltZeroReturn>();
            GameComponent_PersonaHeroPersistence persistence =
                Current.Game?.GetComponent<GameComponent_PersonaHeroPersistence>();
            if (component == null || persistence == null ||
                !component.CanBeginReturn(pawn) ||
                !persistence.CanStorePawn(LifecycleKey, pawn))
                return false;

            Map sourceMap = pawn.MapHeld;
            bool pooled = false;
            interceptingPawn = pawn;
            try
            {
                RestoreForReturn(pawn);
                DetachPawnFromWorld(pawn);
                if (!persistence.StorePawn(LifecycleKey, pawn))
                    return false;
                pooled = true;
                if (!component.BeginReturn(pawn, sourceMap, extension))
                    Log.Error("[PMP_Persona] Gestalt Zero was pooled but its return timer could not start.");
                return true;
            }
            catch (Exception exception)
            {
                Log.Error("[PMP_Persona] Gestalt Zero death interception failed: " + exception);
                return pooled;
            }
            finally
            {
                interceptingPawn = null;
            }
        }

        private static void DetachPawnFromWorld(Pawn pawn)
        {
            if (pawn.Spawned)
            {
                pawn.DeSpawn(DestroyMode.Vanish);
                return;
            }

            if (pawn.holdingOwner != null)
                pawn.holdingOwner.Remove(pawn);

            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);
        }

        public static void RestoreForReturn(Pawn pawn)
        {
            if (pawn?.health == null)
                return;

            Pawn previousRecoveringPawn = recoveringPawn;
            recoveringPawn = pawn;
            try
            {
                // A direct Pawn.Kill path can leave the private state at Dead even
                // though Harmony skipped the body. Make the repair explicit before
                // any Hediff notification is allowed to evaluate the pawn again.
                pawn.health.isBeingKilled = false;
                HealthStateField(pawn.health) = PawnHealthState.Mobile;

                // A previously leaked second Pawn.Kill may have executed
                // RemoveComponentsOnKilled. Recreate only missing trackers so
                // old saves and already-corrupted pooled Zero instances recover.
                PawnComponentsUtility.CreateInitialComponents(pawn);

                HediffSet set = pawn.health.hediffSet;
                if (set != null)
                {
                    var remove = new List<Hediff>();
                    var restore = new List<Hediff_MissingPart>();
                    List<Hediff> hediffs = set.hediffs;
                    for (int i = 0; i < hediffs.Count; i++)
                    {
                        Hediff hediff = hediffs[i];
                        if (hediff is Hediff_Injury)
                        {
                            remove.Add(hediff);
                            continue;
                        }

                        if (hediff is Hediff_MissingPart missing)
                        {
                            if (missing.Part != null &&
                                !set.PartOrAnyAncestorHasDirectlyAddedParts(missing.Part))
                                restore.Add(missing);
                            continue;
                        }

                        HediffDef def = hediff.def;
                        if (def == null || def.countsAsAddedPartOrImplant || def.IsAddiction)
                            continue;

                        if (!def.chronic && def.isBad &&
                            (def.everCurableByItem || hediff.IsLethal ||
                             hediff.IsAnyStageLifeThreatening()))
                        {
                            remove.Add(hediff);
                            continue;
                        }

                        if (def.chronic && def.lethalSeverity > 0f &&
                            hediff.Severity >= def.lethalSeverity)
                            hediff.Severity = Math.Max(def.minSeverity, def.lethalSeverity * 0.7f);
                    }

                    for (int i = 0; i < remove.Count; i++)
                        pawn.health.RemoveHediff(remove[i]);
                    for (int i = 0; i < restore.Count; i++)
                        pawn.health.RestorePart(restore[i].Part);

                    set.DirtyCache();
                    pawn.health.Notify_HediffChanged(null);
                }

                HealthStateField(pawn.health) = PawnHealthState.Mobile;
                pawn.health.forceDowned = false;
                pawn.health.isBeingKilled = false;

                pawn.jobs?.StopAll(false, false);
                if (pawn.mindState?.mentalStateHandler?.InMentalState == true)
                    pawn.mindState.mentalStateHandler.CurState?.RecoverFromState();
                pawn.stances?.CancelBusyStanceHard();
                pawn.stances?.stunner?.StopStun();
                if (pawn.stances != null)
                    pawn.stances.stagger = new StaggerHandler(pawn);

                if (pawn.needs?.AllNeeds != null)
                {
                    List<Need> needs = pawn.needs.AllNeeds;
                    for (int i = 0; i < needs.Count; i++)
                        needs[i].CurLevel = needs[i].MaxLevel;
                }

                if (pawn.abilities?.AllAbilitiesForReading != null)
                {
                    List<Ability> abilities = pawn.abilities.AllAbilitiesForReading;
                    for (int i = 0; i < abilities.Count; i++)
                        abilities[i]?.ResetCooldown();
                }

                try
                {
                    pawn.GetComp<ProtossMechShieldPawn>()?.RestoreShield();
                }
                catch
                {
                    // A broken optional shield comp must never strand Zero in the pool.
                }

                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }
            finally
            {
                recoveringPawn = previousRecoveringPawn;
            }
        }
    }

    public class GameComponent_GestaltZeroReturn : GameComponent
    {
        public const int ReconstructionEpochTicks = 150;
        public const int FinalReconstructionHoldTicks = 60;

        private bool active;
        private int returnAtTick = -1;
        private int targetMapUniqueId = -1;
        private int lifecycleToken;
        private Thing_GestaltZeroBeacon beacon;
        private bool finalSequenceActive;
        private int finalSequenceStartTick = -1;
        private bool migrationApplied;
        private int nextMaintenanceTick;

        public GameComponent_GestaltZeroReturn(Game game)
        {
        }

        public bool CanBeginReturn(Pawn pawn)
        {
            return !active && GestaltZeroUtility.IsZero(pawn);
        }

        public bool BeginReturn(Pawn pawn, Map sourceMap, GestaltZeroLifecycleExtension extension)
        {
            if (active || pawn == null || extension == null)
                return false;

            active = true;
            returnAtTick = (Find.TickManager?.TicksGame ?? 0) +
                Math.Max(1, extension.returnDelayTicks);
            targetMapUniqueId = sourceMap?.uniqueID ?? -1;
            lifecycleToken++;
            beacon = null;
            finalSequenceActive = false;
            finalSequenceStartTick = -1;

            Map targetMap = ResolveTargetMap();
            if (targetMap != null)
                EnsureBeacon(targetMap, extension.beaconDef);

            LookTargets target = beacon != null ? new LookTargets(beacon) : LookTargets.Invalid;
            try
            {
                Find.LetterStack?.ReceiveLetter(
                    "PMP.GestaltZero.DownLetter.Label".Translate(),
                    "PMP.GestaltZero.DownLetter.Text".Translate(),
                    LetterDefOf.ThreatBig,
                    target);
            }
            catch (Exception exception)
            {
                Log.Warning("[PMP_Persona] Could not send Gestalt Zero downed letter: " + exception.Message);
            }
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref active, "gestaltZeroReturnActive", false);
            Scribe_Values.Look(ref returnAtTick, "gestaltZeroReturnAtTick", -1);
            Scribe_Values.Look(ref targetMapUniqueId, "gestaltZeroTargetMapUniqueId", -1);
            Scribe_Values.Look(ref lifecycleToken, "gestaltZeroLifecycleToken", 0);
            Scribe_References.Look(ref beacon, "gestaltZeroBeacon");
            Scribe_Values.Look(ref finalSequenceActive,
                "gestaltZeroFinalSequenceActive", false);
            Scribe_Values.Look(ref finalSequenceStartTick,
                "gestaltZeroFinalSequenceStartTick", -1);
            Scribe_Values.Look(ref migrationApplied, "gestaltZeroMigrationApplied", false);
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            ApplyLegacyMigration();
            nextMaintenanceTick = 0;
        }

        public override void GameComponentTick()
        {
            if (!migrationApplied)
                ApplyLegacyMigration();
            if (!active || Find.TickManager == null)
                return;

            int now = Find.TickManager.TicksGame;
            if (now < nextMaintenanceTick)
                return;
            nextMaintenanceTick = now + 30;

            GameComponent_PersonaHeroPersistence persistence =
                Current.Game?.GetComponent<GameComponent_PersonaHeroPersistence>();
            Pawn pawn = persistence?.PeekPooledPawn(GestaltZeroUtility.LifecycleKey, true);
            if (pawn == null)
            {
                ClearState(true);
                Log.Error("[PMP_Persona] Gestalt Zero return state lost its pooled pawn; cancelling the timer.");
                return;
            }

            if (pawn.Dead)
            {
                Log.Warning("[PMP_Persona] Gestalt Zero entered the pool with Dead health state; repairing it.");
                GestaltZeroUtility.RestoreForReturn(pawn);
            }

            Map targetMap = ResolveTargetMap();
            if (targetMap == null)
                return;

            GestaltZeroLifecycleExtension extension = GestaltZeroUtility.ExtensionFor(pawn);
            if (!finalSequenceActive && now >= returnAtTick)
            {
                finalSequenceActive = true;
                finalSequenceStartTick = now;
                EnsureBeacon(targetMap, extension?.beaconDef);
                return;
            }

            if (finalSequenceActive)
            {
                if (finalSequenceStartTick < 0)
                    finalSequenceStartTick = now;

                int finalElapsed = Math.Max(0, now - finalSequenceStartTick);
                if (finalElapsed >= ReconstructionEpochTicks + FinalReconstructionHoldTicks)
                {
                    ReturnPawn(persistence, targetMap);
                    return;
                }

                EnsureBeacon(targetMap, extension?.beaconDef);
                return;
            }

            EnsureBeacon(targetMap, extension?.beaconDef);
        }

        public bool MatchesToken(int token)
        {
            return active && token == lifecycleToken;
        }

        public Pawn GetPooledPawn(int token)
        {
            if (!MatchesToken(token))
                return null;
            return Current.Game?.GetComponent<GameComponent_PersonaHeroPersistence>()
                ?.PeekPooledPawn(GestaltZeroUtility.LifecycleKey, true);
        }

        public int RemainingTicks(int token)
        {
            if (!MatchesToken(token) || Find.TickManager == null)
                return 0;
            return Math.Max(0, returnAtTick - Find.TickManager.TicksGame);
        }

        public float ReconstructionPhase(int token)
        {
            if (!MatchesToken(token) || Find.TickManager == null)
                return 0f;

            int now = Find.TickManager.TicksGame;
            if (finalSequenceActive)
            {
                int startTick = finalSequenceStartTick >= 0 ? finalSequenceStartTick : now;
                int elapsed = Math.Max(0, now - startTick);
                if (elapsed >= ReconstructionEpochTicks)
                    return 1f;
                return Mathf.Clamp01((float)elapsed / ReconstructionEpochTicks);
            }

            int epochTick = now % ReconstructionEpochTicks;
            if (epochTick < 0)
                epochTick += ReconstructionEpochTicks;
            return (float)epochTick / ReconstructionEpochTicks;
        }

        public void DevCompleteNow(int token)
        {
            if (!MatchesToken(token))
                return;
            returnAtTick = Find.TickManager?.TicksGame ?? 0;
            nextMaintenanceTick = 0;
        }

        private void ReturnPawn(GameComponent_PersonaHeroPersistence persistence, Map map)
        {
            IntVec3 cell = IntVec3.Invalid;
            if (beacon != null && !beacon.Destroyed && beacon.Spawned && beacon.Map == map &&
                IsUsableCell(beacon.Position, map, beacon))
                cell = beacon.Position;
            if (!cell.IsValid && !TryFindUsableCell(map, out cell))
                return;

            Thing_GestaltZeroBeacon oldBeacon = beacon;
            beacon = null;
            ForceRemoveBeacon(oldBeacon);

            Pawn pawn = persistence.TakePooledPawn(GestaltZeroUtility.LifecycleKey, true);
            if (pawn == null)
            {
                ClearState(false);
                return;
            }

            try
            {
                GestaltZeroUtility.RestoreForReturn(pawn);
                GestaltZeroReconstructionRenderer.RemoveCoreReconstructionVisual(pawn);
                GenSpawn.Spawn(pawn, cell, map, WipeMode.VanishOrMoveAside);
                // SpawnSetup recreates spawn-only trackers removed by DeSpawn.
                GestaltZeroUtility.RestoreForReturn(pawn);
            }
            catch (Exception exception)
            {
                persistence.StorePawn(GestaltZeroUtility.LifecycleKey, pawn);
                Log.Error("[PMP_Persona] Gestalt Zero return spawn failed; retrying: " + exception);
                return;
            }
            pawn.Rotation = Rot4.South;
            active = false;
            returnAtTick = -1;
            targetMapUniqueId = -1;
            finalSequenceActive = false;
            finalSequenceStartTick = -1;

            try
            {
                Find.LetterStack?.ReceiveLetter(
                    "PMP.GestaltZero.ReadyLetter.Label".Translate(),
                    "PMP.GestaltZero.ReadyLetter.Text".Translate(),
                    LetterDefOf.PositiveEvent,
                    new LookTargets(pawn));
            }
            catch (Exception exception)
            {
                Log.Warning("[PMP_Persona] Could not send Gestalt Zero ready letter: " + exception.Message);
            }
        }

        private void EnsureBeacon(Map map, ThingDef beaconDef)
        {
            if (map == null)
                return;

            if (beacon != null && !beacon.Destroyed && beacon.Spawned && beacon.Map == map)
                return;

            ForceRemoveBeacon(beacon);
            beacon = null;

            beaconDef = beaconDef ?? DefDatabase<ThingDef>.GetNamedSilentFail("PMPH_GestaltZeroBeacon");
            if (beaconDef == null || !TryFindUsableCell(map, out IntVec3 cell))
                return;

            beacon = ThingMaker.MakeThing(beaconDef) as Thing_GestaltZeroBeacon;
            if (beacon == null)
                return;
            beacon.Configure(lifecycleToken);
            GenSpawn.Spawn(beacon, cell, map, WipeMode.VanishOrMoveAside);
            targetMapUniqueId = map.uniqueID;
        }

        private Map ResolveTargetMap()
        {
            List<Map> maps = Find.Maps;
            if (maps == null || maps.Count == 0)
                return null;

            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i]?.uniqueID == targetMapUniqueId)
                    return maps[i];
            }

            Map best = PickBestMap(maps, true) ?? PickBestMap(maps, false);
            if (best != null)
                targetMapUniqueId = best.uniqueID;
            return best;
        }

        private static Map PickBestMap(List<Map> maps, bool requirePlayerHome)
        {
            Map best = null;
            int bestCount = -1;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map == null || (requirePlayerHome && !map.IsPlayerHome))
                    continue;

                int count = 0;
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int p = 0; p < pawns.Count; p++)
                {
                    if (pawns[p]?.Faction == Faction.OfPlayer)
                        count++;
                }
                if (!requirePlayerHome && count == 0)
                    continue;

                if (best == null || count > bestCount ||
                    (count == bestCount && map.uniqueID < best.uniqueID))
                {
                    best = map;
                    bestCount = count;
                }
            }
            return best;
        }

        private static bool TryFindUsableCell(Map map, out IntVec3 result)
        {
            result = IntVec3.Invalid;
            if (map == null)
                return false;

            int bestDistance = int.MaxValue;
            IntVec3 center = map.Center;
            for (int z = 0; z < map.Size.z; z++)
            {
                for (int x = 0; x < map.Size.x; x++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!IsUsableCell(cell, map))
                        continue;
                    int distance = Math.Abs(x - center.x) + Math.Abs(z - center.z);
                    if (distance >= bestDistance)
                        continue;
                    bestDistance = distance;
                    result = cell;
                }
            }
            return result.IsValid;
        }

        private static bool IsUsableCell(IntVec3 cell, Map map, Thing ignoredThing = null)
        {
            if (!cell.InBounds(map) || !cell.Standable(map) || cell.Fogged(map) ||
                cell.GetEdifice(map) != null || cell.GetFirstPawn(map) != null ||
                cell.GetFirstThing<Fire>(map) != null)
                return false;

            List<Thing> things = cell.GetThingList(map);
            for (int i = 0; i < things.Count; i++)
            {
                Thing thing = things[i];
                if (thing == null || ReferenceEquals(thing, ignoredThing))
                    continue;
                ThingCategory category = thing.def.category;
                if (category == ThingCategory.Item || category == ThingCategory.Building ||
                    category == ThingCategory.Plant || category == ThingCategory.Pawn)
                    return false;
            }
            return true;
        }

        private void ApplyLegacyMigration()
        {
            if (migrationApplied)
                return;
            migrationApplied = true;

            try
            {
                foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction)
                {
                    if (GestaltZeroUtility.IsZero(pawn))
                        GestaltZeroUtility.ApplyCanonicalAppearance(pawn);
                }

                Pawn pooled = Current.Game?.GetComponent<GameComponent_PersonaHeroPersistence>()
                    ?.PeekPooledPawn(GestaltZeroUtility.LifecycleKey, true);
                if (pooled != null)
                    GestaltZeroUtility.ApplyCanonicalAppearance(pooled);
            }
            catch (Exception exception)
            {
                Log.Warning("[PMP_Persona] Gestalt Zero old-save migration failed: " + exception.Message);
            }
        }

        private void ClearState(bool destroyBeacon)
        {
            Thing_GestaltZeroBeacon oldBeacon = beacon;
            beacon = null;
            active = false;
            returnAtTick = -1;
            targetMapUniqueId = -1;
            finalSequenceActive = false;
            finalSequenceStartTick = -1;
            if (destroyBeacon)
                ForceRemoveBeacon(oldBeacon);
        }

        private static void ForceRemoveBeacon(Thing beaconToRemove)
        {
            if (beaconToRemove == null || beaconToRemove.Destroyed)
                return;

            bool previous = Thing.allowDestroyNonDestroyable;
            try
            {
                Thing.allowDestroyNonDestroyable = true;
                beaconToRemove.Destroy(DestroyMode.Vanish);
            }
            finally
            {
                Thing.allowDestroyNonDestroyable = previous;
            }
        }
    }
}
