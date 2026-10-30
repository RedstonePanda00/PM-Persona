using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Campaign
{
    [StaticConstructorOnStartup]
    public static class MissionBootstrap {static MissionBootstrap(){new Harmony("redstone.pmpersona.campaign").PatchAll(typeof(MissionBootstrap).Assembly);}}
    public static class MissionEquipment
    {
        public static void Initialize(CampaignController c){
            if(!MissionScope.Matches(c)||MissionEffectsMapComponent.For(c.Map)==null||c.EntryPending||c.Transitioning||c.State.Variables.ContainsKey("mason-initialized"))return;
            if(!c.Actors.TryGetValue("hero",out var pawn)||!pawn.Spawned||pawn.Map!=c.Map)return;
            // Defaults belong to a fresh start, not to an inherited character.
            if(!c.Completed.Contains("mission-one")){
                pawn.Name=new NameTriple("David","Mason","Mason");
                pawn.skills.GetSkill(SkillDefOf.Shooting).Level=18;pawn.skills.GetSkill(SkillDefOf.Melee).Level=16;pawn.skills.GetSkill(SkillDefOf.Intellectual).Level=16;
                if(pawn.equipment.Primary==null)pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PM_GunC7Templar")));
            }
            if(c.NodeId=="mission-two"){pawn.drafter.Drafted=true;pawn.drafter.FireAtWill=c.State.Variables.GetValueOrDefault("m2-stage")>=1;}
            pawn.Drawer.renderer.SetAllGraphicsDirty();c.State.Variables["mason-initialized"]=1;
            foreach(var thing in c.Objects.Values.Where(t=>t?.Map==c.Map)){var fuel=thing.TryGetComp<CompRefuelable>();if(fuel!=null)fuel.Refuel(fuel.Props.fuelCapacity);}
            if(!c.Presentation){Find.CameraDriver.SetRootPosAndSize(pawn.DrawPos,18);Find.Selector.Select(pawn);}
        }
    }
    [HarmonyPatch(typeof(Root_Play),nameof(Root_Play.SetupForQuickTestPlay))]
    static class AuditSetup
    {
        static void Postfix(){if(!GenCommandLine.CommandLineArgPassed("pmp-intro-audit")&&!GenCommandLine.CommandLineArgPassed("pmp-mission-two-audit")&&!GenCommandLine.CommandLineArgPassed("pmp-transition-opening-audit")&&!GenCommandLine.CommandLineArgPassed("pmp-mission-three-audit"))return;
            Current.Game.Scenario=DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario;Find.Scenario.PreConfigure();
            var c=Current.Game.GetComponent<CampaignController>();var def=DefDatabase<CampaignDef>.GetNamed("PMP_MissionOne");
            if(GenCommandLine.CommandLineArgPassed("pmp-mission-three-audit"))c.Prepare(def.LoadProject(),def.campaignId,"mission-three");else if(GenCommandLine.CommandLineArgPassed("pmp-mission-two-audit"))c.Prepare(def.LoadProject(),def.campaignId,"mission-two");else c.Prepare(def);
            Find.GameInitData.startingAndOptionalPawns=c.Actors.Values.ToList();Find.GameInitData.startingPawnCount=c.Actors.Count;
            Find.GameInitData.mapSize=c.MapSpec.Width;Find.GameInitData.mapGeneratorDef=DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
        }
    }
}
