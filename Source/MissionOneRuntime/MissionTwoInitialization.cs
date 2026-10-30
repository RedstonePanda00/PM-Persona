using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using Verse;

namespace PMP.Campaign {
    public static class MissionTwoInitialization {
        public static void Initialize(CampaignController c){
            if(!MissionScope.Matches(c,"mission-two")||MissionEffectsMapComponent.For(c.Map)==null)return;
            foreach(var pawn in c.Objects.Values.OfType<Pawn>().Distinct()){
                if(pawn.Dead||pawn.Destroyed||!pawn.Spawned||pawn.Map!=c.Map)continue;
                string kind=pawn.kindDef.defName,weapon=kind=="PMP_LegionCenturion"?"PMP_CenturionMinigun":kind=="PMP_ChampionWarrior"?"MeleeWeapon_LongSword":kind=="PMP_ImperialKnight"?"Gun_ChargeRifle":kind=="PMP_OutpostGuard"?"PMP_BelieverSpearRifle":null;
                if(weapon==null)continue;
                if(pawn.equipment.Primary==null){var def=DefDatabase<ThingDef>.GetNamed(weapon);pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(def,def.MadeFromStuff?ThingDefOf.Plasteel:null));}
                pawn.skills.GetSkill(SkillDefOf.Shooting).Level=kind=="PMP_OutpostGuard"?8:14;pawn.skills.GetSkill(SkillDefOf.Melee).Level=16;
                if(kind=="PMP_ImperialKnight"||kind=="PMP_ChampionWarrior"){
                    var def=DefDatabase<HediffDef>.GetNamed(kind=="PMP_ImperialKnight"?"PMP_KnightProtection":"PMP_ChampionPhysiology");
                    if(!pawn.health.hediffSet.HasHediff(def))pawn.health.AddHediff(def);
                }
                if(kind=="PMP_ImperialKnight"){
                    pawn.Name=new NameSingle(RcText.T("帝国骑士"));var escort=c.Map.GetComponent<EscortMapComponent>();if(escort!=null)escort.Guide="knight";
                }
            }
        }
    }
    [CampaignModule("pmp.m2.setup","Action","Initialize newly arrived mission-two units.")]
    public sealed class InitializeMissionTwo:IActionModule {public bool Execute(Act a,IHost host,string source,double delta){MissionTwoInitialization.Initialize((CampaignController)host);return true;}}
    [CampaignModule("pmp.m2.identity","Action","Reveal the knight's identity.")]
    public sealed class RevealKnight:IActionModule {public bool Execute(Act a,IHost host,string source,double delta){((CampaignController)host).ResolveTriggerPawn("knight").Name=new NameSingle(RcText.T("卡珊德拉"));return true;}}
    [CampaignModule("pmp.m2.focus","Action","Update an authored camera marker to the actor's current position.")]
    public sealed class FocusActor:IActionModule {public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var pawn=c.ResolveTriggerPawn(a.Target);c.MapSpec.Objects.Single(o=>o.Id==a.Text).Position=new Cell{X=pawn.Position.x,Z=pawn.Position.z};return true;}}
}
