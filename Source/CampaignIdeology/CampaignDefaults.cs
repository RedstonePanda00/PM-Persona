using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Runtime;
using RimWorld;
using Verse;

namespace PMP.Campaign {
    // Scenario.CopyForEditing always returns the base type, so scope the page
    // adjustment to our scenario part, which survives native scenario copying.
    [HarmonyPatch(typeof(Scenario), nameof(Scenario.GetFirstConfigPage))]
    static class CampaignSetupPages {
        static void Postfix(Scenario __instance, ref Page __result) {
            if (!__instance.AllParts.OfType<ScenPart_ShadowOfWar>().Any()) return;
            var first = __result;
            for (var page = first; page != null;) {
                var next = page.next;
                if (page is Page_ChooseIdeoPreset || page is Page_ConfigureIdeo) {
                    if (page.prev != null) page.prev.next = next;
                    else first = next;
                    if (next != null) next.prev = page.prev;
                }
                page = next;
            }
            __result = first;
        }
    }

    [StaticConstructorOnStartup]
    static class CampaignIdeologyBootstrap {
        static CampaignIdeologyBootstrap() {
            new Harmony("redstone.pmpersona.campaign.ideology").PatchAll(typeof(CampaignIdeologyBootstrap).Assembly);
            ScenPart_ShadowOfWar.WorldGenerated += () => {
                Current.Game.GetComponent<CampaignDefaults>().EnsureIdeology();
                Find.Scenario.PostIdeoChosen();
            };
        }
    }

    public sealed class CampaignDefaults : GameComponent {
        private Ideo campaignIdeo;
        private bool ideologyInitialized;
        public CampaignDefaults(Game game) { }
        internal static bool Matches(CampaignController c) =>
            c?.Project?.Id == "pmp-mission-one" && c.CampaignId == "pmp_archive";

        public void EnsureIdeology() {
            if (ideologyInitialized) {
                // Upgrade the previous fixed default without replacing player reforms.
                if (campaignIdeo != null) campaignIdeo.Fluid = true;
                return;
            }
            var player = Faction.OfPlayer;
            campaignIdeo = IdeoGenerator.GenerateIdeo(new IdeoGenerationParms(player.def,
                forcedMemes: new List<MemeDef> { DefDatabase<MemeDef>.GetNamed("Structure_Ideological") },
                forceNoWeaponPreference: true, forNewFluidIdeo: true));
            campaignIdeo.Fluid = true;
            campaignIdeo.name = "PMP_CampaignIdeo_Name".Translate();
            campaignIdeo.adjective = "PMP_CampaignIdeo_Adjective".Translate();
            campaignIdeo.memberName = "PMP_CampaignIdeo_Member".Translate();
            campaignIdeo.MakeMemeberNamePluralDirty();
            foreach (var name in new[] { "Cannibalism_Abhorrent", "OrganUse_HorribleNoSell",
                "Slavery_Abhorrent", "Execution_Abhorrent", "Research_VeryFast" })
                SetPrecept(campaignIdeo, name);
            if (ModsConfig.AnomalyActive) SetPrecept(campaignIdeo, "VoidStudy_VeryEfficient");
            Find.IdeoManager.classicMode = false;
            foreach (var ideo in Find.IdeoManager.IdeosListForReading) ideo.initialPlayerIdeo = false;
            campaignIdeo.initialPlayerIdeo = true;
            campaignIdeo.RegenerateDescription(true);
            Find.IdeoManager.Add(campaignIdeo);
            player.ideos.SetPrimary(campaignIdeo);
            // Includes inherited actors and the unspawned revival pool on an old save.
            var c = Current.Game.GetComponent<CampaignController>();
            foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists
                .Concat(c.Actors.Values).Distinct().Where(p => p.Faction == player && p.RaceProps.Humanlike))
                pawn.ideo?.SetIdeo(campaignIdeo);
            ideologyInitialized = true;
        }

        private static void SetPrecept(Ideo ideo, string name) {
            var def = DefDatabase<PreceptDef>.GetNamed(name);
            foreach (var old in ideo.PreceptsListForReading.Where(p => p.def.issue == def.issue).ToList())
                ideo.RemovePrecept(old, replacing: true);
            ideo.AddPrecept(PreceptMaker.MakePrecept(def), init: true, generatingFor: Faction.OfPlayer.def);
        }

        public override void LoadedGame() {
            var c = Current.Game.GetComponent<CampaignController>();
            if (!Matches(c)) return;
            EnsureIdeology();
        }

        public override void ExposeData() {
            Scribe_Values.Look(ref ideologyInitialized, "pmpCampaignIdeologyInitialized");
            Scribe_References.Look(ref campaignIdeo, "pmpCampaignIdeo");
        }
    }
}
