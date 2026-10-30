using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using PMP.Campaign;
using PMP_Persona;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Tests {
 [StaticConstructorOnStartup] public static class FourAuditBootstrap {static FourAuditBootstrap(){new Harmony("pmp.mission-four.audit").PatchAll();}}
 [HarmonyPatch(typeof(Root_Play),nameof(Root_Play.SetupForQuickTestPlay))]
 static class FourSetup {
  static void Postfix(){if(!GenCommandLine.CommandLineArgPassed("pmp-mission-four-audit"))return;
   Current.Game.Scenario=DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario;Find.Scenario.PreConfigure();
   var c=Current.Game.GetComponent<CampaignController>();var def=DefDatabase<CampaignDef>.GetNamed("PMP_MissionOne");c.Prepare(def.LoadProject(),def.campaignId,"mission-four");
   Find.GameInitData.startingAndOptionalPawns=c.Actors.Values.ToList();Find.GameInitData.startingPawnCount=c.Actors.Count;Find.GameInitData.mapSize=c.MapSpec.Width;Find.GameInitData.mapGeneratorDef=DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
  }
 }
 public sealed class FourAudit:GameComponent {
  int stage,wideFrames,recallFrames;bool done,wide,warp,arrival,recallShot;float started;double installationStarted;int movedAt;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"MissionFourAudit");
  public FourAudit(Game game){}
  void Check(bool ok,string message){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
  void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Folder,name+".png"));
  void Clear(CampaignController c,string group,bool core){foreach(var p in c.MapSpec.Objects.Where(o=>o.Group==group&&o.SpawnAtStart&&((o.Def.Name=="PMP_RomulusObelisk")==core)))if(c.Objects.TryGetValue(p.Id,out var t)&&!t.Destroyed)t.Destroy();}
  public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref stage,"stage");Scribe_Values.Look(ref wide,"wide");Scribe_Values.Look(ref warp,"warp");Scribe_Values.Look(ref arrival,"arrival");}
  public override void GameComponentUpdate(){
   if(done||!GenCommandLine.CommandLineArgPassed("pmp-mission-four-audit"))return;
   var c=Current.Game.GetComponent<CampaignController>();if(c?.Map==null||c.EntryPending||c.Transitioning)return;
   Directory.CreateDirectory(Folder);if(started==0)started=Time.realtimeSinceStartup;
   try {
    var hero=c.Actors["hero"];Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
    if(stage==0){
     if(!hero.Spawned&&Find.CameraDriver.RootSize>100&&!wide&&++wideFrames>10){wide=true;Check(Find.Camera.farClipPlane>Find.Camera.transform.position.y,"overview camera sees terrain beyond the normal zoom range");Shot("01-overview");}
     if(!hero.Spawned&&c.Objects.Keys.Any(k=>k.StartsWith("warp:"))&&!warp){warp=true;Shot("02-warp-in");}
     if(hero.Spawned&&!arrival){arrival=true;Shot("03-mason-arrived");}
     if(c.State.Variables.GetValueOrDefault("m4-ready")!=1)return;
     Check(wide&&warp&&arrival,"overview, native warp and Mason arrival all rendered before control");
     Check(c.Node.OpeningTitle.Title=="拔线"&&c.NodeId=="mission-four","fourth mission title and node");
     Check(c.Actors.Count==1&&hero.Spawned&&hero.Drafted&&hero.drafter.FireAtWill,"only Mason starts, drafted and able to return fire");
     Check(hero.TryGetComp<CompNeverDowned>()!=null,"Mason has existing NeverDowned component");
     Check(!hero.health.hediffSet.hediffs.Any(h=>h is Hediff_MissionLocal),"mission-three protection absent");
     Check(!c.Objects["m4-subnode"].def.useHitPoints&&DefDatabase<ThingDef>.GetNamed("PMP_RomulusHub").useHitPoints,"fourth node has no HP; third hub remains destructible");
     var occupied=new Dictionary<IntVec3,string>();
     foreach(var p in c.MapSpec.Objects.Where(o=>o.Kind=="Thing")){
      var def=DefDatabase<ThingDef>.GetNamed(p.Def.Name);
      foreach(var cell in GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),def.size)){
       Check(cell.InBounds(c.Map)&&!occupied.ContainsKey(cell),"clear footprint "+p.Id+" "+cell);occupied[cell]=p.Id;
      }
     }
     Check(c.Objects.Values.OfType<Building_Bookcase>().Count()==6&&c.Objects.Values.OfType<Building_Bookcase>().All(b=>b.GetDirectlyHeldThings().Count==10),"six bookcases filled with technical schematics");
     Check(hero.CanReach(c.Objects["m4-subnode"],PathEndMode.Touch,Danger.Deadly),"Mason can reach the installation node");
     foreach(var p in c.MapSpec.Objects.Where(o=>o.SpawnAtStart&&o.Faction=="Hostile"&&!o.Group.StartsWith("m4-outpost-")&&o.Kind=="PawnKind"))c.Objects[p.Id].Destroy();
     foreach(var p in c.MapSpec.Objects.Where(o=>o.SpawnAtStart&&o.Def.Name=="PMP_RomulusSpearTurret"&&!o.Group.StartsWith("m4-outpost-")))c.Objects[p.Id].Destroy();
     Clear(c,"m4-outpost-2",true);
     Check(!c.TestCondition(new Condition{Op="rc.targetsCleared",Key="m4-outpost-2"}),"core destroyed alone does not reward");
     stage=1;GameDataSaveLoader.SaveGame("MissionFour-Partial");
     LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("MissionFour-Partial"),"LoadingLongEvent",false,null);return;
    }
    if(stage==1){
     Check(c.State.Variables.ContainsKey("destroyed-placement:m4-outpost-2-obelisk"),"destroyed core survives a real save/load");
     Check(!c.TestCondition(new Condition{Op="rc.targetsCleared",Key="m4-outpost-2"}),"guards still prevent reward after load");
     Check(hero.TryGetComp<CompNeverDowned>()!=null,"NeverDowned restored after load");
     Clear(c,"m4-outpost-3",false);
     Check(!c.TestCondition(new Condition{Op="rc.targetsCleared",Key="m4-outpost-3"}),"guards cleared alone do not reward");
     Clear(c,"m4-outpost-3",true);Clear(c,"m4-outpost-2",false);Clear(c,"m4-outpost-1",true);Clear(c,"m4-outpost-1",false);
     stage=2;
    }
    if(stage==2){
     var allies=c.Objects.Where(k=>k.Key.Contains("-ally-")).Select(k=>k.Value).OfType<Pawn>().Distinct().Where(p=>p.Spawned).ToList();
     if(allies.Count<6)return;
     Check(allies.All(p=>p.Drafted&&p.Faction==Faction.OfPlayer),"all six reinforcements arrive drafted for player");
     Check(allies.Count(p=>p.kindDef.defName=="PM_Sentinel")==3&&allies.Single(p=>p.kindDef.defName=="PM_Sentry").equipment.Primary.def.defName=="PMP_HardlightRifle","first outpost composition and hardlight rifle");
     Check(c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Pylon")).Count==3,"three pylons completed building warp");
     Check(c.Actors.Count==1,"reinforcement pawns are not inherited actors");
     Check(Enumerable.Range(1,3).All(i=>c.State.Firings["m4-outpost-"+i+"-clear"]==1),"outposts reward exactly once in nonsequential order");
     allies.Single(p=>p.kindDef.defName=="PMPH_Kaldalis").Kill(null);
     Check(c.CanFinishStory,"Kaldalis shell loss does not block finishing");
     hero.Position=new IntVec3(166,0,178);hero.Notify_Teleported();
     Find.CameraDriver.SetRootPosAndSize(hero.DrawPos,20);Shot("04-node-and-reinforcements");
     var interaction=c.Map.GetComponent<SceneInteractionMapComponent>();var spec=c.MapSpec.Interactions.Single();
     Check(interaction.Commands(c.Objects["m4-subnode"]).Any()&&interaction.Options(c.Objects["m4-subnode"],hero).Any(),"both Gizmo and right-click installation paths available");
     Check(interaction.Begin(spec,hero),"native 120-tick installation job accepted");stage=3;
    }
    if(stage==3){
     if(c.State.Variables.GetValueOrDefault("m4-stage")!=1)return;
     installationStarted=c.State.Elapsed;
     Check(!c.Presentation&&!c.Freeze,"automatic installation does not take player control");
     Check(!c.Map.GetComponent<SceneInteractionMapComponent>().Begin(c.MapSpec.Interactions.Single(),hero),"one-shot installation cannot be restarted");
     var job=JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(159,0,174));job.playerForced=true;hero.jobs.TryTakeOrderedJob(job);movedAt=Find.TickManager.TicksGame;stage=4;
    }
    if(stage==4){
     // Isolate natural combat for deterministic sequence testing; native wave
     // spawning, jobs, timer and all presentation remain live.
     foreach(var pawn in c.Map.mapPawns.AllPawnsSpawned.Where(p=>p.HostileTo(Faction.OfPlayer)).ToList())pawn.Destroy();
     if(Find.TickManager.TicksGame>movedAt+240&&movedAt>0){Check(hero.Position==new IntVec3(159,0,174),"Mason moves while the automatic installation timer advances");movedAt=0;hero.drafter.Drafted=false;hero.drafter.FireAtWill=false;}
     if(c.State.Variables.GetValueOrDefault("m4-stage")==2&&!recallShot){recallShot=true;Check(c.State.Elapsed-installationStarted>=59.5,"full sixty-second automatic installation");Check(!hero.Drafted&&!hero.drafter.FireAtWill,"later enemy waves preserve player changes to draft/fire controls");Check(((RomulusSubnode)c.Objects["m4-subnode"]).Suppressed,"node persists in suppressed state");Shot("05-anchor-complete");}
     if(c.State.Variables.GetValueOrDefault("m4-stage")==2&&!c.Freeze&&c.Communications.Messages.Count==0){recallFrames++;if(recallFrames%10==0)Shot("recall-"+recallFrames.ToString("D3"));}
     if(c.State.Phase!="Succeeded")return;
     Check(Enumerable.Range(1,3).All(i=>c.State.Firings.ContainsKey("m4-wave-"+i+"-trigger")),"all three authored reinforcement waves fired");
     Check(!hero.Spawned&&!c.Map.mapPawns.AllPawnsSpawned.Any(p=>p.Faction==Faction.OfPlayer),"all surviving allies recalled from their current positions");
     Check(c.State.Objectives["m4-anchor"]=="Complete"&&c.State.Objectives["m4-recall"]=="Complete","suppression and recall objectives complete");
     Shot("06-results");Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native errors through save/load, rewards, installation and recall");
     File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Native fourth-mission audit complete. Combat deliberately isolated; not a balance verdict.");done=true;
     return;
    }
    if(Time.realtimeSinceStartup-started>220)throw new Exception("Mission four audit timeout stage="+stage);
   }catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),ex.ToString());done=true;Log.Error(ex.ToString());}
  }
 }
}
