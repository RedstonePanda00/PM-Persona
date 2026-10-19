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
        private float netAwardedToday;
        private int day = -1, settledDay = -1, lastUpdate = -1, lastBattleTick, pulseEnd, pulseNext, pulseRemaining;
        private int masonNext, kaldalisNext;
        private List<HonorRecord> records = new List<HonorRecord>();
        private HashSet<string> completed = new HashSet<string>();
        private Dictionary<int, int> recoveryAttempts = new Dictionary<int, int>();
        private Dictionary<int, int> refusalChecks = new Dictionary<int, int>();
        private Dictionary<int, int> dangerJobs = new Dictionary<int, int>();
        private HashSet<int> tendedEnemies = new HashSet<int>();
        public MasonKaldalisState(Game game) { }
        public static MasonKaldalisState Current => Verse.Current.Game?.GetComponent<MasonKaldalisState>();
        public bool Active => Find.Storyteller?.def?.defName == DefName;
        public static MasonKaldalisConfig Config => Find.Storyteller?.def?.GetModExtension<MasonKaldalisConfig>() ?? new MasonKaldalisConfig();
        public int Tier => HonorRules.Tier(Honor);
        public bool Enabled => Active && !Paused;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref Honor, "mkHonor");
            Scribe_Values.Look(ref BattleDrive, "mkBattleDrive");
            Scribe_Values.Look(ref Paused, "mkPaused");
            Scribe_Values.Look(ref netAwardedToday, "mkNetAwardedToday");
            Scribe_Values.Look(ref day, "mkDay", -1);
            Scribe_Values.Look(ref settledDay, "mkSettledDay", -1);
            Scribe_Values.Look(ref lastUpdate, "mkLastUpdate", -1);
            Scribe_Values.Look(ref lastBattleTick, "mkLastBattleTick");
            Scribe_Values.Look(ref pulseEnd, "mkPulseEnd");
            Scribe_Values.Look(ref pulseNext, "mkPulseNext");
            Scribe_Values.Look(ref pulseRemaining, "mkPulseRemaining");
            Scribe_Values.Look(ref masonNext, "mkMasonNext");
            Scribe_Values.Look(ref kaldalisNext, "mkKaldalisNext");
            Scribe_Collections.Look(ref records, "mkRecords", LookMode.Deep);
            Scribe_Collections.Look(ref completed, "mkCompleted", LookMode.Value);
            Scribe_Collections.Look(ref recoveryAttempts, "mkRecoveryAttempts", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref refusalChecks, "mkRefusalChecks", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref dangerJobs, "mkDangerJobs", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref tendedEnemies, "mkTendedEnemies", LookMode.Value);
            Scribe_Collections.Look(ref battles, "mkBattles", LookMode.Deep);
        }

        internal bool Once(string key) => completed.Add(key);
        internal bool OncePerDay(string key)
        {
            int now = GenTicks.TicksGame;
            if (records.Any(r => r.key == key && now - r.tick < 60000)) return false;
            records.Add(new HonorRecord { key = key, tick = now }); return true;
        }
        public void Award(string kind, float points, Pawn actor = null, string ideologyIssue = null)
        {
            if (!Enabled || points == 0) return;
            ResetDay();
            int now = GenTicks.TicksGame;
            records.RemoveAll(r => now - r.tick >= 60000);
            if (points > 0)
            {
                points = HonorRules.Reward(points, records.Count(r => r.key == "reward:" + kind), netAwardedToday);
                records.Add(new HonorRecord { key = "reward:" + kind, tick = now });
            }
            else points *= IdeologyFactor(actor, ideologyIssue);
            float previous = Honor;
            Honor = HonorRules.Apply(Honor, points);
            // The design caps daily net gains. Only actual losses (after the -200 floor) restore headroom.
            netAwardedToday += Honor - previous;
        }
        private void ResetDay()
        {
            int nowDay = GenTicks.TicksGame / 60000;
            if (day == nowDay) return;
            day = nowDay; netAwardedToday = 0;
        }
        private static float IdeologyFactor(Pawn actor, string issue)
        {
            if (!ModsConfig.IdeologyActive || string.IsNullOrEmpty(issue)) return 1f;
            var ideo = actor?.Ideo ?? Faction.OfPlayer?.ideos?.PrimaryIdeo;
            if (ideo == null) return 1f;
            // Explicit vanilla precepts: never infer acceptance from translated labels.
            var precept = ideo.PreceptsListForReading.FirstOrDefault(p => p.def.defName.StartsWith(issue + "_", StringComparison.Ordinal));
            string name = precept?.def.defName ?? "";
            if (name.Contains("_Acceptable") || name.Contains("_Required") || name.Contains("_Preferred") || name.Contains("_Honorable") || name.Contains("_Respected") || name.Contains("_DontCare")) return .5f;
            if (name.Contains("_Abhorrent") || name.Contains("_Horrible") || name.Contains("_Disapproved") || name.Contains("_Forbidden")) return 2f;
            return 1f;
        }
        internal static IEnumerable<Pawn> Colonists => PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.Where(p => p.RaceProps.Humanlike && !p.IsSlave);
        internal static bool Applies(Pawn pawn) => pawn != null && pawn.RaceProps.Humanlike && pawn.IsColonist && !pawn.IsSlave && !pawn.Dead;
        public override void GameComponentTick()
        {
            int now = GenTicks.TicksGame;
            if (now % 250 != 0) return;
            if (lastUpdate < 0) { lastUpdate = now; lastBattleTick = now; settledDay = now / 60000; }
            int elapsed = Math.Max(0, now - lastUpdate); lastUpdate = now;
            if (Enabled)
            {
                BattleDrive = Mathf.Min(100, BattleDrive + 3f * elapsed / 60000f);
                if (settledDay != now / 60000)
                {
                    settledDay = now / 60000;
                    ResetDay(); Honor = HonorRules.Decay(Honor);
                    var slaves = Find.Maps.SelectMany(m => m.mapPawns.AllPawnsSpawned).Where(p => p.RaceProps.Humanlike && p.IsSlaveOfColony).ToList();
                    float slavePenalty = Math.Min(10, slaves.Count * 2);
                    if (slavePenalty > 0) Award("Slavery", -slavePenalty, null, "Slavery");
                    // Inactivity only counts while hostile combatants are present; peaceful play is not punished.
                    if (now - lastBattleTick >= Config.inactivityDays * 60000 && Find.Maps.Any(m => m.IsPlayerHome && m.mapPawns.AllPawnsSpawned.Any(p => !p.Downed && p.HostileTo(Faction.OfPlayer))))
                        Award("AvoidingBattle", -1);
                    if (Tier < 0) foreach (Pawn p in Colonists)
                        if (!p.Drafted && !p.InMentalState && Rand.Chance(Config.idlingChancePerDay)) StartRefusal(p);
                }
                TickBattles(now);
                TickDangerOrders();
            }
            else
            {
                settledDay = now / 60000;
                if (masonNext > 0) masonNext += elapsed;
                if (kaldalisNext > 0) kaldalisNext += elapsed;
                if (pulseRemaining > 0) { pulseEnd += elapsed; pulseNext += elapsed; }
            }
            SyncEffects();
        }
        private void SyncEffects()
        {
            foreach (Pawn pawn in Colonists)
            {
                var def = DefDatabase<HediffDef>.GetNamed("PMP_CommandHonor");
                Hediff existing = pawn.health.hediffSet.GetFirstHediffOfDef(def);
                int tier = Active ? Tier : 0;
                if (tier == 0) { if (existing != null) pawn.health.RemoveHediff(existing); continue; }
                float severity = tier < 0 ? .1f : tier == 1 ? .5f : 1f;
                if (existing == null) { existing = HediffMaker.MakeHediff(def, pawn); existing.Severity = severity; pawn.health.AddHediff(existing); }
                else if (Math.Abs(existing.Severity - severity) > .001f) existing.Severity = severity;
            }
        }
        internal void NotifyTendedEnemy(Pawn pawn) { tendedEnemies.Add(pawn.thingIDNumber); records.Add(new HonorRecord { key = "care:" + pawn.thingIDNumber, tick = GenTicks.TicksGame }); }
        internal bool RecentlyTended(Pawn pawn, int since) => records.Any(r => r.key == "care:" + pawn.thingIDNumber && r.tick >= since);
        internal bool WasTended(Pawn pawn) => tendedEnemies.Contains(pawn.thingIDNumber);
        internal void NotifyBattleFinished() { BattleDrive = Math.Max(0, BattleDrive - 40); lastBattleTick = GenTicks.TicksGame; }
    }
}
