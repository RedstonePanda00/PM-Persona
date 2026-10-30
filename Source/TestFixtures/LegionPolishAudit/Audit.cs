using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using PMP.Campaign;
using PMP_Persona;
using PMP_Persona.Romulus;
using PMP_Persona.TacticalSprint;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Tests.Polish {
 [StaticConstructorOnStartup] static class Boot {static Boot(){if(GenCommandLine.CommandLineArgPassed("pmp-legion-polish-audit"))new Harmony("pmp.legion.polish.audit").PatchAll();}}
 [HarmonyPatch(typeof(Root_Play),nameof(Root_Play.SetupForQuickTestPlay))] static class Setup {
  static void Postfix(){if(!GenCommandLine.CommandLineArgPassed("pmp-legion-polish-audit"))return;
   Current.Game.Scenario=DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario;Find.Scenario.PreConfigure();
   var c=Current.Game.GetComponent<CampaignController>();var def=DefDatabase<CampaignDef>.GetNamed("PMP_MissionOne");c.Prepare(def.LoadProject(),def.campaignId,"mission-four");
   Find.GameInitData.startingAndOptionalPawns=c.Actors.Values.ToList();Find.GameInitData.startingPawnCount=c.Actors.Count;Find.GameInitData.mapSize=c.MapSpec.Width;Find.GameInitData.mapGeneratorDef=DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
  }
 }
 [HarmonyPatch(typeof(PersonaCommunicationRenderer),nameof(PersonaCommunicationRenderer.Draw))] static class Comm {
  static void Postfix(CommunicationDrawContext context){var c=Current.Game?.GetComponent<CampaignController>();if(c?.NodeId=="mission-four"&&c.State.Variables.GetValueOrDefault("m4-ready")==0)Audit.OpeningY=context.Panel.y;}
 }
 public sealed class Audit:GameComponent {
  public static float? OpeningY;int stage,frames,startTick,endingTick;bool done,dive,midway,collapse,shockShot;float start;
  List<Pawn> samples=new();Pawn farGuard;IntVec3 destination;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"LegionPolishAudit");
  public Audit(Game game){}
  void Check(bool value,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(value?"PASS ":"FAIL ")+text+"\n");if(!value)throw new Exception(text);}
  void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Folder,name+".png"));
  Pawn Spawn(string kind,IntVec3 pos,Rot4 facing){var p=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind),Faction.OfPlayer);GenSpawn.Spawn(p,pos,Find.CurrentMap,facing);p.jobs.StopAll();p.Rotation=facing;if(p.drafter!=null){p.drafter.Drafted=true;p.drafter.FireAtWill=false;}return p;}
  void Injury(Pawn p){var part=p.RaceProps.body.corePart;var h=HediffMaker.MakeHediff(HediffDefOf.Cut,p,part);h.Severity=part.def.GetMaxHealth(p)*.55f;p.health.AddHediff(h);LegionDoctrine.Record(p)?.UpdateInjury();Check(!p.Dead&&!p.Downed,"nonlethal render-test injury");}
  public override void GameComponentUpdate(){
   if(done||!GenCommandLine.CommandLineArgPassed("pmp-legion-polish-audit"))return;
   var c=Current.Game.GetComponent<CampaignController>();if(c?.Map==null||c.EntryPending||c.Transitioning)return;
   Directory.CreateDirectory(Folder);if(start==0)start=Time.realtimeSinceStartup;
   try {
    var hero=c.Actors["hero"];var map=c.Map;
    if(stage==0){
     if(OpeningY.HasValue&&frames++==3)Shot("01-opening-communication");
     if(c.State.Variables.GetValueOrDefault("m4-ready")!=1)return;
     Check(OpeningY.HasValue&&Math.Abs(OpeningY.Value-260)<1,"opening communications use common Y=260");
     Check(!c.MapSpec.Objectives.Any(o=>o.Id=="m4-recall"),"automatic recall absent from objectives");
     var node=(RomulusSubnode)c.Objects["m4-subnode"];
     Check(!node.GetInspectString().StartsWith("\n")&&!node.GetInspectString().Contains("\n\n"),"node inspect has no empty lines");
     foreach(string name in new[]{"PMP_RomulusRedWall","PMP_RomulusObelisk","PMP_RomulusHub","PMP_RomulusSubnode","PMP_RomulusDome","PMP_RomulusSpearTurret"}){
      var def=DefDatabase<ThingDef>.GetNamed(name);
      if(name=="PMP_RomulusRedWall")
       Check(!def.building.claimable&&def.building.IsDeconstructible&&!def.building.neverBuildable&&def.researchPrerequisites.Single().defName=="PMP_RomulusAnalysis"&&def.costList.Count==1&&def.costList[0].thingDef.defName=="PMP_ArchotechArmorFragment"&&def.costList[0].count==1,"wall research-gated construction uses one fragment");
      else Check(!def.building.claimable&&!def.building.IsDeconstructible&&def.building.neverBuildable,name+" native ownership/designation restrictions");
     }
     Check(!node.ClaimableBy(Faction.OfPlayer).Accepted&&!node.DeconstructibleBy(Faction.OfPlayer).Accepted,"subnode rejects claim and deconstruction");
     Check(hero.GetStatValue(StatDefOf.StaggerDurationFactor,true,0)==0,"heart native hit-stagger factor zero");
     Check(!hero.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.PanicFleeFire,forced:true),"heart blocks burning panic mental state");
     foreach(var cell in CellRect.FromLimits(16,20,65,48)){
      foreach(var thing in cell.GetThingList(map).ToList())if(!(thing is Pawn))thing.Destroy();map.roofGrid.SetRoof(cell,null);map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);
     }
     hero.Position=new IntVec3(20,0,24);hero.Notify_Teleported();hero.jobs.StopAll();hero.drafter.Drafted=true;hero.GetComp<CompTacticalSprint>().SetEnabled(true);
     hero.TryAttachFire(.5f,null);Check(hero.IsBurning(),"fire attached for live sprint test");
     destination=new IntVec3(35,0,24);hero.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,destination));
     Find.CameraDriver.SetRootPosAndSize(hero.DrawPos+new Vector3(5,0,0),12);Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;startTick=Find.TickManager.TicksGame;stage=1;
    } else if(stage==1){
     var sprint=hero.GetComp<CompTacticalSprint>();
     if(hero.Position.x>23&&hero.Position.x<30&&!midway){Check(hero.IsBurning()&&hero.CurJobDef==JobDefOf.Goto,"burning does not interrupt commanded movement or extinguish early");midway=true;}
     if(sprint.Traversal==SprintTraversal.Dive&&hero.pather.MovePercentage>.3f&&!dive){dive=true;Shot("02-fire-dive");}
     if(hero.Position!=destination){if(Find.TickManager.TicksGame-startTick>600)throw new Exception("Sprint timed out "+hero.Position+" "+hero.CurJobDef);return;}
     Check(midway&&dive&&!hero.IsBurning(),"last step dives and extinguishes attached fire");
     hero.TryAttachFire(.3f,null);hero.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(55,0,24)));startTick=Find.TickManager.TicksGame;stage=2;
    } else if(stage==2){
     if(Find.TickManager.TicksGame-startTick<20)return;hero.pather.StopDead();hero.jobs.StopAll();Check(hero.IsBurning(),"cancelled sprint does not extinguish fire");hero.GetAttachment(ThingDefOf.Fire)?.Destroy();
     string[] kinds={"PMP_Warhound","PMP_LegionGuard","PMP_LegionChosen","PMP_OutpostGuard"};
     for(int row=0;row<4;row++)for(int col=0;col<4;col++){
      var rot=new[]{Rot4.South,Rot4.East,Rot4.North,Rot4.West}[col];var p=Spawn(kinds[row],new IntVec3(24+col*5,0,44-row*4),rot);samples.Add(p);
      var r=LegionDoctrine.Record(p);Check(r!=null&&r.EyeAlpha==0,kinds[row]+" healthy eyes transparent");
      Injury(p);Check(r.EyeAlpha>0&&r.Description.Contains(p.LabelShortCap),kinds[row]+" injury opacity and personalized description");
      var eyeNodes=new DynamicPawnRenderNodeSetup_Hediffs().GetDynamicNodes(p,p.Drawer.renderer.renderTree).Where(e=>e.node.Props is LegionEyeProperties).Select(e=>e.node).ToList();
      Check(eyeNodes.Count==p.RaceProps.body.AllParts.Count(b=>b.def==BodyPartDefOf.Eye||b.def.tags.Contains(BodyPartTagDefOf.SightSource))&&eyeNodes.All(n=>n.bodyPart!=null),kinds[row]+" eye nodes bind actual sight organs");
      if(p.RaceProps.Humanlike){
       Check(eyeNodes.Count==2&&eyeNodes.All(n=>n is PawnRenderNode_AttachmentHead),"human has two native head attachment eye nodes");
       var parms=new PawnDrawParms{pawn=p,facing=Rot4.South};
       foreach(var eye in eyeNodes){
        var reference=new PawnRenderNode_AttachmentHead(p,DefDatabase<HediffDef>.GetNamed("BionicEye").RenderNodeProperties[0],p.Drawer.renderer.renderTree){bodyPart=eye.bodyPart};
        var expected=new PawnRenderNodeWorker_HediffEye().OffsetFor(reference,parms,out _);
        var actual=eye.Worker.OffsetFor(eye,parms,out _);
        Check((actual-expected).sqrMagnitude<.000001f,"human eye offset equals native bionic-eye attachment");
       }
       if(col==0)foreach(var helmet in p.apparel.WornApparel.Where(a=>a.def.apparel.layers.Contains(ApparelLayerDefOf.Overhead)).ToList()){p.apparel.Remove(helmet);helmet.Destroy();}
      }
      r.SetAura(3,p);Check(r.TipStringExtra.Contains("38")||r.BloodStage!=2,"combined stat tooltip follows multiplicative factors");
     }
     Find.Selector.ClearSelection();Find.CameraDriver.SetRootPosAndSize(new Vector3(32,0,38),14);Find.TickManager.Pause();stage=3;frames=0;
    } else if(stage==3){
     if(++frames==20)Shot("03-eyes-and-warhound-directions");
     if(frames==25)Find.CameraDriver.SetRootPosAndSize(new Vector3(31.5f,0,44),6);
     if(frames==45)Shot("03a-warhound-close");
     if(frames==50)Find.CameraDriver.SetRootPosAndSize(new Vector3(31.5f,0,32),6);
     if(frames==70)Shot("03b-human-eye-close");
     if(frames<80)return;
     Find.CameraDriver.SetRootPosAndSize(new Vector3(32,0,38),10);
     foreach(var p in samples){p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,p.Position+new IntVec3(10,0,0)));}
     Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;startTick=Find.TickManager.TicksGame;stage=4;
    } else if(stage==4){
     if(Find.TickManager.TicksGame-startTick<36)return;
     Check(samples.Any(p=>LegionDoctrine.Record(p).EyeTrails.Values.Any(h=>h.count>=10)),"moving units collect final rendered eye positions for continuous trails");Shot("04-moving-eye-trails");
     // Keep one distant legion guard to verify the network shutdown ignores range and normal stun immunity.
     farGuard=c.Objects.Values.OfType<Pawn>().FirstOrDefault(p=>p.def.defName=="PMP_LegionGuard"&&p.Faction!=Faction.OfPlayer);
     if(farGuard==null){farGuard=Spawn("PMP_LegionGuard",new IntVec3(60,0,30),Rot4.South);farGuard.SetFaction(c.Objects["m4-subnode"].Faction);}
     foreach(var p in map.mapPawns.AllPawnsSpawned.Where(p=>p.HostileTo(Faction.OfPlayer)&&p!=farGuard).ToList())p.Destroy();
     foreach(var t in map.listerThings.AllThings.OfType<Building_TurretGun>().ToList())t.Destroy();
     farGuard.jobs.StopAll();farGuard.pather.StopDead();farGuard.Position=new IntVec3(285,0,220);farGuard.Notify_Teleported();
     c.State.Variables["m4-stage"]=1;c.State.Timers["m4-install"]=0;
     endingTick=Find.TickManager.TicksGame;stage=5;
    } else if(stage==5){
     var node=(RomulusSubnode)c.Objects["m4-subnode"];
     if(node.Suppressed&&!collapse){
      collapse=true;Check(!hero.Spawned&&!map.mapPawns.AllPawnsSpawned.Any(p=>p.Faction==Faction.OfPlayer),"all friendly pawns leave before node dims");
      Check(farGuard.stances.stunner.Stunned,"distant legion guard stunned despite normal stun immunity");
      Check(Traverse.Create(farGuard.stances.stunner).Field("stunTicksLeft").GetValue<int>()>=295,"shutdown stun lasts five seconds");
      Check(c.State.Phase!="Succeeded","collapse plays before results");Shot("05-node-shockwave");endingTick=Find.TickManager.TicksGame;
     }
     if(collapse&&!shockShot&&Find.TickManager.TicksGame-endingTick>=12){shockShot=true;Shot("05b-shockwave-expanded");Check(node.Graphic.color.r<.4f,"suppressed node uses dark material");}
     if(c.State.Phase!="Succeeded")return;
     Check(collapse&&Find.TickManager.TicksGame-endingTick>=80,"native effect receives game time before results");
     Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no native red errors through render, movement and ending");
     Shot("06-results");File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"PASS");done=true;
    }
    if(Time.realtimeSinceStartup-start>210)throw new Exception("Audit timeout stage="+stage);
   }catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),ex.ToString());Log.Error(ex.ToString());done=true;}
  }
 }
}
