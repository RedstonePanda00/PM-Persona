using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PMP_Persona.Storyteller
{
    public class Hediff_CommandHonor : Hediff
    {
        public override bool ShouldRemove => base.ShouldRemove || MasonKaldalisState.Current?.Active != true || !MasonKaldalisState.Applies(pawn);
    }
    public class ThoughtWorker_CommandHonor : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            var state = MasonKaldalisState.Current;
            if (state?.Active != true || !MasonKaldalisState.Applies(pawn)) return false;
            float h = state.Honor;
            if (h < 0) return ThoughtState.ActiveAtStage(h <= -150 ? 0 : h <= -75 ? 1 : 2);
            if (h >= 600) return ThoughtState.ActiveAtStage(5);
            if (h >= 200) return ThoughtState.ActiveAtStage(h >= 400 ? 4 : 3);
            return false;
        }
    }
    public class MentalState_CommandRefusal : MentalState
    {
        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            if (age >= MasonKaldalisState.Config.refusalTicks || MasonKaldalisState.Current?.Active != true) RecoverFromState();
        }
    }
    public class JobGiver_HonorableExtinguishSelf : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (MasonKaldalisState.Current is not { Active: true, Tier: 2 } || !MasonKaldalisState.Applies(pawn)) return null;
            var fire = pawn.GetAttachment(ThingDefOf.Fire);
            return fire == null ? null : JobMaker.MakeJob(JobDefOf.ExtinguishSelf, fire);
        }
    }
    public partial class MasonKaldalisState
    {
        internal static void StartRefusal(Pawn pawn)
        {
            pawn.mindState.mentalStateHandler.TryStartMentalState(DefDatabase<MentalStateDef>.GetNamed("PMP_CommandRefusal"), forceWake: true);
        }
        internal bool RefuseDraft(Pawn pawn)
        {
            if (!Enabled || Tier >= 0 || !Applies(pawn) || pawn.InMentalState) return false;
            int now = GenTicks.TicksGame;
            if (refusalChecks.TryGetValue(pawn.thingIDNumber, out int previous) && now - previous < 2500) return false;
            refusalChecks[pawn.thingIDNumber] = now;
            if (!Rand.Chance(Config.draftRefusalChance)) return false;
            StartRefusal(pawn); return pawn.InMentalState;
        }
        internal void WatchOrder(Pawn pawn, Job job)
        {
            if (Enabled && Tier < 0 && Applies(pawn) && job?.def == JobDefOf.Goto)
                dangerJobs[pawn.thingIDNumber] = job.loadID;
        }
        private void TickDangerOrders()
        {
            if (Tier >= 0) { dangerJobs.Clear(); return; }
            foreach (Pawn pawn in Colonists)
            {
                if (!dangerJobs.TryGetValue(pawn.thingIDNumber, out int id)) continue;
                var job = pawn.CurJob;
                if (!pawn.Spawned || job == null || job.loadID != id) { dangerJobs.Remove(pawn.thingIDNumber); continue; }
                IntVec3 cell = job.targetA.Cell;
                if (!cell.InBounds(pawn.Map)) continue;
                if (pawn.Position.DistanceToSquared(cell) > 144) continue;
                bool danger = cell.GetThingList(pawn.Map).Any(t => t is Fire) || pawn.Map.mapPawns.AllPawnsSpawned.Any(p => !p.Downed && p.HostileTo(pawn) && p.Position.DistanceToSquared(cell) <= 36);
                // One roll per issued order, when it first approaches danger; never roll every tick.
                if (!danger) continue;
                dangerJobs.Remove(pawn.thingIDNumber);
                if (Rand.Chance(Config.dangerRefusalChance))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    Messages.Message("PMP.Honor.RefusedDanger".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.RejectInput, false);
                }
            }
        }
        internal bool RecoveryAvailable(Pawn pawn) => !recoveryAttempts.TryGetValue(pawn.thingIDNumber, out int tick) || GenTicks.TicksGame - tick >= Config.recoveryCooldownTicks;
        internal void AttemptRecovery(Pawn pawn)
        {
            if (!Enabled || Tier < 2 || !Applies(pawn) || !pawn.InMentalState || !RecoveryAvailable(pawn)) return;
            recoveryAttempts[pawn.thingIDNumber] = GenTicks.TicksGame;
            bool success = Rand.Chance(Config.recoveryChance);
            if (success)
            {
                pawn.MentalState.RecoverFromState();
                pawn.needs?.mood?.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("PMP_RecoveryExhaustion"));
            }
            Messages.Message((success ? "PMP.Honor.RecoverySuccess" : "PMP.Honor.RecoveryFailed").Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);
        }
    }
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    internal static class HonorDraftOrder
    {
        static bool Prefix(Pawn ___pawn, bool value) => !value || MasonKaldalisState.Current?.RefuseDraft(___pawn) != true;
    }
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.TryTakeOrderedJob))]
    internal static class HonorDangerOrder
    {
        static void Postfix(Pawn ___pawn, Job job, bool __result) { if (__result) MasonKaldalisState.Current?.WatchOrder(___pawn, job); }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    internal static class HonorRecoveryCommand
    {
        static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (Gizmo g in __result) yield return g;
            var state = MasonKaldalisState.Current;
            if (state?.Active != true || state.Tier < 2 || !MasonKaldalisState.Applies(__instance) || !__instance.InMentalState) yield break;
            var command = new Command_Action {
                defaultLabel = "PMP.Honor.Recovery".Translate(),
                defaultDesc = "PMP.Honor.RecoveryDesc".Translate(MasonKaldalisState.Config.recoveryChance.ToStringPercent()),
                action = () => state.AttemptRecovery(__instance)
            };
            if (!state.Enabled || !state.RecoveryAvailable(__instance)) command.Disable("PMP.Honor.RecoveryCooldown".Translate());
            yield return command;
        }
    }
}
