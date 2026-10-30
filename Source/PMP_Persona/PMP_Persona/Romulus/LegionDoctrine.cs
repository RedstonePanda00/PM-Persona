using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Romulus
{
    // Explicit opt-in for a race, equipment or faction. Equipment quality is irrelevant.
    public sealed class LegionExtension : DefModExtension { public bool advanced; }

    public static class LegionDoctrine
    {
        private sealed class Entry { public Hediff_LegionRecord record; }
        private static readonly ConditionalWeakTable<Pawn, Entry> cache = new();
        public static Hediff_LegionRecord Record(Pawn pawn)
        {
            if (pawn?.health == null) return null;
            var entry = cache.GetOrCreateValue(pawn);
            if (entry.record != null && pawn.health.hediffSet.hediffs.Contains(entry.record)) return entry.record;
            return entry.record = pawn.health.hediffSet.hediffs.OfType<Hediff_LegionRecord>().FirstOrDefault();
        }
        public static bool IsLegionFaction(Faction faction) => faction != null &&
            (faction.def.HasModExtension<LegionExtension>() || Current.Game?.GetComponent<LegionFactionRegistry>()?.Contains(faction) == true);
        public static void Refresh(Pawn pawn)
        {
            if (pawn?.health == null || pawn.Dead) return;
            var race = pawn.def.GetModExtension<LegionExtension>();
            bool blood = pawn.RaceProps.IsMechanoid && (race != null || IsLegionFaction(pawn.Faction));
            bool advanced = pawn.RaceProps.IsMechanoid && race?.advanced == true;
            if (pawn.apparel != null)
                foreach (var item in pawn.apparel.WornApparel) Qualify(item, ref blood, ref advanced);
            if (pawn.equipment != null)
                foreach (var item in pawn.equipment.AllEquipmentListForReading) Qualify(item, ref blood, ref advanced);
            bool recipient = blood || IsLegionFaction(pawn.Faction);
            var record = Record(pawn);
            if (record == null && recipient)
            {
                record = (Hediff_LegionRecord)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("PMP_LegionRecord"), pawn);
                pawn.health.AddHediff(record);
                cache.GetOrCreateValue(pawn).record = record;
            }
            record?.SetQualification(blood, advanced, recipient);
        }
        private static void Qualify(Thing item, ref bool blood, ref bool advanced)
        {
            var extension = item.def.GetModExtension<LegionExtension>();
            if (extension == null) return;
            blood = true; advanced |= extension.advanced;
        }
        // Only the capacity calculation sees full HP. Damage, death and health summaries keep real HP.
        public static float EfficiencyHealth(HediffSet set, BodyPartRecord part) =>
            Record(set.pawn)?.Blood == true && !set.PartIsMissing(part)
                ? part.def.GetMaxHealth(set.pawn) : set.GetPartHealth(part);
    }

    // Campaign adapters register exact faction instances; never infer identity from a translated name or Pirate def.
    public sealed class LegionFactionRegistry : GameComponent
    {
        private List<Faction> factions = new();
        public LegionFactionRegistry(Game game) { }
        public bool Contains(Faction faction) => factions.Contains(faction);
        public void Register(Faction faction) { if (faction != null && !factions.Contains(faction)) factions.Add(faction); }
        public override void ExposeData() { Scribe_Collections.Look(ref factions, "pmpLegionFactions", LookMode.Reference); factions ??= new(); }
    }

    public sealed class Hediff_LegionRecord : Hediff
    {
        public bool Blood, Advanced, Recipient;
        public float Merit;
        public int AuraRank;
        public Pawn AuraSource;
        private int bloodStage;
        public readonly Dictionary<BodyPartRecord,LegionEyeHistory> EyeTrails=new();
        public float EyeAlpha=>Visible?Mathf.Pow(Mathf.Clamp01((1-pawn.health.summaryHealth.SummaryHealthPercent)/.2f),.65f):0;
        public int Rank => Advanced ? Merit >= 700 ? 3 : Merit >= 300 ? 2 : Merit >= 100 ? 1 : 0 : 0;
        public int BloodStage => Blood ? bloodStage : 0;
        public override bool Visible => Blood || Advanced || AuraRank > 0;
        public override bool ShouldRemove => false; // Merit belongs to the pawn, including while equipment is removed.
        public override HediffStage CurStage => Blood ? def.stages[0] : null;
        public override string LabelInBrackets => "PMP.Legion.Summary".Translate(BloodStage, Rank, AuraRank);
        public override string Description => def.description.Formatted(pawn.LabelShortCap);
        public override string TipStringExtra => "PMP.Legion.Details".Translate(Mathf.FloorToInt(Merit), AuraSource?.LabelShortCap ?? "—")+"\n\n"+
            "PMP.Legion.Total".Translate(
                Bonus(StatDefOf.MoveSpeed),Bonus(StatDefOf.MeleeDamageFactor),
                Bonus(StatDefOf.AimingDelayFactor),Bonus(StatDefOf.RangedCooldownFactor));
        private string Bonus(StatDef stat){float delta=StatPart_Legion.Factor(this,stat.defName)-1;return (delta>0?"+":"")+delta.ToStringPercent();}
        public void SetQualification(bool blood, bool advanced, bool recipient)
        {
            bool changed = Blood != blood || Advanced != advanced || Recipient != recipient;
            Blood = blood; Advanced = advanced; Recipient = recipient;
            if (!recipient) SetAura(0, null);
            UpdateInjury();
            if (changed) pawn.health.Notify_HediffChanged(this);
        }
        public void UpdateInjury()
        {
            float health = pawn.health.summaryHealth.SummaryHealthPercent;
            int next = !Blood ? 0 : health <= .4f ? 2 : health <= .75f ? 1 : 0;
            if (next == bloodStage) return;
            bloodStage = next;
            pawn.health.Notify_HediffChanged(this);
        }
        public void AddMerit(float value) { if (Advanced) Merit = Mathf.Clamp(Merit + value, 0, 700); }
        public void SetAura(int rank, Pawn source) { AuraRank = rank; AuraSource = source; }
        public override void TickInterval(int delta) { base.TickInterval(delta); if (pawn.IsHashIntervalTick(60, delta)) UpdateInjury(); }
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref Merit, "merit");
            // Preserve injury protection during deserialization, before SpawnSetup revalidates current gear/faction.
            Scribe_Values.Look(ref Blood,"bloodQualified"); Scribe_Values.Look(ref Advanced,"advancedQualified");
            Scribe_Values.Look(ref Recipient,"inspirationQualified");
            // Aura is rebuilt for the current map rather than serialized across maps.
        }
    }

    public sealed class StatPart_Legion : StatPart
    {
        public static float Factor(Hediff_LegionRecord r,string stat) {
            if(r==null)return 1;
            int b=r.BloodStage,a=r.AuraRank;
            return stat switch {
                "MoveSpeed" => (1 + .10f * b) * (1 + .05f * a),
                "MeleeDamageFactor" => (1 + .125f * b) * (1 + .05f * a),
                "AimingDelayFactor" => (1 - .05f * b) * (1 - .05f * a),
                "RangedCooldownFactor" => (1 - .10f * b) * (1 - .05f * a),
                _ => 1
            };
        }
        private float Factor(Pawn pawn)
        {
            return Factor(LegionDoctrine.Record(pawn),parentStat.defName);
        }
        public override void TransformValue(StatRequest req, ref float val) { if (req.Thing is Pawn p) val *= Factor(p); }
        public override string ExplanationPart(StatRequest req) => req.Thing is Pawn p && Factor(p) != 1
            ? "PMP.Legion.Stat".Translate(Factor(p).ToStringPercent()) : null;
    }

    // One source index per update cycle, then a fixed recipient budget per tick. No per-unit radial map scans.
    public sealed class LegionMapComponent : MapComponent
    {
        private const int BucketSize = 18, RecipientBudget = 32;
        private readonly Dictionary<(int, int), List<Hediff_LegionRecord>> buckets = new();
        private readonly List<Hediff_LegionRecord> recipients = new();
        private readonly Dictionary<int, Dictionary<Pawn, float>> contributions = new();
        private HashSet<int> rewardedVictims = new();
        private int cursor, nextCycle;
        public int LastCandidatesChecked { get; private set; }
        public LegionMapComponent(Map map) : base(map) { }
        public bool HasContributions(Pawn victim) => contributions.ContainsKey(victim.thingIDNumber);
        public static int Radius(int rank) => rank == 1 ? 10 : rank == 2 ? 14 : rank >= 3 ? 18 : 0;
        public override void MapComponentTick()
        {
            int tick = Find.TickManager.TicksGame;
            if (cursor >= recipients.Count && tick >= nextCycle) { BeginCycle(); nextCycle = tick + 60; }
            for (int n = 0; n < RecipientBudget && cursor < recipients.Count; n++) UpdateRecipient(recipients[cursor++]);
        }
        public void BeginCycle()
        {
            buckets.Clear(); recipients.Clear(); cursor = 0; LastCandidatesChecked = 0;
            foreach (Pawn p in map.mapPawns.AllPawnsSpawned)
            {
                LegionDoctrine.Refresh(p);
                var r = LegionDoctrine.Record(p);
                if (r?.Recipient != true || p.Dead) continue;
                recipients.Add(r);
                if (r.Rank == 0 || p.Downed || p.Faction == null) continue;
                var key = (p.Position.x / BucketSize, p.Position.z / BucketSize);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new();
                list.Add(r);
            }
        }
        public void UpdateRecipient(Hediff_LegionRecord receiver)
        {
            Pawn p = receiver.pawn;
            if (!p.Spawned || p.Map != map || !receiver.Recipient || p.Dead) { receiver.SetAura(0, null); return; }
            int best = 0; Pawn source = null;
            int bx = p.Position.x / BucketSize, bz = p.Position.z / BucketSize;
            for (int x = bx - 1; x <= bx + 1; x++) for (int z = bz - 1; z <= bz + 1; z++)
            {
                if (!buckets.TryGetValue((x,z), out var list)) continue;
                foreach (var r in list)
                {
                    LastCandidatesChecked++;
                    Pawn s = r.pawn; int rank = r.Rank;
                    if (rank <= best || s.Dead || s.Downed || !s.Spawned || s.Map != map || s.Faction != p.Faction) continue;
                    if (s.Position.InHorDistOf(p.Position, Radius(rank)))
                    {
                        best = rank; source = s;
                        if (best == 3) { receiver.SetAura(best, source); return; }
                    }
                }
            }
            receiver.SetAura(best, source);
        }
        public void RecordDamage(Pawn victim, Pawn attacker, float damage, bool? hostileBeforeHit = null)
        {
            if (damage <= 0 || victim == attacker || victim.Faction == null || !(hostileBeforeHit ?? attacker.HostileTo(victim)) ||
                (!victim.RaceProps.Humanlike && !victim.RaceProps.IsMechanoid) || LegionDoctrine.Record(attacker)?.Advanced != true) return;
            if (!contributions.TryGetValue(victim.thingIDNumber, out var values)) contributions[victim.thingIDNumber] = values = new();
            values.TryGetValue(attacker, out float previous); values[attacker] = previous + damage;
        }
        public void Award(Pawn victim)
        {
            if (!contributions.Remove(victim.thingIDNumber, out var values) || !rewardedVictims.Add(victim.thingIDNumber)) return;
            float total = values.Values.Sum();
            float points = Mathf.Clamp(victim.kindDef.combatPower, 20, 200);
            foreach (var pair in values)
                if (!pair.Key.Dead && pair.Key.Faction?.HostileTo(victim.Faction)==true)
                    LegionDoctrine.Record(pair.Key)?.AddMerit(points * pair.Value / total);
        }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref rewardedVictims, "pmpRewardedVictims", LookMode.Value); rewardedVictims ??= new();
        }
    }

    [HarmonyPatch(typeof(PawnCapacityUtility), nameof(PawnCapacityUtility.CalculatePartEfficiency))]
    static class BloodbattlePartEfficiency
    {
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
        {
            var original = AccessTools.Method(typeof(HediffSet), nameof(HediffSet.GetPartHealth));
            var replacement = AccessTools.Method(typeof(LegionDoctrine), nameof(LegionDoctrine.EfficiencyHealth));
            foreach (var instruction in source)
            {
                if (instruction.Calls(original)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; }
                yield return instruction;
            }
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    static class LegionSpawn { static void Postfix(Pawn __instance) => LegionDoctrine.Refresh(__instance); }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SetFaction))]
    static class LegionFactionChange { static void Postfix(Pawn __instance) => LegionDoctrine.Refresh(__instance); }
    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Notify_ApparelChanged))]
    static class LegionApparelChange { static void Postfix(Pawn ___pawn) => LegionDoctrine.Refresh(___pawn); }
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.AddEquipment))]
    static class LegionEquipmentAdd { static void Postfix(Pawn ___pawn) => LegionDoctrine.Refresh(___pawn); }
    [HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Remove))]
    static class LegionEquipmentRemove { static void Postfix(Pawn ___pawn) => LegionDoctrine.Refresh(___pawn); }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    static class LegionDamage
    {
        static void Postfix(Pawn __instance) => LegionDoctrine.Record(__instance)?.UpdateInjury();
    }
    [HarmonyPatch(typeof(Thing),nameof(Thing.TakeDamage))]
    static class LegionAppliedDamage
    {
        internal sealed class Hit { internal Pawn victim, attacker; internal LegionMapComponent map; internal bool hostile; }
        [ThreadStatic] private static Dictionary<Pawn,int> applying;
        internal static bool InProgress(Pawn pawn) => applying?.ContainsKey(pawn)==true;
        static void Prefix(Thing __instance,DamageInfo dinfo,out Hit __state)
        {
            __state=null;
            if(__instance is not Pawn victim||victim.Dead||victim.MapHeld==null)return;
            var component=victim.MapHeld.GetComponent<LegionMapComponent>();
            var attacker=dinfo.Instigator as Pawn;
            if(LegionDoctrine.Record(attacker)?.Advanced!=true&&!component.HasContributions(victim))return;
            __state=new Hit {victim=victim,attacker=attacker,map=component,hostile=attacker?.HostileTo(victim)==true};
            applying??=new();applying.TryGetValue(victim,out int depth);applying[victim]=depth+1;
        }
        static void Postfix(DamageWorker.DamageResult __result,Hit __state)
        {
            if(__state==null)return;
            // Use actual applied damage, including the lethal hit once, never requested/overkill damage.
            if(__state.attacker!=null)__state.map.RecordDamage(__state.victim,__state.attacker,__result.totalDamageDealt,__state.hostile);
            if(__state.victim.Dead)__state.map.Award(__state.victim);
        }
        static Exception Finalizer(Exception __exception,Hit __state)
        {
            if(__state!=null&&applying!=null&&applying.TryGetValue(__state.victim,out int depth))
            {if(depth<=1)applying.Remove(__state.victim);else applying[__state.victim]=depth-1;}
            return __exception;
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    static class LegionKill
    {
        static void Prefix(Pawn __instance, DamageInfo? dinfo, out LegionMapComponent __state)
        {
            __state = __instance.Dead ? null : __instance.MapHeld?.GetComponent<LegionMapComponent>();
        }
        static void Postfix(Pawn __instance, LegionMapComponent __state)
        { if (__instance.Dead&&!LegionAppliedDamage.InProgress(__instance)) __state?.Award(__instance); }
    }
}
