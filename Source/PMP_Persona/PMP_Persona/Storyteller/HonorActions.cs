using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace PMP_Persona.Storyteller
{
    internal static class HonorActions
    {
        private static HashSet<ThingDef> humanProducts, humanMeat;
        internal static bool HumanProduct(ThingDef def, bool meatOnly = false)
        {
            if (humanProducts == null)
            {
                humanProducts = new HashSet<ThingDef>(); humanMeat = new HashSet<ThingDef>();
                foreach (ThingDef race in DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.race?.Humanlike == true))
                {
                    if (race.race.meatDef != null) { humanMeat.Add(race.race.meatDef); humanProducts.Add(race.race.meatDef); }
                    if (race.race.leatherDef != null) humanProducts.Add(race.race.leatherDef);
                }
            }
            return def != null && (meatOnly ? humanMeat : humanProducts).Contains(def);
        }
        internal static bool ContainsHuman(Thing thing, bool meatOnly = false) => thing != null &&
            (HumanProduct(thing.def, meatOnly) || (!meatOnly && HumanProduct(thing.Stuff)) ||
             thing.TryGetComp<CompIngredients>()?.ingredients.Any(d => HumanProduct(d, meatOnly)) == true);
        internal static MasonKaldalisState State => MasonKaldalisState.Current is { Enabled: true } state ? state : null;
    }

    [HarmonyPatch(typeof(Building_Grave), nameof(Building_Grave.TryAcceptThing))]
    internal static class HonorBurial
    {
        static void Postfix(Building_Grave __instance, Thing thing, bool __result)
        {
            var state = HonorActions.State;
            if (!__result || state == null || __instance.Faction != Faction.OfPlayer || thing is not Corpse corpse || !corpse.InnerPawn.RaceProps.Humanlike) return;
            Pawn pawn = corpse.InnerPawn;
            if (state.Once("buried:" + pawn.thingIDNumber)) state.Award("Burial", pawn.Faction == Faction.OfPlayer ? 3 : 5);
        }
    }
    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    internal static class HonorRecipe
    {
        static IEnumerable<Thing> Postfix(IEnumerable<Thing> __result, Pawn worker, List<Thing> ingredients)
        {
            bool humanCorpse = ingredients.Any(t => t is Corpse c && c.InnerPawn.RaceProps.Humanlike);
            bool humanMeat = ingredients.Any(t => HonorActions.ContainsHuman(t, true));
            bool producedHuman = false, producedFood = false;
            foreach (Thing product in __result)
            {
                producedHuman |= HonorActions.HumanProduct(product.def);
                producedFood |= product.def.ingestible != null;
                yield return product;
            }
            // Iterator completion is the actual production point, not constructing the enumerable.
            if (worker?.Faction != Faction.OfPlayer || !worker.RaceProps.Humanlike) yield break;
            if (humanCorpse && producedHuman) HonorActions.State?.Award("Butchery", -10, worker, "Cannibalism");
            else if (humanMeat && producedFood) HonorActions.State?.Award("HumanFood", -10, worker, "Cannibalism");
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
    internal static class HonorEating
    {
        static void Prefix(Thing __instance, Pawn ingester, out bool __state)
        {
            // Animals exit before any ingredient inspection.
            __state = HonorActions.State != null && MasonKaldalisState.Applies(ingester) && HonorActions.ContainsHuman(__instance, true);
        }
        static void Postfix(Pawn ingester, float __result, bool __state)
        {
            var state = HonorActions.State;
            if (!__state || __result <= 0 || state == null) return;
            if (state.OncePerDay("meal1:" + ingester.thingIDNumber) || state.OncePerDay("meal2:" + ingester.thingIDNumber))
                state.Award("HumanMeatEaten", -3, ingester, "Cannibalism");
        }
    }
    [HarmonyPatch(typeof(GenGuest), nameof(GenGuest.PrisonerRelease))]
    internal static class HonorReleasePrisoner
    {
        static void Prefix(Pawn p, out bool __state) => __state = p.RaceProps.Humanlike && p.IsPrisonerOfColony;
        static void Postfix(Pawn p, bool __state)
        {
            var state = HonorActions.State;
            if (__state && state != null && !p.IsPrisonerOfColony && state.Once("released:" + p.thingIDNumber)) state.Award("Release", state.WasTended(p) ? 6 : 4);
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    internal static class HonorReleasedPawnExit
    {
        static void Prefix(Pawn __instance, out int __state)
        {
            __state = !__instance.Dead && __instance.RaceProps.Humanlike && __instance.guest?.Released == true
                ? (__instance.IsSlaveOfColony ? 2 : __instance.IsPrisonerOfColony ? 1 : 0) : 0;
        }
        static void Postfix(Pawn __instance, int __state)
        {
            var state = HonorActions.State;
            if (__state == 0 || state == null || __instance.Spawned || __instance.Dead) return;
            if (__state == 2 && state.Once("freed:" + __instance.thingIDNumber)) state.Award("Emancipation", 6);
            if (__state == 1 && state.Once("released:" + __instance.thingIDNumber)) state.Award("Release", state.WasTended(__instance) ? 6 : 4);
        }
    }
    [HarmonyPatch(typeof(GenGuest), nameof(GenGuest.SlaveRelease))]
    internal static class HonorReleaseSlave
    {
        static void Prefix(Pawn p, out bool __state) => __state = p.RaceProps.Humanlike && p.IsSlaveOfColony;
        static void Postfix(Pawn p, bool __state)
        {
            var state = HonorActions.State;
            if (__state && state != null && !p.IsSlaveOfColony && state.Once("freed:" + p.thingIDNumber)) state.Award("Emancipation", 6);
        }
    }
    [HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
    internal static class HonorTend
    {
        static void Prefix(Pawn patient, out bool __state) => __state = patient?.health?.hediffSet?.hediffs.Any(h => h.TendableNow()) == true;
        static void Postfix(Pawn doctor, Pawn patient, bool __state)
        {
            var state = HonorActions.State;
            if (!__state || state == null || doctor?.Faction != Faction.OfPlayer || patient == null || !patient.RaceProps.Humanlike) return;
            state.NotifyTendedEnemy(patient);
            if (MasonKaldalisState.Applies(patient) && state.OncePerDay("tended:" + patient.thingIDNumber)) state.Award("Treatment", 2);
        }
    }
    [HarmonyPatch(typeof(Recipe_RemoveBodyPart), nameof(Recipe_RemoveBodyPart.ApplyThoughts))]
    internal static class HonorOrganHarvest
    {
        static void Postfix(Pawn pawn, Pawn billDoer)
        {
            if (pawn.RaceProps.Humanlike && billDoer?.Faction == Faction.OfPlayer) HonorActions.State?.Award("OrganHarvest", -15, billDoer, "OrganUse");
        }
    }
    [HarmonyPatch(typeof(ThoughtUtility), nameof(ThoughtUtility.GiveThoughtsForPawnExecuted))]
    internal static class HonorExecution
    {
        static void Prefix(Pawn victim, Pawn executioner, PawnExecutionKind kind)
        {
            if (kind != PawnExecutionKind.OrganHarvesting && victim.RaceProps.Humanlike && victim.IsPrisonerOfColony && executioner?.Faction == Faction.OfPlayer)
                HonorActions.State?.Award("Execution", -8, executioner, "Execution");
        }
    }
    [HarmonyPatch(typeof(Tradeable), nameof(Tradeable.ResolveTrade))]
    internal static class HonorTrade
    {
        static void Prefix(Tradeable __instance, out string __state)
        {
            __state = null;
            if (HonorActions.State == null || __instance.CountToTransfer == 0) return;
            Thing thing = __instance.AnyThing;
            if (thing is Pawn pawn && pawn.RaceProps.Humanlike && __instance.ActionToDo == TradeAction.PlayerSells) __state = "Slavery";
            else if (thing is Corpse corpse && corpse.InnerPawn.RaceProps.Humanlike || HonorActions.ContainsHuman(thing)) __state = "Cannibalism";
            else if (thing?.def.thingCategories?.Any(c => c.defName == "BodyPartsNatural" || c.Parents.Any(p => p.defName == "BodyPartsNatural")) == true) __state = "OrganUse";
        }
        static void Postfix(string __state)
        {
            if (__state != null) HonorActions.State?.Award("TradeHuman", -12, TradeSession.playerNegotiator, __state);
        }
    }
    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
    internal static class HonorConquest
    {
        static void Prefix(Settlement factionBase, out bool __state) => __state = factionBase.Faction?.HostileTo(Faction.OfPlayer) == true && factionBase.HasMap && factionBase.Map.mapPawns.FreeColonistsSpawned.Any() && HonorActions.State?.HasPlayerCombat(factionBase.Map) == true;
        static void Postfix(Settlement factionBase, bool __state)
        {
            var state = HonorActions.State;
            if (__state && factionBase.Destroyed && state != null && state.Once("settlement:" + factionBase.ID)) state.Award("Conquest", 30);
        }
    }
    // Explicit metadata for genuine aid/refugee quests, independent of quest titles.
    public class HonorQuestExtension : DefModExtension
    {
        public float successHonor;
        public float acceptHonor;
    }
    [HarmonyPatch(typeof(Quest), nameof(Quest.End))]
    internal static class HonorQuestComplete
    {
        static void Postfix(Quest __instance, QuestEndOutcome outcome)
        {
            var state = HonorActions.State;
            float value = __instance.root?.GetModExtension<HonorQuestExtension>()?.successHonor ?? 0;
            if (state != null && value > 0 && outcome == QuestEndOutcome.Success && __instance.EverAccepted && state.Once("quest:" + __instance.id)) state.Award("AidQuest", value);
        }
    }
    [HarmonyPatch(typeof(Quest), nameof(Quest.Accept))]
    internal static class HonorQuestAccept
    {
        static void Postfix(Quest __instance)
        {
            var state = HonorActions.State;
            float value = __instance.root?.GetModExtension<HonorQuestExtension>()?.acceptHonor ?? 0;
            if (state != null && value > 0 && __instance.EverAccepted && state.Once("accepted:" + __instance.id)) state.Award("Refugees", value);
        }
    }
    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.EndCurrentJob))]
    internal static class HonorRescue
    {
        static void Prefix(Pawn ___pawn, JobCondition condition)
        {
            var state = HonorActions.State;
            Job job = ___pawn.CurJob;
            if (state == null || condition != JobCondition.Succeeded || ___pawn.Faction != Faction.OfPlayer || job?.def != JobDefOf.Rescue) return;
            Pawn patient = job.targetA.Pawn;
            if (MasonKaldalisState.Applies(patient) && patient.InBed() && state.OncePerDay("rescued:" + patient.thingIDNumber)) state.Award("Rescue", 2);
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class HonorPrisonerNeglect
    {
        static void Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit)
        {
            var state = HonorActions.State;
            if (state == null || !__instance.RaceProps.Humanlike || (!__instance.IsPrisonerOfColony && !__instance.IsSlaveOfColony) || dinfo.HasValue) return;
            string cause = exactCulprit?.def?.defName;
            if (cause == "Malnutrition" || cause == "Heatstroke" || cause == "Hypothermia")
                if (state.Once("neglect:" + __instance.thingIDNumber)) state.Award("Neglect", -6);
        }
    }
    [HarmonyPatch(typeof(LordJob_Ritual), nameof(LordJob_Ritual.ApplyOutcome))]
    internal static class HonorFuneral
    {
        static void Prefix(LordJob_Ritual __instance, float progress, bool cancelled, bool ___ended, out bool __state)
        {
            __state = !___ended && !cancelled && progress >= 1f && __instance.lord?.faction == Faction.OfPlayer && __instance.Ritual?.def?.ritualPatternBase?.defName == "FuneralStandard";
        }
        static void Postfix(bool __state) { if (__state) HonorActions.State?.Award("Funeral", 5); }
    }
}
