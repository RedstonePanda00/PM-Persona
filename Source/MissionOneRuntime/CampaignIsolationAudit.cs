using System;
using HarmonyLib;
using ProtossMech.GunSmith;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using Verse;

namespace PMP.Campaign {
    // Invoked only by the opt-in native mission-two audit. Restore all probe mutations immediately.
    static class CampaignIsolationAudit {
        public static void Run(CampaignController c,Action<bool,string> check){
            var project=c.Project;var node=c.Node;var map=c.Map;
            string projectId=project.Id,nodeKind=node.Kind,campaignId=c.CampaignId;
            bool transition=c.Transitioning;
            var hero=c.Actors["hero"];
            var forge=c.Objects["m2-forge"].TryGetComp<CompGunSmithForge>();
            bool ForgeAllowed()=>Traverse.Create(forge).Property<bool>("ResearchSatisfied").Value;
            var runtime=typeof(CampaignController).Assembly;
            bool NativeGameOver()=> (bool)AccessTools.Method(runtime.GetType("RimCampaign.Runtime.CampaignGameOver"),"Prefix").Invoke(null,null);
            object NeedsOverride()=>AccessTools.Method(runtime.GetType("RimCampaign.Runtime.Overrides"),"For").Invoke(null,new object[]{hero});
            var protection=hero.health.AddHediff(DefDatabase<HediffDef>.GetNamed("PMP_SceneProtection"));
            MissionResearch.GrantForgeResearch(c);
            try {
                check(MissionScope.Owns(c,map,"mission-two"),"isolation: exact Persona story scope accepted");
                check(DefDatabase<ResearchProjectDef>.GetNamed("PM_PrinterCertificate").IsFinished,"isolation: tutorial grants actual printer research");
                check(ForgeAllowed(),"isolation: supplied mission forge still opens native gunsmith");
                check(protection is Hediff_MissionLocal&&protection.CurStage!=null&&!protection.ShouldRemove,"isolation: local protection active in its owning story map");
                check(!NativeGameOver(),"isolation: story map retains scripted defeat handling");
                project.Id="unrelated-project";
                check(!MissionScope.Matches(c),"isolation: matching node name in another project grants no Persona permission");
                check(protection.CurStage==null&&protection.ShouldRemove,"isolation: temporary protection rejects another project");
                project.Id=projectId;c.CampaignId="unrelated-campaign";
                check(!MissionScope.Matches(c),"isolation: matching project and node in another campaign rejected");
                c.CampaignId=campaignId;
                c.Map=new Map();
                check(!MissionScope.Owns(c,map),"isolation: objects on another map cannot borrow campaign permissions");
                check(protection.CurStage==null&&protection.ShouldRemove,"isolation: protected pawn loses effects outside owning map");
                check(map.GetComponent<EscortMapComponent>().GuidePawn==null,"isolation: old escort component cannot resolve new-map actors");
                c.Map=map;node.Kind="Open";
                check(!MissionScope.Matches(c),"isolation: open node has no chapter initialization");
                check(protection.CurStage==null&&protection.ShouldRemove,"isolation: open node has no story invulnerability");
                check(NativeGameOver()&&NeedsOverride()==null,"isolation: open node uses native defeat and needs/work rules");
                node.Kind=nodeKind;c.Project=null;
                MissionTwoInitialization.Initialize(c);
                check(!MissionScope.Matches(c)&&NativeGameOver()&&NeedsOverride()==null,"isolation: ordinary game with no campaign falls through native paths");
                check(protection.CurStage==null&&protection.ShouldRemove,"isolation: ordinary game cannot retain campaign protection");
                c.Project=project;c.Transitioning=true;
                check(protection.CurStage==null,"isolation: permissions inactive during transition");
                c.Transitioning=transition;
                check(ForgeAllowed()&&protection.CurStage!=null,"isolation: probe restored owning mission state");
                var extra=(ThingWithComps)ThingMaker.MakeThing(c.Objects["m2-forge"].def);
                check(Traverse.Create(extra.TryGetComp<CompGunSmithForge>()).Property<bool>("ResearchSatisfied").Value,"isolation: real research unlocks native forges without special permission");
                extra.Destroy();
                var ordinary=Profiles.Generate(new DefRef{Kind="PawnKindDef",Name="PMP_ChampionWarrior"},Faction.OfPlayer);
                GenSpawn.Spawn(ordinary,new IntVec3(4,0,20),map);
                check(BossMapComponent.For(ordinary)==null&&!ordinary.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("PMP_KnightProtection")),"isolation: same-kind unregistered pawn has no boss record or knight protection");
                ordinary.Kill(null);
                check(ordinary.Dead,"isolation: unregistered champion can die through native Kill");
                ordinary.Corpse?.Destroy();
                var effects=map.GetComponent<MissionEffectsMapComponent>();
                check(effects!=null&&typeof(CustomMapComponent).IsAssignableFrom(effects.GetType()),"isolation: Persona chapter effects are explicitly attached custom map component");
                effects.SetEnabled(false);
                check(!hero.health.hediffSet.hediffs.Contains(protection),"isolation: disabling map component removes owned hediff");
                effects.SetEnabled(true);
                check(ForgeAllowed()&&!hero.health.hediffSet.hediffs.Contains(protection),"isolation: real research survives re-enable without resurrecting expired effects");
            } finally {
                c.Project=project;project.Id=projectId;node.Kind=nodeKind;c.Map=map;c.CampaignId=campaignId;c.Transitioning=transition;
                if(hero.health.hediffSet.hediffs.Contains(protection))hero.health.RemoveHediff(protection);
            }
        }
    }
}
