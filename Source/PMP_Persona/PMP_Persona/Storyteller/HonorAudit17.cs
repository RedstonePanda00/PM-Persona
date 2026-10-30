using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Storyteller
{
    // Isolated honor audit: -pmphonoraudit -savedatafolder=<scratch path>.
    public class HonorAudit17 : GameComponent
    {
        private int stage, frames, started, pawnId, savedPulseEnd, savedBudgetTick, savedRetryTick;
        private static readonly List<string> results = new List<string>();
        private static string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "HonorAudit17");
        public HonorAudit17(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "honorAuditStage");
            Scribe_Values.Look(ref savedPulseEnd, "honorAuditPulseEnd");
            Scribe_Values.Look(ref savedBudgetTick, "honorAuditBudgetTick");
            Scribe_Values.Look(ref savedRetryTick, "honorAuditRetryTick");
        }
        private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Check(string name, Action run)
        {
            try { run(); results.Add("PASS " + name); }
            catch (Exception e) { results.Add("FAIL " + name + ": " + e); }
            File.WriteAllLines(Path.Combine(Folder, "results.txt"), results);
        }
        public override void GameComponentTick()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmphonoraudit") || stage != 1 || GenTicks.TicksGame % 30 != 0) return;
            Pawn pawn = Find.CurrentMap?.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.thingIDNumber == pawnId);
            if (pawn != null) File.AppendAllText(Path.Combine(Folder, "fire-trace.txt"),
                $"{GenTicks.TicksGame - started}: {pawn.CurJobDef?.defName}, fire={pawn.HasAttachment(ThingDefOf.Fire)}, extinguished={pawn.records.GetAsInt(RecordDefOf.FiresExtinguished)}\n");
        }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmphonoraudit") || stage == 3 || Find.CurrentMap == null || ++frames < 90) return;
            Directory.CreateDirectory(Folder);
            var state = MasonKaldalisState.Current;
            var fields = Traverse.Create(state);
            if (stage == 2)
            {
                stage = 3;
                Check("save reload: honor, gross allowance, pulse, shared cooldown, ledger, dedupe", () => {
                    Assert(state.Honor == 15 && state.Paused && state.BattleDrive == 83, "values lost");
                    Assert(state.PulseActive && fields.Field("pulseEnd").GetValue<int>() == savedPulseEnd, "pulse clock lost");
                    Assert(state.Budget.nextTick == savedBudgetTick && state.Budget.retryTick == savedRetryTick, "borrowed budget/retry lost");
                    Assert(fields.Field("selectionReady").GetValue<bool>() && fields.Field("selectedKaldalis").GetValue<bool>(), "pending persona lost");
                    Assert(!state.RecoveryAvailable && state.Ledger.Count > 0, "cooldown or ledger lost");
                    state.Paused = false; state.Award("Reload", 10);
                    Assert(state.Honor == 15 && state.RewardRemaining == 0, "cap lost");
                    Assert(!state.Once("audit-once"), "dedupe lost");
                });
                Check("runtime log", () => {
                    var errors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToArray();
                    File.WriteAllLines(Path.Combine(Folder, "errors.txt"), errors);
                    Assert(errors.Length == 0, string.Join("\n", errors));
                });
                File.WriteAllText(Path.Combine(Folder, "complete.txt"), results.Any(r => r.StartsWith("FAIL")) ? "FAIL" : "PASS");
                Application.Quit(); return;
            }
            if (stage == 1)
            {
                if (GenTicks.TicksGame - started < 350 && frames < 3000) return;
                Find.TickManager.Pause();
                Check("passive comp completes extinguish job during actual ticking", () => {
                    var pawn = Find.CurrentMap.mapPawns.AllPawnsSpawned.Single(p => p.thingIDNumber == pawnId);
                    Assert(GenTicks.TicksGame - started >= 350, "simulation timed out");
                    Assert(!pawn.HasAttachment(ThingDefOf.Fire), "fire still attached");
                    Assert(pawn.records.GetAsInt(RecordDefOf.FiresExtinguished) > 0, "job never completed");
                });
                Check("save", () => {
                    state.Honor = 15; state.BattleDrive = 83; state.Paused = true;
                    fields.Field("pulseRemaining").SetValue(1);
                    savedPulseEnd = GenTicks.TicksGame + 90000;
                    fields.Field("pulseEnd").SetValue(savedPulseEnd);
                    fields.Field("pulseNext").SetValue(GenTicks.TicksGame + 45000);
                    state.Budget.nextTick = savedBudgetTick = GenTicks.TicksGame + 120000;
                    state.Budget.retryTick = savedRetryTick = GenTicks.TicksGame + 5000;
                    fields.Field("selectionReady").SetValue(true); fields.Field("selectedKaldalis").SetValue(true);
                    state.Once("audit-once"); stage = 2;
                    GameDataSaveLoader.SaveGame("HonorAudit17");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("HonorAudit17"));
                });
                return;
            }
            stage = 1;
            Find.TickManager.Pause();
            Current.Game.storyteller = new RimWorld.Storyteller(DefDatabase<StorytellerDef>.GetNamed(MasonKaldalisState.DefName), DifficultyDefOf.Rough);
            Pawn subject = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First();
            pawnId = subject.thingIDNumber;
            Check("all storyteller Harmony hooks installed", () => {
                var hooks = typeof(HonorAudit17).Assembly.GetTypes().Where(t => t.Namespace == typeof(HonorAudit17).Namespace && t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0);
                var installed = Harmony.GetAllPatchedMethods().SelectMany(m => Harmony.GetPatchInfo(m).Prefixes.Concat(Harmony.GetPatchInfo(m).Postfixes)).Select(p => p.PatchMethod.DeclaringType).ToHashSet();
                Assert(hooks.All(installed.Contains), string.Join(", ", hooks.Where(t => !installed.Contains(t))));
            });
            Check("arithmetic boundaries and exact ideology policies", () => {
                Assert(new[] { -200f, -0.1f, 0, 199, 200, 599, 600 }.Select(HonorRules.Tier).SequenceEqual(new[] { -1, -1, 0, 0, 1, 1, 2 }), "tiers");
                Assert(HonorRules.Decay(-.25f) == 0 && HonorRules.Decay(.25f) == 0, "cross-zero decay");
                Assert(HonorRules.RaidReward(0) == 8 && HonorRules.RaidReward(10000) == 20, "raid caps");
                Assert(HonorRules.IdeologyFactor("OrganUse_HorribleSellOK", true, false) == .5f && HonorRules.IdeologyFactor("OrganUse_HorribleSellOK", false, false) == 2, "organ policy");
                Assert(HonorRules.IdeologyFactor("Execution_HorribleIfInnocent", false, true) == 1 && HonorRules.IdeologyFactor("Execution_HorribleIfInnocent", false, false) == 2, "guilt");
                Assert(HonorRules.IdeologyFactor("Mod_Acceptable", false, false) == 1, "unknown precept guessed");
                Assert(HonorRules.FreezeDeadline(10000, 500) == 10500 && HonorRules.FreezeDeadline(0, 500) == 0, "freeze");
            });
            Check("gross reward cap, rolling diminishing returns and penalty floor", () => {
                state.Honor = 0;
                for (int i = 0; i < 4; i++) state.Award("AuditReward", 8);
                Assert(state.Honor == 14, "diminishing returns");
                state.Award("AuditOther", 8); Assert(state.Honor == 15, "daily cap");
                state.Award("AuditLoss", -400); Assert(state.Honor == -200, "floor");
                state.Award("AuditRestore", 400); Assert(state.Honor == -200, "loss restored allowance");
            });
            Check("one scheduler, pulse budget, failed execution, expiry and peaceful gate", () => {
                Assert(Find.Storyteller.storytellerComps.OfType<StorytellerComp_MasonKaldalis>().Count() == 1, "multiple schedulers");
                const float mean = 1.76f / 2.7f;
                state.Budget.nextTick = GenTicks.TicksGame - 1;
                Assert(state.ScheduleDue(mean) && !state.ScheduleDue(mean), "multiple targets duplicate an opportunity");
                int before = state.Budget.nextTick;
                state.MarkIncidentPending(); state.ScheduleCompleted(false, mean, IncidentCategoryDefOf.Misc);
                Assert(state.Budget.nextTick == before, "failed execution consumed budget");
                state.BattleDrive = 80;
                fields.Field("scheduleChecked").SetValue(-1);
                Assert(state.ScheduleDue(mean) && state.PulseActive && state.SelectPersona(1), "pulse start/persona");
                state.Budget.nextTick = GenTicks.TicksGame + 30000;
                state.MarkIncidentPending(); state.ScheduleCompleted(true, mean, IncidentCategoryDefOf.ThreatBig);
                before = state.Budget.nextTick;
                Assert(before > GenTicks.TicksGame + 30000 && state.PulseActive && !state.CanScheduleBig(), "pulse free event or missing cooldown");
                state.ScheduleCompleted(true, mean, IncidentCategoryDefOf.ThreatBig);
                Assert(state.Budget.nextTick == before, "duplicate callback spent budget twice");
                fields.Field("pulseEnd").SetValue(GenTicks.TicksGame);
                fields.Field("scheduleChecked").SetValue(-1);
                Assert(!state.ScheduleDue(mean), "pulse expiry bypassed repayment");
                Assert(!state.PulseActive && state.BattleDrive == 0 && state.Budget.nextTick == before, "expiry refunded borrowed budget");
                state.Budget.Freeze(1000);
                Assert(state.Budget.nextTick == before + 1000, "pause lost budget");
                Find.Storyteller.difficulty.allowBigThreats = false;
                Assert(!state.CanScheduleBig(), "peaceful gate");
                Find.Storyteller.difficulty.allowBigThreats = true;
            });
            Check("100000-day shared-budget regression with and without pulses", AuditBudget);
            Check("natural recovery and shared cooldown", () => {
                state.Honor = 600;
                Assert(subject.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Sad, forceWake: true, causedByMood: true), "mental state");
                state.AttemptRecovery(subject);
                Assert(!subject.InMentalState && !state.RecoveryAvailable, "recovery/cooldown");
                Assert(subject.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Sad, forceWake: true, causedByPsycast: true), "psycast state");
                Assert(!MasonKaldalisState.Recoverable(subject), "forced state recoverable");
                subject.MentalState.RecoverFromState();
            });
            Check("refusal is a timer, no mental state or think tree", () => {
                state.Honor = -100;
                float chance = MasonKaldalisState.Config.draftRefusalChance;
                try {
                    MasonKaldalisState.Config.draftRefusalChance = 1;
                    subject.drafter.Drafted = true;
                    Assert(!subject.Drafted && !subject.InMentalState && subject.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("PMP_CommandRefusalTimer")), "refusal");
                } finally { MasonKaldalisState.Config.draftRefusalChance = chance; }
                Assert(DefDatabase<ThinkTreeDef>.GetNamedSilentFail("PMP_HonorExtinguishSelf") == null && DefDatabase<ThinkTreeDef>.GetNamedSilentFail("PMP_CommandRefusalBehavior") == null, "injected tree");
            });
            Check("threshold effects and pause cleanup", () => {
                state.Honor = 600; state.SyncEffects();
                var effect = subject.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("PMP_CommandHonor"));
                Assert(effect.CurStage.statOffsets.Single(s => s.stat == StatDefOf.MentalBreakThreshold).value < 0, "threshold sign");
                Assert(effect is HediffWithComps h && h.TryGetComp<HediffComp_HonorResponse>() != null, "passive comp missing");
                state.Paused = true; state.SyncEffects(); state.Award("Pause", -10);
                Assert(state.Honor == 600 && !subject.health.hediffSet.HasHediff(effect.def), "pause left effect");
                state.Paused = false; state.SyncEffects();
            });
            Check("enslavement cannot farm release rewards", () => {
                Pawn captive = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
                GenSpawn.Spawn(captive, subject.Position, subject.Map);
                captive.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                captive.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
                Assert(!state.CanRewardRelease(captive), "enslave/free loop rewards");
                captive.Destroy();
                state.Honor = 600;
            });
            Check("human corpse ingestion is counted; animals exit before classification", () => {
                Pawn dead = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
                GenSpawn.Spawn(dead, subject.Position, subject.Map); dead.Kill(null);
                Corpse corpse = dead.Corpse;
                state.Honor = 600;
                float nutrition = corpse.Ingested(subject, .1f);
                Assert(nutrition > 0 && state.Honor < 600, "direct corpse ingestion not counted");
                Pawn animal = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Muffalo"));
                var args = new object[] { corpse, animal, false };
                AccessTools.Method(typeof(HonorEating), "Prefix").Invoke(null, args);
                Assert(!(bool)args[2], "animal ingestion classified");
                if (!corpse.Destroyed) corpse.Destroy(); animal.Destroy();
                state.Honor = 600;
            });
            Check("comp starts self-extinguish once without tree injection", () => {
                state.SyncEffects();
                // Disable rain and other firefighters in the job fixture.
                var map = subject.Map;
                var cell = map.AllCells.First(c => c.Standable(map) && !c.Fogged(map)
                    && map.mapPawns.AllPawnsSpawned.All(p => p == subject || p.Position.DistanceToSquared(c) > 2500));
                subject.Position = cell; subject.Notify_Teleported();
                map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
                subject.drafter.Drafted = true;
                subject.TryAttachFire(.8f, null);
                Assert(subject.HasAttachment(ThingDefOf.Fire), "fire not attached");
                var comp = ((HediffWithComps)subject.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("PMP_CommandHonor"))).TryGetComp<HediffComp_HonorResponse>();
                comp.TryExtinguish();
                Assert(subject.CurJobDef == JobDefOf.ExtinguishSelf, "comp did not start job");
                var job = subject.CurJob; comp.TryExtinguish();
                Assert(ReferenceEquals(job, subject.CurJob), "comp restarted job");
            });
            started = GenTicks.TicksGame;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
        }
        private static void AuditBudget()
        {
            int Run(bool pulses)
            {
                var random = new System.Random(47);
                var budget = new IncidentBudget { nextTick = IncidentBudget.Interval(1.76f / 2.7f, random.NextDouble()) };
                int count = 0, remaining = 0, nextPulseEvent = 0;
                // Use 30000-day epochs within the signed-int tick range.
                for (int epoch = 0; epoch < 4; epoch++)
                {
                    int days = epoch < 3 ? 30000 : 10000;
                    int end = days * 60000;
                    for (int tick = 1000; tick <= end; tick += 1000)
                    {
                        if (pulses && tick % 600000 == 0) { remaining = 2; nextPulseEvent = tick; }
                        if (!budget.Due(tick, remaining > 0, nextPulseEvent)) continue;
                        budget.Spend(tick, IncidentBudget.Interval(1.76f / 2.7f, random.NextDouble()));
                        count++;
                        if (remaining > 0) { remaining--; nextPulseEvent = tick + 15000; }
                    }
                    budget.nextTick -= end; nextPulseEvent -= end;
                }
                return count;
            }
            int regular = Run(false), withPulses = Run(true);
            Assert(Math.Abs(withPulses - regular) <= 2, "pulses created events: " + regular + " vs " + withPulses);
            Assert(regular > 152000 && regular < 155000, "mean rate drift: " + regular);
            var blocked = new IncidentBudget { nextTick = 1000 };
            blocked.Attempt(60000, 5000);
            Assert(blocked.nextTick == 1000 && !blocked.Due(61000, false, 0), "retry consumed budget or busy loop");
            blocked.Spend(65000, 39000);
            Assert(blocked.nextTick == 104000, "blocked opportunities accumulated backlog");
            File.WriteAllText(Path.Combine(Folder, "budget-simulation.txt"), $"100000 days; seed 47\nRegular: {regular} ({regular / 100000.0:F5}/day)\nPulses: {withPulses} ({withPulses / 100000.0:F5}/day)\n");
        }
    }
}
