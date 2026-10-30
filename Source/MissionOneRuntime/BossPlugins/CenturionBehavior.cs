using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace PMP.Campaign.BossPlugins
{
    public static class CenturionBehavior
    {
        public static bool IsRaid(Pawn pawn)
        {
            if (pawn?.kindDef?.defName != "PMP_LegionCenturion") return false;
            var record = BossMapComponent.For(pawn);
            if (record != null && record.Template.Id != "persona.regularCenturion") return false;
            return LegionBossModes.Get(pawn) == LegionBossMode.Raid;
        }
        public static void UseRaidLogic(Pawn pawn)
        {
            if (pawn?.kindDef?.defName != "PMP_LegionCenturion") return;
            var record = BossMapComponent.For(pawn);
            if (record != null && record.Template.Id != "persona.regularCenturion") return;
            LegionBossModes.Set(pawn, LegionBossMode.Raid);
            if (record == null) return;
            record.Active = true;
            // Keep independent Boss HP, but movement and shooting belong to the native raid Lord/AI.
            if (record.Template.Phases.All(p => p.Behaviors.Count == 0)) return;
            foreach (var phase in record.Template.Phases) phase.Behaviors.Clear();
            record.TemplateJson = Json.Write(record.Template);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    static class CenturionRaidArrival
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Pawn __instance, bool respawningAfterLoad)
        {
            if (respawningAfterLoad) return;
            LegionBossModes.Attach(__instance);
            if (CenturionBehavior.IsRaid(__instance)) CenturionBehavior.UseRaidLogic(__instance);
            ChampionBehavior.Refresh(__instance);
        }
    }

    public sealed class CenturionBehaviorMapComponent : MapComponent
    {
        public CenturionBehaviorMapComponent(Map map) : base(map) { }
        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 30 != 0) return;
            foreach (var record in map.GetComponent<BossMapComponent>().Records)
            {
                var pawn = record.Proxy;
                // Authored encounters retain their own activation triggers and restrictions.
                if (record.Ended || record.Active || record.Template.Id != "persona.regularCenturion" ||
                    pawn?.Spawned != true || pawn.Dead || CenturionBehavior.IsRaid(pawn)) continue;
                if (pawn.Faction == Faction.OfPlayer || map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer)
                    .Any(p => !p.Dead && !p.Downed && p.HostileTo(pawn) && p.Position.DistanceToSquared(pawn.Position) <= 100))
                    record.Active = true;
            }
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    static class CenturionRaidLogic
    {
        [HarmonyPriority(Priority.Last)]
        static void Postfix(IncidentWorker_Raid __instance, bool __result, List<Pawn> pawns)
        {
            if (!__result || __instance is not IncidentWorker_RaidEnemy || pawns == null) return;
            foreach (var pawn in pawns)
            {
                CenturionBehavior.UseRaidLogic(pawn);
                ChampionBehavior.UseRaidLogic(pawn);
            }
        }
    }

    [HarmonyPatch(typeof(LordMaker), nameof(LordMaker.MakeNewLord))]
    static class CenturionAssaultWaveLogic
    {
        static void Postfix(Lord __result)
        {
            // Campaign assault waves can use a Lord directly without a native raid incident.
            if (__result?.LordJob is not LordJob_AssaultColony) return;
            foreach (var pawn in __result.ownedPawns)
            {
                CenturionBehavior.UseRaidLogic(pawn);
                ChampionBehavior.UseRaidLogic(pawn);
            }
        }
    }

    // The framework's BossThinking prefix normally suppresses native think trees for every Boss.
    // Raid centurions explicitly retain that path while still using the framework's Boss health.
    [HarmonyPatch]
    static class CenturionNativeRaidThinking
    {
        static MethodBase TargetMethod() => AccessTools.Method(
            typeof(BossMapComponent).Assembly.GetType("RimCampaign.Runtime.BossThinking"), "Prefix");
        static bool Prefix([HarmonyArgument(0)] Pawn pawn, ref bool __result)
        {
            if (!CenturionBehavior.IsRaid(pawn) && !ChampionBehavior.IsRaid(pawn)) return true;
            __result = true;
            return false;
        }
    }
}
