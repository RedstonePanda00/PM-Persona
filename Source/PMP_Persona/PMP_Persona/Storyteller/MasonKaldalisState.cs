using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Storyteller
{
    public class HonorRecord : IExposable
    {
        public string key;
        public int tick;
        public void ExposeData() { Scribe_Values.Look(ref key, "key"); Scribe_Values.Look(ref tick, "tick"); }
    }

    public partial class MasonKaldalisState : GameComponent
    {
        public const string DefName = "PMP_MasonAndKaldalis";
        public float Honor;
        public float BattleDrive;
        public bool Paused;
        private float awardedToday;
        private int dataVersion = 3;
        private int day = -1, settledDay = -1, lastUpdate = -1, pulseEnd, pulseNext, pulseRemaining;
        private int bigThreatNext, restUntil, recoveryNext;
        private List<HonorRecord> records = new List<HonorRecord>();
        private HashSet<string> completed = new HashSet<string>();
        private Dictionary<int, int> refusalChecks = new Dictionary<int, int>();
        private Dictionary<int, int> dangerChecks = new Dictionary<int, int>();
        private Dictionary<int, int> dangerJobs = new Dictionary<int, int>();
        private HashSet<int> tendedEnemies = new HashSet<int>();
        public MasonKaldalisState(Game game) { }
        public static MasonKaldalisState Current => Verse.Current.Game?.GetComponent<MasonKaldalisState>();
        public bool Active => Find.Storyteller?.def?.defName == DefName;
        private static readonly MasonKaldalisConfig DefaultConfig = new MasonKaldalisConfig();
        public static MasonKaldalisConfig Config => Find.Storyteller?.def?.GetModExtension<MasonKaldalisConfig>() ?? DefaultConfig;
        public int Tier => HonorRules.Tier(Honor);
        public bool Enabled => Active && !Paused;
        internal float RewardRemaining => Math.Max(0, 15 - (day == GenTicks.TicksGame / 60000 ? awardedToday : 0));

        public override void ExposeData()
        {
            Scribe_Values.Look(ref dataVersion, "mkDataVersion", 0);
            Scribe_Values.Look(ref Honor, "mkHonor");
            Scribe_Values.Look(ref BattleDrive, "mkBattleDrive");
            Scribe_Values.Look(ref Paused, "mkPaused");
            Scribe_Values.Look(ref awardedToday, "mkAwardedToday");
            Scribe_Values.Look(ref day, "mkDay", -1);
            Scribe_Values.Look(ref settledDay, "mkSettledDay", -1);
            Scribe_Values.Look(ref lastUpdate, "mkLastUpdate", -1);
            Scribe_Values.Look(ref pulseEnd, "mkPulseEnd");
            Scribe_Values.Look(ref pulseNext, "mkPulseNext");
            Scribe_Values.Look(ref pulseRemaining, "mkPulseRemaining");
            Scribe_Values.Look(ref incidentBudget.nextTick, "mkNextEventTick");
            Scribe_Values.Look(ref incidentBudget.retryTick, "mkRetryEventTick");
            Scribe_Values.Look(ref selectionReady, "mkSelectionReady");
            Scribe_Values.Look(ref selectedKaldalis, "mkSelectedKaldalis");
            Scribe_Values.Look(ref bigThreatNext, "mkBigThreatNext");
            Scribe_Values.Look(ref restUntil, "mkRestUntil");
            Scribe_Values.Look(ref recoveryNext, "mkRecoveryNext");
            Scribe_Collections.Look(ref records, "mkRecords", LookMode.Deep);
            Scribe_Collections.Look(ref completed, "mkCompleted", LookMode.Value);
            Scribe_Collections.Look(ref refusalChecks, "mkRefusalChecks", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref dangerChecks, "mkDangerChecks", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref dangerJobs, "mkDangerJobs", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref tendedEnemies, "mkTendedEnemies", LookMode.Value);
            Scribe_Collections.Look(ref battles, "mkBattles", LookMode.Deep);
            Scribe_Collections.Look(ref ledger, "mkLedger", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                records ??= new List<HonorRecord>(); completed ??= new HashSet<string>();
                refusalChecks ??= new Dictionary<int, int>(); dangerChecks ??= new Dictionary<int, int>();
                dangerJobs ??= new Dictionary<int, int>(); tendedEnemies ??= new HashSet<int>();
                battles ??= new List<HonorBattle>(); ledger ??= new List<HonorLedgerEntry>();
                if (dataVersion < 2)
                {
                    // Block further income on the migrated save's current day.
                    awardedToday = 15; pulseRemaining = 0; BattleDrive = 0;
                }
                if (dataVersion < 3)
                {
                    // Initialize the shared deadline without catch-up events.
                    incidentBudget.nextTick = GenTicks.TicksGame + IncidentBudget.Interval(1.76f / 2.7f, .5);
                    incidentBudget.retryTick = 0; pulseRemaining = 0; BattleDrive = 0;
                    selectionReady = false;
                    dataVersion = 3;
                }
                Honor = Math.Max(-200, Honor); BattleDrive = Mathf.Clamp(BattleDrive, 0, 100);
                scheduleChecked = pendingIncidentTick = -1;
            }
        }

        internal bool Once(string key) => completed.Add(key);
        internal bool OncePerDay(string key)
        {
            int now = GenTicks.TicksGame;
            if (records.Any(r => r.key == key && now - r.tick < 60000)) return false;
            records.Add(new HonorRecord { key = key, tick = now }); return true;
        }
        internal bool CanRewardRelease(Pawn pawn) => !completed.Contains("enslaved:" + pawn.thingIDNumber)
            && !completed.Contains("released:" + pawn.thingIDNumber) && !completed.Contains("freed:" + pawn.thingIDNumber)
            && Once("liberated:" + pawn.thingIDNumber);
        public void Award(string kind, float points, Pawn actor = null, string ideologyIssue = null, bool guilty = false)
        {
            if (!Enabled || points == 0) return;
            ResetDay();
            int now = GenTicks.TicksGame;
            records.RemoveAll(r => now - r.tick >= 60000);
            if (points > 0)
            {
                points = HonorRules.Reward(points, records.Count(r => r.key == "reward:" + kind), awardedToday);
                if (points <= 0) return;
                records.Add(new HonorRecord { key = "reward:" + kind, tick = now });
                awardedToday += points;
            }
            else points *= IdeologyFactor(actor, ideologyIssue, kind == "TradeHuman", guilty);
            ChangeHonor(kind, points);
        }
        private void ChangeHonor(string kind, float points)
        {
            float previous = Honor;
            Honor = HonorRules.Apply(Honor, points);
            if (Math.Abs(Honor - previous) < .001f) return;
            ledger.Add(new HonorLedgerEntry { kind = kind, tick = GenTicks.TicksGame, delta = Honor - previous, balance = Honor });
            if (ledger.Count > 40) ledger.RemoveRange(0, ledger.Count - 40);
        }
        private void ResetDay()
        {
            int nowDay = GenTicks.TicksGame / 60000;
            if (day == nowDay) return;
            day = nowDay; awardedToday = 0;
        }
        private static float IdeologyFactor(Pawn actor, string issue, bool trade, bool guilty)
        {
            if (!ModsConfig.IdeologyActive || string.IsNullOrEmpty(issue)) return 1f;
            var ideo = actor?.Ideo ?? Faction.OfPlayer?.ideos?.PrimaryIdeo;
            var precept = ideo?.PreceptsListForReading.FirstOrDefault(p => p.def.defName.StartsWith(issue + "_", StringComparison.Ordinal));
            return HonorRules.IdeologyFactor(precept?.def.defName, trade, guilty);
        }
        internal static IEnumerable<Pawn> Colonists => PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Where(Applies);
        internal static bool Applies(Pawn pawn) => pawn != null && pawn.RaceProps.Humanlike && pawn.Faction == Faction.OfPlayer && pawn.IsColonist && !pawn.IsSlave && !pawn.Dead;
        public override void GameComponentTick()
        {
            int now = GenTicks.TicksGame;
            if (now % 250 != 0) return;
            if (lastUpdate < 0) { lastUpdate = now; settledDay = now / 60000; }
            int elapsed = Math.Max(0, now - lastUpdate); lastUpdate = now;
            if (Enabled)
            {
                if (!Find.Storyteller.difficulty.allowBigThreats)
                {
                    if (PulseActive) FinishPulse(now);
                    BattleDrive = 0;
                }
                else if (!PulseActive && now >= restUntil)
                    BattleDrive = Mathf.Min(100, BattleDrive + Config.battleDrivePerDay * elapsed / 60000f);
                if (HonorRules.PulseExpired(pulseRemaining, pulseEnd, now)) FinishPulse(now);
                if (settledDay != now / 60000)
                {
                    settledDay = now / 60000;
                    ResetDay(); ChangeHonor("Decay", HonorRules.Decay(Honor) - Honor);
                    int slaves = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_SlavesOfColony.Count(p => p.RaceProps.Humanlike);
                    if (slaves > 0) Award("Slavery", -Math.Min(10, slaves * 2), null, "Slavery");
                    records.RemoveAll(r => now - r.tick >= 60000);
                    foreach (int id in refusalChecks.Where(p => now - p.Value >= 60000).Select(p => p.Key).ToList()) refusalChecks.Remove(id);
                    foreach (int id in dangerChecks.Where(p => now - p.Value >= 60000).Select(p => p.Key).ToList()) dangerChecks.Remove(id);
                }
                TickBattles(now);
                TickDangerOrders();
            }
            else
            {
                settledDay = now / 60000;
                incidentBudget.Freeze(elapsed);
                bigThreatNext = HonorRules.FreezeDeadline(bigThreatNext, elapsed);
                restUntil = HonorRules.FreezeDeadline(restUntil, elapsed);
                recoveryNext = HonorRules.FreezeDeadline(recoveryNext, elapsed);
                if (Active && PulseActive) { pulseEnd += elapsed; pulseNext += elapsed; }
                if (!Active) { if (PulseActive) BattleDrive = 0; pulseRemaining = 0; selectionReady = false; battles.Clear(); }
                dangerJobs.Clear();
            }
            SyncEffects();
        }
        internal void SyncEffects()
        {
            var def = DefDatabase<HediffDef>.GetNamed("PMP_CommandHonor");
            foreach (Pawn pawn in Colonists.ToList())
            {
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                int tier = Enabled ? Tier : 0;
                if (tier == 0) { if (existing != null) pawn.health.RemoveHediff(existing); continue; }
                float severity = tier < 0 ? .1f : tier == 1 ? .5f : 1f;
                if (existing == null) { existing = HediffMaker.MakeHediff(def, pawn); existing.Severity = severity; pawn.health.AddHediff(existing); }
                else if (Math.Abs(existing.Severity - severity) > .001f) existing.Severity = severity;
            }
        }
        internal void NotifyTendedEnemy(Pawn pawn)
        {
            if (!Applies(pawn)) tendedEnemies.Add(pawn.thingIDNumber);
        }
        internal bool WasTended(Pawn pawn) => tendedEnemies.Contains(pawn.thingIDNumber);
    }
}
