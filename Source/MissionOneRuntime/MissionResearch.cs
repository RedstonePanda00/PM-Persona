using RimCampaign.Runtime;
using RimWorld;
using Verse;

namespace PMP.Campaign {
    public sealed class OpenResearchDefaults : GameComponent {
        public OpenResearchDefaults(Game game) { }
        public override void LoadedGame() {
            // Run after optional DLC components finish their save migration.
            LongEventHandler.ExecuteWhenFinished(() =>
                MissionResearch.InitializeOpenResearch(Current.Game.GetComponent<CampaignController>()));
        }
    }
    [StaticConstructorOnStartup]
    public static class MissionResearch {
        static MissionResearch(){CampaignController.NodeInitialized+=InitializeOpenResearch;}

        // Actual completion unlocks the separate tuning/share/apply windows as well.
        // Read native research state so old chapter saves also receive the grant.
        public static void GrantForgeResearch(CampaignController c){
            if(!MissionScope.Owns(c,c?.Map,"mission-two")||c.Transitioning||c.EntryPending||
                MissionEffectsMapComponent.For(c.Map)==null||!c.Objects.TryGetValue("m2-forge",out var forge)||
                !forge.Spawned||forge.Destroyed||forge.Map!=c.Map)return;
            var project=DefDatabase<ResearchProjectDef>.GetNamed("PM_PrinterCertificate");
            if(!project.IsFinished)Find.ResearchManager.FinishProject(project,false,null,false);
        }

        public static void InitializeOpenResearch(CampaignController c){
            if(c?.Project?.Id!="pmp-mission-one"||c.CampaignId!="pmp_archive"||c.Node?.Kind!="Open")return;
            // Persist outside RunState, which the entry checkpoint recreates. Also
            // migrates existing open-chapter saves exactly once, never every load.
            const string resetKey="pmp-open-research-baseline-v1";
            if(c.CampaignVariables.ContainsKey(resetKey))return;
            if(ModsConfig.AnomalyActive)
                foreach(var selection in Find.ResearchManager.CurrentAnomalyKnowledgeProjects)
                    if(selection.project!=null)Find.ResearchManager.StopProject(selection.project);
            Find.ResearchManager.ResetAllProgress();
            ResearchUtility.ApplyPlayerStartingResearch();
            c.CampaignVariables[resetKey]=1;
        }
    }
}
