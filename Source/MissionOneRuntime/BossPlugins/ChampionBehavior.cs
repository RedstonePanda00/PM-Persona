using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PMP_Persona.Romulus;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace PMP.Campaign.BossPlugins
{
    public static class ChampionBehavior
    {
        public static bool Supported(Pawn pawn) => pawn?.kindDef?.defName is "PMP_ChampionWarrior" or "PMP_SanctuaryChampion";
        static bool InChapter(Pawn pawn, string node)
        {
            var c = CampaignController.ForMap(pawn?.Map);
            return c?.Project?.Id == "pmp-mission-one" && c.NodeId == node;
        }
        static bool FixedContext(Pawn pawn) => InChapter(pawn, "mission-three") ||
            pawn?.Map?.GetComponent<BossArenaMapComponent>()?.RuleFor(pawn) != null;
        static bool Regular(BossRecord record) => record == null || record.Template.Id == "persona.regularChampion";
        public static bool IsRaid(Pawn pawn) => Supported(pawn) && Regular(BossMapComponent.For(pawn)) &&
            !FixedContext(pawn) && LegionBossModes.Get(pawn) == LegionBossMode.Raid;
        public static void UseFixedLogic(Pawn pawn)
        {
            if (!Supported(pawn)) return;
            var record = BossMapComponent.For(pawn);
            if (!Regular(record)) return;
            bool wasRaid = LegionBossModes.Get(pawn) == LegionBossMode.Raid;
            LegionBossModes.Set(pawn, LegionBossMode.Fixed);
            if (record == null || !wasRaid) return;
            // Restore only movement/weapon behaviors. HP, skills, cooldowns and component state stay intact.
            var preset = Json.Read<BossTemplate>(DefDatabase<BossTemplateDef>.GetNamed("PMP_RegularChampionBoss").templateJson);
            foreach (var phase in record.Template.Phases)
            {
                var original = preset.Phases.FirstOrDefault(p => p.Id == phase.Id);
                if (original != null) phase.Behaviors = Json.Copy(original.Behaviors);
            }
            record.TemplateJson = Json.Write(record.Template);
        }
        public static void UseRaidLogic(Pawn pawn)
        {
            if (!Supported(pawn)) return;
            var record = BossMapComponent.For(pawn);
            if (!Regular(record)) return; // Includes the separate mission-two pursuit champion.
            if (FixedContext(pawn) || LegionBossModes.Get(pawn) == LegionBossMode.Fixed) return;
            LegionBossModes.Set(pawn, LegionBossMode.Raid);
            if (record == null) return;
            record.Active = true;
            if (record.Template.Phases.All(p => p.Behaviors.Count == 0)) return;
            foreach (var phase in record.Template.Phases) phase.Behaviors.Clear();
            record.TemplateJson = Json.Write(record.Template);
        }
        public static void Refresh(Pawn pawn)
        {
            if (!Supported(pawn)) return;
            if (FixedContext(pawn)) UseFixedLogic(pawn);
            else if (IsRaid(pawn) || InChapter(pawn, "mission-five") || InChapter(pawn, "open-campaign") || pawn.GetLord()?.LordJob is LordJob_AssaultColony)
                UseRaidLogic(pawn);
        }
    }

    [HarmonyPatch(typeof(CampaignController), nameof(CampaignController.Place))]
    static class ChampionChapterMode
    {
        static void Postfix(CampaignController __instance, Placement p, Thing __result)
        {
            if (__instance.Project?.Id != "pmp-mission-one" || __result is not Pawn pawn || !ChampionBehavior.Supported(pawn)) return;
            if (__instance.NodeId == "mission-three" && p.Id == "m3-champion") ChampionBehavior.UseFixedLogic(pawn);
            else if (__instance.NodeId is "mission-five" or "open-campaign") ChampionBehavior.UseRaidLogic(pawn);
        }
    }

    [HarmonyPatch(typeof(LegionRaidChampion), nameof(LegionRaidChampion.Apply))]
    static class ChampionExtraRaidMode
    {
        static void Postfix(List<Pawn> pawns)
        {
            if (pawns != null) foreach (var pawn in pawns) ChampionBehavior.UseRaidLogic(pawn);
        }
    }
}
