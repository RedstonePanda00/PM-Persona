using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.Storyteller
{
    public class StorytellerCompProperties_MasonKaldalis : StorytellerCompProperties
    {
        // Mean event interval in days; combined rate: 1/1.1 + 1/1.6.
        public float mtbDays = 1.76f / 2.7f;
        public float masonShare = 16f / 27f;
        public List<IncidentCategoryEntry> masonWeights = new List<IncidentCategoryEntry>();
        public List<IncidentCategoryEntry> kaldalisWeights = new List<IncidentCategoryEntry>();
        public StorytellerCompProperties_MasonKaldalis() { compClass = typeof(StorytellerComp_MasonKaldalis); }
    }

    public class StorytellerComp_MasonKaldalis : StorytellerComp
    {
        public StorytellerCompProperties_MasonKaldalis Props => (StorytellerCompProperties_MasonKaldalis)props;
        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget ignored)
        {
            var state = MasonKaldalisState.Current;
            if (state?.Enabled != true || !state.ScheduleDue(Props.mtbDays)) yield break;
            bool pulse = state.PulseActive;
            bool kaldalis = state.SelectPersona(Props.masonShare);
            var weights = kaldalis ? Props.kaldalisWeights : Props.masonWeights;
            var categories = weights.Where(c => c.category != null && c.weight > 0 &&
                (c.category != IncidentCategoryDefOf.ThreatBig || state.CanScheduleBig()) &&
                (!pulse || c.category == IncidentCategoryDefOf.ThreatBig || c.category == IncidentCategoryDefOf.ThreatSmall)).ToList();
            state.Budget.Attempt(GenTicks.TicksGame, MasonKaldalisState.Config.retryTicks);
            while (categories.Count > 0)
            {
                var category = categories.RandomElementByWeight(c => c.weight *
                    (kaldalis && !pulse && state.BattleDrive >= 60 && c.category == IncidentCategoryDefOf.ThreatSmall ? 2 : 1));
                categories.Remove(category);
                var targets = Find.Storyteller.AllIncidentTargets.Where(t =>
                    (!(t is Map map) || map.IsPlayerHome || map.IncidentTargetTags().Contains(IncidentTargetTagDefOf.Map_RaidBeacon)) &&
                    DefDatabase<IncidentDef>.AllDefsListForReading.Any(d => d.category == category.category && d.TargetAllowed(t))).ToList();
                if (targets.Count == 0) continue;
                // Probe one target per category; worker availability is cached per tick.
                var target = targets.RandomElement();
                IncidentParms parms = GenerateParms(category.category, target);
                if (parms.points > 0) parms.points *= kaldalis ? Rand.Range(.7f, 1.3f) : Rand.Range(.9f, 1.1f);
                if (TrySelectRandomIncident(UsableIncidentsInCategory(category.category, parms), out IncidentDef incident, target))
                {
                    state.MarkIncidentPending();
                    yield return new FiringIncident(incident, this, parms);
                    yield break;
                }
            }
        }
    }

    public partial class MasonKaldalisState
    {
        private int scheduleChecked = -1, pendingIncidentTick = -1;
        private bool selectionReady, selectedKaldalis;
        private IncidentBudget incidentBudget = new IncidentBudget();
        internal IncidentBudget Budget => incidentBudget;
        internal bool PulseActive => pulseRemaining > 0;
        internal bool CanScheduleBig() => Find.Storyteller.difficulty.allowBigThreats
            && GenDate.DaysPassedSinceSettleFloat >= 3 && GenTicks.TicksGame >= bigThreatNext
            && GenTicks.TicksGame >= restUntil
            && !battles.Any(b => b.raid && !b.settled && (b.enemies.Any(p => p != null && !p.Dead && !p.Downed && p.Spawned && p.HostileTo(Faction.OfPlayer))
                || !b.arrived && GenTicks.TicksGame - b.started < 60000));
        internal bool ScheduleDue(float mtb)
        {
            int now = GenTicks.TicksGame;
            if (!Enabled || scheduleChecked == now) return false;
            scheduleChecked = now;
            if (incidentBudget.nextTick == 0)
                incidentBudget.nextTick = now + IncidentBudget.Interval(mtb, Rand.Value);
            if (PulseActive && (!Find.Storyteller.difficulty.allowBigThreats || HonorRules.PulseExpired(pulseRemaining, pulseEnd, now))) FinishPulse(now);
            if (!PulseActive && BattleDrive >= 80 && now >= restUntil && Find.Storyteller.difficulty.allowBigThreats)
            {
                pulseRemaining = Math.Max(1, Math.Min(2, Config.pulseIncidents));
                pulseEnd = now + Rand.RangeInclusive(Config.pulseDurationMin, Math.Max(Config.pulseDurationMin, Config.pulseDurationMax));
                pulseNext = now;
                selectionReady = false;
            }
            return incidentBudget.Due(now, PulseActive, pulseNext);
        }
        internal bool SelectPersona(float masonShare)
        {
            if (PulseActive) return true;
            if (!selectionReady)
            {
                selectedKaldalis = !Rand.Chance(masonShare);
                selectionReady = true;
            }
            return selectedKaldalis;
        }
        internal void MarkIncidentPending() => pendingIncidentTick = GenTicks.TicksGame;
        internal void FinishPulse(int now)
        {
            pulseRemaining = 0; BattleDrive = 0;
            restUntil = now + Config.restTicks;
            selectionReady = false;
            // Retain the deadline charged by pulse events.
        }
        internal void ScheduleCompleted(bool success, float mtb, IncidentCategoryDef category)
        {
            int now = GenTicks.TicksGame;
            if (pendingIncidentTick != now) return;
            pendingIncidentTick = -1;
            if (!success || !Enabled) return;
            incidentBudget.Spend(now, IncidentBudget.Interval(mtb, Rand.Value));
            selectionReady = false;
            if (category == IncidentCategoryDefOf.ThreatBig)
                bigThreatNext = now + Config.bigThreatGapTicks;
            if (PulseActive)
            {
                if (--pulseRemaining == 0) FinishPulse(now);
                else pulseNext = now + Config.pulseGapTicks;
            }
        }
    }
    [HarmonyPatch(typeof(RimWorld.Storyteller), nameof(RimWorld.Storyteller.TryFire))]
    internal static class RecordChannelFire
    {
        static bool Prefix(FiringIncident fi) => fi.source is not StorytellerComp_MasonKaldalis || MasonKaldalisState.Current?.Enabled == true;
        static void Postfix(FiringIncident fi, bool __result)
        {
            if (fi.source is StorytellerComp_MasonKaldalis comp)
                MasonKaldalisState.Current?.ScheduleCompleted(__result, comp.Props.mtbDays, fi.def.category);
        }
    }
}
