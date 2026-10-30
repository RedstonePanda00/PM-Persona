using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using PMP_Persona.GestaltZero;
using RimCampaign.Runtime;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 [StaticConstructorOnStartup] static class OpenBootstrap {
  static OpenBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-open-campaign-audit"))new Harmony("pmp.open.audit").PatchAll();}
 }
 [HarmonyPatch(typeof(Root_Play),nameof(Root_Play.SetupForQuickTestPlay))] static class OpenSetup {
  static void Postfix(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-open-campaign-audit"))return;
   Current.Game.Scenario=DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario;Find.Scenario.PreConfigure();
   var c=Current.Game.GetComponent<CampaignController>();var def=DefDatabase<CampaignDef>.GetNamed("PMP_MissionOne");c.Prepare(def.LoadProject(),def.campaignId,"mission-five");
   if(GenCommandLine.CommandLineArgPassed("pmp-open-fast-audit"))c.MapSpec.Entry[0].Steps.RemoveAll(a=>a.Id=="m5-briefing");
   Find.GameInitData.startingAndOptionalPawns=c.Actors.Values.ToList();Find.GameInitData.startingPawnCount=c.Actors.Count;
   Find.GameInitData.mapSize=325;Find.GameInitData.mapGeneratorDef=DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
  }
 }
 public sealed class OpenAudit:GameComponent {
  int stage,frames,exitFrames,waitTick,identity;bool done,beamShot,warpShot,caShot,entryCheckpointLoaded;float started;
  readonly HashSet<string> shots=new HashSet<string>();
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"OpenCampaignAudit");
  public OpenAudit(Game game){}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"openAuditStage");Scribe_Values.Look(ref identity,"masonIdentity");}
  public override void LoadedGame(){if(stage==6)stage=7;if(stage==5)entryCheckpointLoaded=true;}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Folder,name+".png"));
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-open-campaign-audit"))return;
   if(done){if(++exitFrames>60)Application.Quit();return;}
   if(Find.CurrentMap==null||++frames<30)return;
   Directory.CreateDirectory(Folder);
   if(started==0)started=Time.realtimeSinceStartup;
   try {
    if(Time.realtimeSinceStartup-started>480)throw new Exception("Opening audit timed out: stage="+stage);
    var c=Current.Game.GetComponent<CampaignController>();var map=c.Map;
    var zero=Current.Game.GetComponent<GameComponent_GestaltZeroReturn>();
    if(stage==0){
     Check(c.Project.Name=="Shadow of War"&&c.Campaign.Name=="Shadow of War","project and campaign English names");
     Check(DefDatabase<ScenarioDef>.GetNamedSilentFail("PMPH_Akhundelar")==null,"old Akhundelar scenario removed");
     Check(c.Campaign.Edges.Any(e=>e.From=="mission-five"&&e.To=="open-campaign"),"fifth chapter leads to open node");
     Check(c.Actors["mason"].kindDef.defName=="PMPH_GestaltZero","mission five uses reviving open-world Mason");
     identity=c.Actors["mason"].thingIDNumber;stage=1;
    }
    if(stage==1&&c.State.Variables.GetValueOrDefault("m5-seconds")>=25&&!c.Presentation){
     var mason=c.Actors["mason"];Check(mason.Spawned,"Mason arrived through fifth-chapter warp");
     Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
     mason.Kill(null);Check(!mason.Dead&&!mason.Spawned,"Mason death intercepted and pawn retained");
     Check(Find.LetterStack.LettersListForReading.Last().def==LetterDefOf.ThreatSmall,"Zero death uses yellow letter");
     Check(Find.TickManager.CurTimeSpeed==TimeSpeed.Normal,"Zero death does not trigger major-threat pause");
     waitTick=Find.TickManager.TicksGame;stage=2;
    }
    if(stage==2){
     var beacon=map.listerThings.ThingsOfDef(ThingDef.Named("PMPH_GestaltZeroBeacon")).FirstOrDefault();
     if(beacon!=null){
      Find.CameraDriver.JumpToCurrentMapLoc(beacon.Position);Find.CameraDriver.SetRootSize(5);
      if(!shots.Contains("mesh-data")&&Find.TickManager.TicksGame-waitTick>30){
       var renderer=typeof(Thing_GestaltZeroBeacon).Assembly.GetType("PMP_Persona.GestaltZero.GestaltZeroReconstructionRenderer");
       object[] args={c.Actors["mason"],beacon.DrawPos,null,null};
       AccessTools.Method(renderer,"TryPrepare").Invoke(null,args);
       var tree=c.Actors["mason"].Drawer.renderer.renderTree;
       var requests=(List<PawnGraphicDrawRequest>)AccessTools.Field(typeof(PawnRenderTree),"drawRequests").GetValue(tree);
       if(requests.Count>0){shots.Add("mesh-data");File.WriteAllLines(Path.Combine(Folder,"scan-meshes.txt"),requests.Where(r=>r.mesh!=null).SelectMany(r=>r.mesh.vertices.Select((vertex,index)=>r.node.GetType().Name+" "+(r.material?.shader?.name??"no material")+" uv="+r.mesh.uv[index]+" world="+r.preDrawnComputedMatrix.MultiplyPoint3x4(vertex)+" matrix="+r.preDrawnComputedMatrix)));}
      }
      int token=(int)AccessTools.Field(zero.GetType(),"lifecycleToken").GetValue(zero);
      int phase=(int)(zero.ReconstructionPhase(token)*100);
      if(phase>=25&&phase<=30&&shots.Add("scan-low"))Shot("01-scan-low");
      if(phase>=48&&phase<=52&&shots.Add("scan-mid"))Shot("02-scan-mid");
      if(phase>=78&&phase<=83&&shots.Add("scan-high"))Shot("03-scan-high");
     }
     if(Find.TickManager.TicksGame-waitTick>180){
      Check(shots.Contains("scan-low")&&shots.Contains("scan-mid")&&shots.Contains("scan-high"),"captured actual 150-tick scan at three reconstruction phases");
      AccessTools.Field(zero.GetType(),"returnAtTick").SetValue(zero,Find.TickManager.TicksGame);
      stage=3;
     }
    }
    if(stage==3&&c.Actors["mason"].Spawned){
     Check(c.Actors["mason"].thingIDNumber==identity,"resurrection preserves Mason identity");
     // Also test a transition while this same actor is waiting for reconstruction.
     c.Actors["mason"].Kill(null);c.Transition("open-campaign");stage=4;frames=0;
    }
    if(stage==4&&frames>40){
     Check(Find.World.renderer.wantedMode==WorldRenderMode.Planet,"transition presents actual planet view");
     Check(Find.WindowStack.Windows.OfType<OpenNodeWindow>().Any(),"manual landing selector is shown");Shot("04-world-selection");
     Check(!c.TryChooseOpenTile(PlanetTile.Invalid),"invalid landing tile rejected");
     var selected=Enumerable.Range(0,Find.WorldGrid.TilesCount).Select(i=>new PlanetTile(i)).First(t=>TileFinder.IsValidTileForNewSettlement(t));
     Check(c.TryChooseOpenTile(selected),"manual selector accepts valid planet tile");
     Find.WindowStack.Windows.OfType<OpenNodeWindow>().First().Close();stage=5;
    }
    if(stage==5&&!c.Transitioning&&!c.EntryPending&&c.NodeId=="open-campaign"){
     if(!entryCheckpointLoaded){
      Check(!(bool)AccessTools.Field(zero.GetType(),"active").GetValue(zero),"pending resurrection resolved before entry checkpoint");
      entryCheckpointLoaded=true;GameDataSaveLoader.LoadGame(c.Checkpoint);return;
     }
     if(!beamShot&&c.State.Variables.GetValueOrDefault("open-bombard:shots")>=13){beamShot=true;Shot("05-bombardment");}
     if(!warpShot&&c.Objects.Keys.Any(k=>k.StartsWith("building-warp:"))){
      Check(c.State.Variables.GetValueOrDefault("open-bombard:done")==1,"bombardment completes before construction warp");
      Check(!map.listerThings.AllThings.Any(t=>t is Building&&t.HostileTo(Faction.OfPlayer)&&CellRect.CenteredOn(map.Center,20).Contains(t.Position)),"central hostile fortifications fully destroyed");warpShot=true;Shot("06-building-warp");
     }
     if(!caShot&&c.Communications.Messages.Any(msg=>msg.Spec.SpeakerName=="C.A.")){
      Check(!c.Cinematic&&!c.Freeze,"C.A. comms run after control handover");caShot=true;Shot("07-ca-communications");
     }
     if(c.State.Variables.GetValueOrDefault("open-intro-complete")!=1)return;
     Check(map.Size.x==250&&map.Parent is Settlement&&map.IsPlayerHome,"normal colony map generated on selected tile");
     Check(!c.StoryActive&&c.Node.Storyteller&&c.Node.Work&&c.Node.Research,"ordinary open-world simulation enabled");
     Check(c.Actors["mason"].thingIDNumber==identity&&c.Actors["mason"].Spawned,"same Mason safely transferred from pending revival");
     Check(!((bool)AccessTools.Field(zero.GetType(),"active").GetValue(zero)),"old reconstruction timer cleared at arrival");
     Check(map.mapPawns.AllPawnsSpawned.Count(p=>p.Faction==Faction.OfPlayer)==7,"exactly five probes, Mason, Kaldalis");
     Check(map.listerBuildings.allBuildingsColonist.Count(b=>b.def.defName=="Pylon")==1&&map.listerBuildings.allBuildingsColonist.Count(b=>b.def.defName=="ProtossForge")==1,"one pylon and one forge");
     Check(!map.listerThings.AllThings.Any(t=>t.def.defName=="PMP_PurificationBeam"||t is PMP_Persona.PMPH_OrbitalIncoming),"all bombardment weapons removed before play");
     Check(beamShot&&warpShot&&caShot,"all visual stages observed");
     Shot("08-control-restored");stage=6;GameDataSaveLoader.SaveGame("OpenCampaignAudit");
     LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("OpenCampaignAudit"));return;
    }
    if(stage==7&&frames>90){
     Check(c.State.Variables.GetValueOrDefault("open-intro-complete")==1&&!c.Cinematic,"save/load retains completed intro without replay");
     Check(c.Actors["mason"].thingIDNumber==identity&&c.Actors["mason"].Spawned,"Mason retained after save/load");
     var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);
     File.WriteAllLines(Path.Combine(Folder,"native-messages.txt"),Log.Messages.Select(m=>m.text));
     Check(errors.Length==0,"no native runtime errors");File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Open campaign and resurrection audit passed.");done=true;
    }
   }catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),ex.ToString());File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text));File.WriteAllLines(Path.Combine(Folder,"native-warnings.txt"),Log.Messages.Where(m=>m.type==LogMessageType.Warning).Select(m=>m.text));done=true;}
  }
 }
}
