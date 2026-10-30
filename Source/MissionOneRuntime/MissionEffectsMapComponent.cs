using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using Verse;

namespace PMP.Campaign {
    // Persona-only chapter effects. Never auto-created on a normal or open-world map.
    public sealed class MissionEffectsMapComponent : CustomMapComponent,ICampaignMapModule,ICampaignMapOverlay {
        bool enabled;
        string projectId,campaignId,nodeId;
        List<Hediff> effects=new List<Hediff>();
        readonly MissionTwoPresentation presentation=new MissionTwoPresentation();
        readonly MissionWallCracks wallCracks=new MissionWallCracks();
        public MissionEffectsMapComponent(Map map):base(map){}
        CampaignController Controller=>Current.Game?.GetComponent<CampaignController>();
        bool Bound=>MissionScope.Owns(Controller,map)&&
            Controller.Project.Id==projectId&&Controller.CampaignId==campaignId&&Controller.NodeId==nodeId;
        public bool Active=>enabled&&Bound&&!Controller.Transitioning;
        public MissionTwoPresentation Presentation=>presentation;
        public static MissionEffectsMapComponent For(Map map){var m=map?.GetComponent<MissionEffectsMapComponent>();return m?.Active==true?m:null;}
        public static MissionTwoPresentation CurrentPresentation=>For(Current.Game?.GetComponent<CampaignController>()?.Map)?.Presentation;
        public void Attach(CampaignController c,MapComponentSpec spec){
            projectId=c.Project.Id;campaignId=c.CampaignId;nodeId=c.NodeId;enabled=spec.Enabled;wallCracks.Reset();
            if(nodeId=="mission-two"||nodeId=="mission-three"||nodeId=="mission-four")
                Current.Game.GetComponent<PMP_Persona.Romulus.LegionFactionRegistry>().Register(
                    Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec,"Hostile"));
        }
        public void SetEnabled(bool value){if(!value)Detach();else enabled=true;}
        public void Register(Hediff effect){if(Active&&effect.pawn?.MapHeld==map&&!effects.Contains(effect))effects.Add(effect);}
        public bool Owns(Hediff effect)=>Active&&effect.pawn?.MapHeld==map&&effects.Contains(effect);
        public void Detach(){
            enabled=false;presentation.End();wallCracks.Reset();
            foreach(var h in effects.ToList())if(h?.pawn?.health?.hediffSet.hediffs.Contains(h)==true)h.pawn.health.RemoveHediff(h);
            effects.Clear();
        }
        public override void MapRemoved(){Detach();base.MapRemoved();}
        public override void MapComponentTick(){
            if(!Active||Controller.EntryPending)return;
            effects.RemoveAll(h=>h?.pawn?.health?.hediffSet.hediffs.Contains(h)!=true);
            MissionEquipment.Initialize(Controller);
            MissionResearch.GrantForgeResearch(Controller);
            if(nodeId=="mission-two"&&!Controller.State.Variables.ContainsKey("m2-equipped")){
                MissionTwoInitialization.Initialize(Controller);Controller.State.Variables["m2-equipped"]=1;
            }
        }
        public override void MapComponentUpdate(){if(enabled&&!Bound)Detach();}
        public override void MapComponentDraw(){if(Active&&nodeId=="mission-two"&&!Controller.EntryPending&&Find.CurrentMap==map&&map.GetComponent<SceneInteractionMapComponent>()?.Enabled==true)wallCracks.Draw(Controller);}
        public void DrawCampaignOverlay(){if(Active&&Find.CurrentMap==map)presentation.Draw(Controller);}
        public override void ExposeData(){
            base.ExposeData();Scribe_Values.Look(ref enabled,"enabled");Scribe_Values.Look(ref projectId,"project");
            Scribe_Values.Look(ref campaignId,"campaign");Scribe_Values.Look(ref nodeId,"node");
            Scribe_Collections.Look(ref effects,"localEffects",LookMode.Reference);
            if(Scribe.mode==LoadSaveMode.PostLoadInit&&effects==null)effects=new List<Hediff>();
        }
    }
    // The map component owns the lifetime; this hediff only exposes native XML stat factors.
    public sealed class Hediff_MissionLocal : Hediff {
        bool InScope=>MissionEffectsMapComponent.For(pawn?.MapHeld)?.Owns(this)==true;
        public override void PostAdd(DamageInfo? dinfo){base.PostAdd(dinfo);MissionEffectsMapComponent.For(pawn?.MapHeld)?.Register(this);}
        public override HediffStage CurStage=>InScope?base.CurStage:null;
        public override bool ShouldRemove=>base.ShouldRemove||!InScope;
    }
    [HarmonyPatch(typeof(GenScene),nameof(GenScene.GoToMainMenu))]
    static class MissionEffectsExit {
        static void Prefix(){
            if(Current.Game==null)return;
            foreach(var map in Current.Game.Maps)map.GetComponent<MissionEffectsMapComponent>()?.Detach();
        }
    }
}
