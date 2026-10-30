using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PMP_Persona.Storyteller
{
    public class Hediff_CommandHonor : HediffWithComps
    {
        public override bool ShouldRemove => base.ShouldRemove || MasonKaldalisState.Current?.Enabled != true || !MasonKaldalisState.Applies(pawn);
    }
    public class ThoughtWorker_CommandHonor : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn pawn)
        {
            var state = MasonKaldalisState.Current;
            if (state?.Enabled != true || !MasonKaldalisState.Applies(pawn)) return false;
            float h = state.Honor;
            if (h < 0) return ThoughtState.ActiveAtStage(h <= -150 ? 0 : h <= -75 ? 1 : 2);
            if (h >= 600) return ThoughtState.ActiveAtStage(5);
            if (h >= 200) return ThoughtState.ActiveAtStage(h >= 400 ? 4 : 3);
            return false;
        }
    }
    // Legacy mental-state loader for existing saves.
    public class MentalState_CommandRefusal : MentalState
    {
        public override void MentalStateTick(int delta) => RecoverFromState();
    }
    public class Hediff_CommandRefusal : Hediff
    {
        public int until;
        public override void PostMake() { base.PostMake(); until = GenTicks.TicksGame + MasonKaldalisState.Config.refusalTicks; }
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref until, "until"); }
        public override bool ShouldRemove => GenTicks.TicksGame >= until || MasonKaldalisState.Current?.Enabled != true
            || MasonKaldalisState.Current.Honor > -50 || !MasonKaldalisState.Applies(pawn);
    }
    public class HediffCompProperties_HonorResponse : HediffCompProperties
    {
        public HediffCompProperties_HonorResponse() { compClass = typeof(HediffComp_HonorResponse); }
    }
    public class HediffComp_HonorResponse : HediffComp
    {
        private int nextAttempt;
        public override void CompExposeData() => Scribe_Values.Look(ref nextAttempt, "nextFireResponse");
        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            if (!Pawn.IsHashIntervalTick(60, delta)) return;
            TryExtinguish();
        }
        internal void TryExtinguish()
        {
            Pawn pawn = Pawn;
            if (MasonKaldalisState.Current is not { Enabled: true, Tier: 2 } || !MasonKaldalisState.Applies(pawn)
                || !pawn.Spawned || pawn.Downed || pawn.InMentalState || pawn.jobs == null || GenTicks.TicksGame < nextAttempt) return;
            string job = pawn.CurJobDef?.defName;
            if (pawn.CurJobDef == JobDefOf.ExtinguishSelf || job == "JumpInWater") return;
            Thing fire = pawn.GetAttachment(ThingDefOf.Fire);
            if (fire == null) return;
            nextAttempt = GenTicks.TicksGame + 250;
            // Start the self-extinguish job from the pawn's passive response.
            pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.ExtinguishSelf, fire), JobCondition.InterruptForced, resumeCurJobAfterwards: true);
        }
    }
    public partial class MasonKaldalisState
    {
        internal bool RefuseDraft(Pawn pawn)
        {
            if (!Enabled || Honor > -50 || !Applies(pawn) || pawn.InMentalState) return false;
            var def = DefDatabase<HediffDef>.GetNamed("PMP_CommandRefusalTimer");
            var refusal = pawn.health.hediffSet.GetFirstHediffOfDef(def) as Hediff_CommandRefusal;
            if (refusal != null && !refusal.ShouldRemove) return true;
            int now = GenTicks.TicksGame;
            if (refusalChecks.TryGetValue(pawn.thingIDNumber, out int previous) && now - previous < 2500) return false;
            refusalChecks[pawn.thingIDNumber] = now;
            if (!Rand.Chance(Config.draftRefusalChance)) return false;
            if (refusal != null) pawn.health.RemoveHediff(refusal);
            pawn.health.AddHediff(def);
            Messages.Message("PMP.Honor.RefusedDraft".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.RejectInput, false);
            return true;
        }
        internal void WatchOrder(Pawn pawn, Job job)
        {
            if (Enabled && Honor <= -50 && Applies(pawn) && job?.def == JobDefOf.Goto)
                dangerJobs[pawn.thingIDNumber] = job.loadID;
        }
        private void TickDangerOrders()
        {
            if (Honor > -50) { dangerJobs.Clear(); return; }
            foreach (Pawn pawn in Colonists)
            {
                if (!dangerJobs.TryGetValue(pawn.thingIDNumber, out int id)) continue;
                var job = pawn.CurJob;
                if (!pawn.Spawned || job == null || job.loadID != id) { dangerJobs.Remove(pawn.thingIDNumber); continue; }
                IntVec3 cell = job.targetA.Cell;
                if (!cell.InBounds(pawn.Map) || pawn.Position.DistanceToSquared(cell) > 144) continue;
                bool danger = cell.GetThingList(pawn.Map).Any(t => t is Fire) || pawn.Map.mapPawns.AllPawnsSpawned.Any(p => !p.Downed && p.HostileTo(pawn) && p.Position.DistanceToSquared(cell) <= 36);
                if (!danger) continue;
                dangerJobs.Remove(pawn.thingIDNumber);
                int now = GenTicks.TicksGame;
                if (dangerChecks.TryGetValue(pawn.thingIDNumber, out int previous) && now - previous < 60000) continue;
                dangerChecks[pawn.thingIDNumber] = now;
                if (Rand.Chance(Config.dangerRefusalChance))
                {
                    pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
                    Messages.Message("PMP.Honor.RefusedDanger".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.RejectInput, false);
                }
            }
        }
        internal bool RecoveryAvailable => GenTicks.TicksGame >= recoveryNext;
        internal static bool Recoverable(Pawn pawn) => Applies(pawn) && pawn.MentalState is { causedByMood: true, causedByDamage: false, causedByPsycast: false };
        internal void AttemptRecovery(Pawn pawn)
        {
            if (!Enabled || Tier < 2 || !Recoverable(pawn) || !RecoveryAvailable) return;
            recoveryNext = GenTicks.TicksGame + Config.recoveryCooldownTicks;
            pawn.MentalState.RecoverFromState();
            pawn.needs?.mood?.thoughts.memories.TryGainMemory(DefDatabase<ThoughtDef>.GetNamed("PMP_RecoveryExhaustion"));
            Messages.Message("PMP.Honor.RecoverySuccess".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false);
        }
    }
    [HarmonyPatch(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted), MethodType.Setter)]
    internal static class HonorDraftOrder
    {
        static bool Prefix(Pawn ___pawn, bool value) => !value || ___pawn.Drafted || MasonKaldalisState.Current?.RefuseDraft(___pawn) != true;
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
            if (state?.Active != true || !MasonKaldalisState.Applies(__instance)) yield break;
            yield return new Command_Action {
                defaultLabel = "PMP.Honor.Status".Translate(), defaultDesc = "PMP.Honor.StatusDesc".Translate(),
                action = () => Find.WindowStack.Add(new Dialog_HonorLedger(state))
            };
            if (state.Tier < 2 || !MasonKaldalisState.Recoverable(__instance)) yield break;
            var command = new Command_Action {
                defaultLabel = "PMP.Honor.Recovery".Translate(),
                defaultDesc = "PMP.Honor.RecoveryDesc".Translate(),
                action = () => state.AttemptRecovery(__instance)
            };
            if (!state.Enabled || !state.RecoveryAvailable) command.Disable("PMP.Honor.RecoveryCooldown".Translate());
            yield return command;
        }
    }
}
