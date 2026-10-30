using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Editor;
using RimCampaign.Runtime;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 [StaticConstructorOnStartup] static class OpenHotBootstrap {
  static OpenHotBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-open-hot-audit"))new Harmony("pmp.open.hot.audit").PatchAll(typeof(OpenHotBootstrap).Assembly);}
 }
 [HarmonyPatch(typeof(MainMenuDrawer),nameof(MainMenuDrawer.DoMainMenuControls))]
 static class OpenHotMenu {static void Postfix()=>OpenHotAudit.Tick();}
 [HarmonyPatch(typeof(Root),nameof(Root.Update))]
 static class OpenHotFrames {static void Postfix(){if(OpenHotAudit.Stage>0)OpenHotAudit.Tick();}}
 [HarmonyPatch(typeof(Root_Play),nameof(Root_Play.Start))]
 static class OpenHotPlay {static void Prefix(){OpenHotAudit.PlayStarts++;}}
 static class OpenHotAudit {
  internal static int Stage,PlayStarts;
  static int frames,exit,mason,playsBeforeFailure;static float began;static Workspace editor;static Project project;
  static PlanetTile selected;static string autosave;static Quaternion worldRotation;
  static string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"OpenHotTestAudit");
  static void Check(bool ok,string message){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
  static void Errors(string name)=>File.WriteAllLines(Path.Combine(Folder,name),Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text));
  internal static void Tick(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-open-hot-audit"))return;
   if(Stage==99){if(++exit>90)Application.Quit();return;}
   if(LongEventHandler.AnyEventNowOrWaiting)return;
   Directory.CreateDirectory(Folder);
   try{
    if(began==0)began=Time.realtimeSinceStartup;
    if(Time.realtimeSinceStartup-began>420)throw new Exception("Timeout at stage "+Stage);
    if(Stage==0){
     project=DefDatabase<CampaignDef>.GetNamed("PMP_MissionOne").LoadProject();
     editor=new Workspace();var t=Traverse.Create(editor);t.Field("history").SetValue(new History<Project>(project));t.Field("mapId").SetValue("open-landing");
     project.Maps.Single(m=>m.Id=="open-landing").Objects.Add(new Placement{Id="open-hot-unsaved-probe",Kind="Marker",Position=new Cell{X=12,Z=12}});
     Find.WindowStack.Add(editor);Stage=1;
     HotTestSession.Start(editor,Path.Combine(Folder,"authoring.project.json"),project,"open-landing");return;
    }
    if(Stage==1){
     var page=Find.WindowStack.Windows.OfType<Page_SelectStartingSite>().FirstOrDefault();if(page==null||++frames<80)return;
     Check(Current.ProgramState==ProgramState.Entry&&Current.Game.Maps.Count==0,"manual world site selection precedes map generation");
     Check(Current.Game.GetComponent<CampaignController>().NodeId=="open-campaign","direct hot test prepares the selected open node");
     Check(Find.GameInitData.mapGeneratorDef==null,"open start uses native settlement generator");
     Check(Find.FactionManager.AllFactionsListForReading.Count(f=>f.IsPlayer)==1,"world preparation creates exactly one player faction");
     Check(WorldRendererUtility.WorldSelected&&Find.WorldCamera.gameObject.activeSelf,"native planet camera is active during site selection");
     Check(!Traverse.Create(Find.WorldCameraDriver).Property("AnythingPreventsCameraMotion").GetValue<bool>(),"native site-selection camera is not input-locked");
     ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"01-world-site.png"));Stage=10;frames=0;return;
    }
    if(Stage==10){
     if(++frames<10)return;
     worldRotation=Find.WorldCameraDriver.sphereRotation;
     // Feed the native drag accumulator, then let the normal camera Update
     // consume it. Do not directly set the camera pose or force an update.
     Traverse.Create(Find.WorldCameraDriver).Field("desiredRotationRaw").SetValue(new Vector2(15,0));
     Stage=11;frames=0;return;
    }
    if(Stage==11){
     if(++frames<30)return;
     Check(Quaternion.Angle(worldRotation,Find.WorldCameraDriver.sphereRotation)>1,"native camera consumes drag motion while choosing a site");
     ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"01b-world-rotated.png"));Stage=12;frames=0;return;
    }
    if(Stage==12){
     if(++frames<10)return;
     selected=TileFinder.RandomStartingTile();Find.WorldInterface.SelectedTile=selected;
     Stage=2;frames=0;Find.WindowStack.Windows.OfType<Page_SelectStartingSite>().Single().OnAcceptKeyPressed();return;
    }
    var c=Current.Game?.GetComponent<CampaignController>();
    if(Stage==2){
     if(Current.ProgramState==ProgramState.Entry&&Find.WindowStack.Windows.OfType<Page_SelectStartingSite>().Any()){
      var confirm=Find.WindowStack.Windows.OfType<Dialog_MessageBox>().FirstOrDefault(d=>d.acceptAction!=null&&d.text.ToString().Contains("ConfirmSettle".Translate().ToString()));
      if(confirm!=null){confirm.acceptAction();confirm.Close();}return;
     }
     if(Current.ProgramState!=ProgramState.Playing||c?.Map==null)return;
     if(c.State.Variables.GetValueOrDefault("open-intro-complete")!=1||HotTestSession.SavePending)return;
     Check(c.Map.Parent is Settlement&&!c.Map.IsPocketMap&&c.Map.Tile==selected,"selected tile creates a normal colony map");
     Check(c.MapSpec.Objects.Any(p=>p.Id=="open-hot-unsaved-probe"),"unsaved editor state survives hot-test snapshot");
     Check(c.Actors.Values.All(p=>p.Spawned&&p.Map==c.Map)&&c.Map.mapPawns.AllPawnsSpawned.Count(p=>p.Faction==Faction.OfPlayer)==7,"opening contains Mason, Kaldalis and five probes only");
     Check(c.Map.listerBuildings.allBuildingsColonist.Count(b=>b.def.defName=="Pylon")==1&&c.Map.listerBuildings.allBuildingsColonist.Count(b=>b.def.defName=="ProtossForge")==1,"opening warps one pylon and one forge");
     Check(c.CampaignVariables.ContainsKey("pmp-open-research-baseline-v1"),"direct open start invokes baseline research initialization");
     Check(Faction.OfPlayer.ideos.PrimaryIdeo.Fluid&&Faction.OfPlayer.ideos.PrimaryIdeo.name=="PMP_CampaignIdeo_Name".Translate().ToString(),"campaign fluid ideology applies in hot test");
     Check(!c.Presentation&&!c.Freeze,"opening returns control");
     Errors("success-errors.txt");Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no runtime errors in successful open hot test");
     autosave=HotTestSession.AutoSaveName;mason=c.Actors["mason"].thingIDNumber;
     Check(File.Exists(GenFilePaths.FilePathForSavedGame(autosave)),"native hot-test autosave exists");
     ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"02-open-control.png"));Stage=20;frames=0;return;
    }
    if(Stage==20){
     if(++frames<10)return;
     Stage=3;frames=0;GameDataSaveLoader.LoadGame(autosave);return;
    }
    if(Stage==3){
     if(Current.ProgramState!=ProgramState.Playing||++frames<90)return;
     // The hot-test save is made when control returns, before C.A.'s
     // nonblocking briefing finishes. Reload must resume that briefing.
     if(frames==90)Check(c.Actors["mason"].thingIDNumber==mason&&c.State.Variables.GetValueOrDefault("open-ready")==1&&!c.Cinematic,"autosave reload preserves actors and completed landing without replay");
     if(c.State.Variables.GetValueOrDefault("open-intro-complete")!=1)return;
     Check(!c.Presentation&&!c.Freeze,"remaining C.A. briefing completes after reload");
     Errors("reload-errors.txt");Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no runtime errors after reload");
     Stage=4;frames=0;GenScene.GoToMainMenu();return;
    }
    if(Stage==4){
     if(Current.ProgramState!=ProgramState.Entry||HotTestSession.Active||!Find.WindowStack.Windows.Contains(editor)||++frames<60)return;
     Check(editor.Map.Id=="open-landing","return to original open-map editor");
     // A valid nonempty profile with malformed payload fails after world setup,
     // at the same preparation stage that previously scheduled a stray Play load.
     var profile=project.Profiles.Single(p=>p.Id=="open-mason");profile.GenerateFrom=null;profile.Payload="{";
     Check(Validation.Check(project).Count==0,"failure probe reaches runtime preparation");
     playsBeforeFailure=PlayStarts;Stage=5;frames=0;HotTestSession.Start(editor,Path.Combine(Folder,"failure.project.json"),project,"open-landing");return;
    }
    if(Stage==5){
     if(Current.ProgramState!=ProgramState.Entry||HotTestSession.Active||!Find.WindowStack.Windows.Contains(editor)||++frames<120)return;
     Errors("expected-preparation-failure.txt");
     Check(Log.Messages.Any(m=>m.type==LogMessageType.Error&&m.text.Contains("[RimCampaign hot test]")),"intentional preparation error is reported");
     Check(PlayStarts==playsBeforeFailure,"failed preparation never enters another Play scene");
     Check(!Log.Messages.Any(m=>m.text.Contains("InvalidCastException")||m.text.Contains("First node must be a story map")),"no chained entry/play cast or story-only restriction");
     Check(HotTestSession.Owner==null&&!HotTestSession.SavePending,"failed hot-test state is cleared");
     File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Open hot test, autosave, editor return and failure recovery passed.");Stage=99;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());Errors("native-errors.txt");Stage=99;}
  }
 }
}
