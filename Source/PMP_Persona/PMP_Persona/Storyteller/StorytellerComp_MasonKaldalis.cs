using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Storyteller
{
    public class StorytellerCompProperties_MasonKaldalis : StorytellerCompProperties
    {
        public bool kaldalis;
        public float mtbDays = 1.1f;
        public List<IncidentCategoryEntry> categoryWeights = new List<IncidentCategoryEntry>();
        public StorytellerCompProperties_MasonKaldalis() { compClass = typeof(StorytellerComp_MasonKaldalis); }
    }

    public class StorytellerComp_MasonKaldalis : StorytellerComp
    {
        public StorytellerCompProperties_MasonKaldalis Props => (StorytellerCompProperties_MasonKaldalis)props;
        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget ignored)
        {
            var state = MasonKaldalisState.Current;
            if (state?.Enabled != true || !state.ChannelDue(Props.kaldalis, Props.mtbDays)) yield break;
            bool pulse = Props.kaldalis && state.PulseActive;
            var categories = Props.categoryWeights.Where(c => c.weight > 0 &&
                (Find.Storyteller.difficulty.allowBigThreats || c.category != IncidentCategoryDefOf.ThreatBig) &&
                (!pulse || c.category == IncidentCategoryDefOf.ThreatBig || c.category == IncidentCategoryDefOf.ThreatSmall)).ToList();
            while (categories.Count > 0)
            {
                var category = categories.RandomElementByWeight(c => c.weight *
                    (Props.kaldalis && state.BattleDrive >= 60 && c.category == IncidentCategoryDefOf.ThreatSmall ? 2 : 1));
                categories.Remove(category);
                var targets = Find.Storyteller.AllIncidentTargets.Where(t =>
                    !(t is Map map) || map.IsPlayerHome || map.IncidentTargetTags().Contains(IncidentTargetTagDefOf.Map_RaidBeacon)).InRandomOrder();
                foreach (var target in targets)
                {
                    IncidentParms parms = GenerateParms(category.category, target);
                    if (TrySelectRandomIncident(UsableIncidentsInCategory(category.category, parms).Where(d => d.TargetAllowed(target)), out IncidentDef incident, target))
                    {
                        yield return new FiringIncident(incident, this, parms);
                        yield break;
                    }
                }
            }
        }
        public override IncidentParms GenerateParms(IncidentCategoryDef category, IIncidentTarget target)
        {
            var parms = StorytellerUtility.DefaultParmsNow(category, target);
            if (parms.points > 0) parms.points *= Props.kaldalis ? Rand.Range(.7f, 1.3f) : Rand.Range(.9f, 1.1f);
            return parms;
        }
    }

    public partial class MasonKaldalisState
    {
        private int masonChecked = -1, kaldalisChecked = -1;
        internal bool PulseActive => pulseRemaining > 0;
        internal bool ChannelDue(bool kaldalis, float mtb)
        {
            int now = GenTicks.TicksGame;
            // Each comp is called once per target. Reserve the global channel before enumerating any incident.
            if (kaldalis ? kaldalisChecked == now : masonChecked == now) return false;
            if (kaldalis) kaldalisChecked = now; else masonChecked = now;
            if (kaldalis && !PulseActive && BattleDrive >= 80 && Find.Storyteller.difficulty.allowBigThreats)
            {
                pulseRemaining = Math.Max(1, Config.pulseIncidents);
                pulseEnd = now + Rand.RangeInclusive(Config.pulseDurationMin, Config.pulseDurationMax);
                pulseNext = now;
            }
            if (kaldalis && PulseActive) return now >= pulseNext;
            ref int next = ref (kaldalis ? ref kaldalisNext : ref masonNext);
            if (next == 0) { next = now + Interval(kaldalis, mtb); return false; }
            if (now < next) return false;
            // A failed selection retries next interval without adding new scheduled attempts.
            return true;
        }
        private static int Interval(bool kaldalis, float mtb) => Math.Max(1000, (int)(60000 * mtb *
            (kaldalis ? -Math.Log(Math.Max(.0001, Rand.Value)) : Rand.Range(.8f, 1.2f))));
        internal void ChannelFired(bool kaldalis, float mtb)
        {
            int now = GenTicks.TicksGame;
            if (kaldalis && PulseActive)
            {
                pulseRemaining--;
                if (pulseRemaining == 0) { BattleDrive = 0; kaldalisNext = now + Interval(true, mtb); }
                else pulseNext = now + Math.Max(1000, (pulseEnd - now) / pulseRemaining);
            }
            else if (kaldalis) kaldalisNext = now + Interval(true, mtb);
            else masonNext = now + Interval(false, mtb);
        }
    }
    [HarmonyPatch(typeof(RimWorld.Storyteller), nameof(RimWorld.Storyteller.TryFire))]
    internal static class RecordChannelFire
    {
        static void Postfix(FiringIncident fi, bool __result)
        {
            if (__result && fi.source is StorytellerComp_MasonKaldalis comp)
                MasonKaldalisState.Current?.ChannelFired(comp.Props.kaldalis, comp.Props.mtbDays);
        }
    }
    // A single externally controllable switch covers inherited, queued and custom storyteller incidents.
    [HarmonyPatch(typeof(RimWorld.Storyteller), nameof(RimWorld.Storyteller.StorytellerTick))]
    internal static class PauseMasonKaldalis
    {
        static bool Prefix() => MasonKaldalisState.Current is not { Active: true, Paused: true };
    }
}
