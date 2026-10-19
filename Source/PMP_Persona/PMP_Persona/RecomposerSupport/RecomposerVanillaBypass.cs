using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace PMP_Persona.RecomposerSupport
{
    public static class RecomposerVanillaBypass
    {
        private static readonly FieldInfo ThingStateField =
            typeof(Thing).GetField("mapIndexOrState", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool PermanentlyRemove(Pawn pawn)
        {
            if (pawn == null)
                return true;

            Map oldMap = pawn.Map;
            if (pawn.Spawned)
            {
                try
                {
                    pawn.DeSpawn(DestroyMode.Vanish);
                }
                catch (Exception e)
                {
                    Log.Warning("[PMP Recomposer] Patched Pawn.DeSpawn threw for " + pawn.ToStringSafe() + ": " + e);
                }
            }

            if (pawn.Spawned)
                EmergencyDetachFromMap(pawn, oldMap);

            if (Find.WorldPawns != null && Find.WorldPawns.Contains(pawn))
                Find.WorldPawns.RemovePawn(pawn);

            ForceDiscard(pawn);
            return pawn.Discarded;
        }

        public static void ForceDiscard(Pawn pawn)
        {
            if (pawn == null || pawn.Discarded)
                return;

            try
            {
                pawn.relations?.ClearAllRelations();
                pawn.pather?.DisposeAndClearCurPathRequest();
                pawn.pather?.DisposeAndClearCurPath();

                if (Current.ProgramState == ProgramState.Playing)
                {
                    Find.PlayLog?.Notify_PawnDiscarded(pawn, true);
                    Find.BattleLog?.Notify_PawnDiscarded(pawn, true);
                    Find.TaleManager?.Notify_PawnDiscarded(pawn, true);
                    Find.QuestManager?.Notify_PawnDiscarded(pawn);
                }

                List<Pawn> living = PawnsFinder.AllMapsWorldAndTemporary_Alive;
                for (int i = 0; i < living.Count; i++)
                    living[i].needs?.mood?.thoughts?.memories?.Notify_PawnDiscarded(pawn);

                Corpse.PostCorpseDestroy(pawn, true);

                if (pawn.holdingOwner != null)
                    pawn.holdingOwner.Remove(pawn);

                if (ThingStateField == null)
                    throw new MissingFieldException(typeof(Thing).FullName, "mapIndexOrState");

                // Thing.Discard only transitions the internal state from unspawned (-2)
                // to discarded (-3). Pawn-specific reference cleanup is performed above.
                ThingStateField.SetValue(pawn, (sbyte)-3);
            }
            catch (Exception e)
            {
                Log.Error("[PMP Recomposer] ForceDiscard failed for " + pawn.ToStringSafe() + ": " + e);
            }
        }

        private static void EmergencyDetachFromMap(Pawn pawn, Map map)
        {
            Log.Error("[PMP Recomposer] Entering emergency map-detach fallback for " + pawn.ToStringSafe());
            if (map == null)
                return;

            TryStep(() => map.overlayDrawer.DisposeHandle(pawn));
            TryStep(() => RegionListersUpdater.DeregisterInRegions(pawn, map));
            TryStep(() => map.spawnedThings.Remove(pawn));
            TryStep(() => map.listerThings.Remove(pawn));
            TryStep(() => map.thingGrid.Deregister(pawn));
            TryStep(() => map.coverGrid.DeRegister(pawn));
            TryStep(() => map.tooltipGiverList.Notify_ThingDespawned(pawn));
            TryStep(() => Find.Selector.Deselect(pawn));
            TryStep(() => map.dynamicDrawManager.DeRegisterDrawable(pawn));
            TryStep(() => Find.TickManager.DeRegisterAllTickabilityFor(pawn));
            TryStep(() => map.attackTargetsCache.Notify_ThingDespawned(pawn));
            TryStep(() => map.physicalInteractionReservationManager.ReleaseAllForTarget(pawn));
            TryStep(() => pawn.jobs?.StopAll());
            TryStep(() => pawn.pather?.StopDead());
            TryStep(() => pawn.roping?.Notify_DeSpawned());
            TryStep(delegate
            {
                if (pawn.mindState != null)
                    pawn.mindState.droppedWeapon = null;
            });
            TryStep(() => pawn.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty());
            TryStep(() => pawn.meleeVerbs?.Notify_PawnDespawned());
            TryStep(() => pawn.mechanitor?.Notify_DeSpawned(DestroyMode.Vanish));
            TryStep(() => MeditationFocusTypeAvailabilityCache.Notify_PawnDiedOrDestroyed(pawn));
            TryStep(() => pawn.ClearAllReservations(false));
            TryStep(delegate
            {
                if (map.mapPawns.AllPawnsSpawned.Contains(pawn))
                {
                    map.mapPawns.DeRegisterPawn(pawn);
                    map.autoSlaughterManager.Notify_PawnDespawned();
                }
            });
            TryStep(() => PawnComponentsUtility.RemoveComponentsOnDespawned(pawn));
            List<ThingComp> comps = pawn.AllComps;
            if (comps != null)
            {
                for (int i = 0; i < comps.Count; i++)
                {
                    ThingComp comp = comps[i];
                    TryStep(() => comp.PostDeSpawn(map, DestroyMode.Vanish));
                }
            }
            TryStep(() => ThingStateField?.SetValue(pawn, (sbyte)-1));
        }

        private static void TryStep(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                // Continue removing the remaining registrations.
            }
        }
    }
}
