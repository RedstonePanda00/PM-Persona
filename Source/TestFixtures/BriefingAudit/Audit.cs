using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using PMP.Campaign;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 [StaticConstructorOnStartup] public static class BriefingAuditBootstrap {
  internal static bool Enabled=>GenCommandLine.CommandLineArgPassed("pmp-briefing-audit");
  internal static bool Third=>GenCommandLine.CommandLineArgPassed("pmp-briefing-third");
  static BriefingAuditBootstrap(){if(Enabled)new Harmony("pmp.briefing.audit").PatchAll();}
 }
 [HarmonyPatch(typeof(Root_Play),nameof(Root_Play.SetupForQuickTestPlay))]
 static class BriefingSetup {
  static void Postfix(){
   if(!BriefingAuditBootstrap.Enabled)return;
   Current.Game.Scenario=DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario;Find.Scenario.PreConfigure();
   var c=Current.Game.GetComponent<CampaignController>();var def=DefDatabase<CampaignDef>.GetNamed("PMP_MissionOne");
   c.Prepare(def.LoadProject(),def.campaignId,BriefingAuditBootstrap.Third?"mission-three":"mission-two");
   Find.GameInitData.startingAndOptionalPawns=c.Actors.Values.ToList();Find.GameInitData.startingPawnCount=c.Actors.Count;
   Find.GameInitData.mapSize=300;Find.GameInitData.mapGeneratorDef=DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
  }
 }
 public sealed class BriefingAudit:GameComponent {
  bool early,rotated,done;int exitFrames;float start;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"BriefingAudit");
  public BriefingAudit(Game game){}
  object Call(MissionTwoPresentation p,string name,params object[] args)=>AccessTools.Method(typeof(MissionTwoPresentation),name).Invoke(p,args);
  T Field<T>(MissionTwoPresentation p,string name)=>(T)AccessTools.Field(typeof(MissionTwoPresentation),name).GetValue(p);
  void Check(bool ok,string message){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
  void Validate(MissionTwoPresentation view){
   float elapsed=view.Elapsed,extent=Field<float>(view,"terrainExtent");var tex=Field<Texture2D>(view,"terrain");
   var material=Field<Material>(view,"terrainMaterial");
   Check(material!=null&&material.shader.isSupported,"native terrain shader supported");
   Check(tex.width==1024,"expanded relief uses 1024-square height field");
   var pixels=Field<Color32[]>(view,"terrainPixels");
   Check(pixels.Take(pixels.Length/4).Select(c=>c.r).Distinct().Count()>20,"new outer area contains varied terrain");
   int seed=Field<int>(view,"terrainSeed");File.WriteAllText(Path.Combine(Folder,"terrain-seed.txt"),seed.ToString());
   foreach(float time in new[]{0f,3f,20f,90f,180f,300f}){
    view.Elapsed=time;object[] basis={null,null,null,null};Call(view,"CameraBasis",basis);
    var eye=(Vector3)basis[0];var fw=(Vector3)basis[1];var right=(Vector3)basis[2];var up=(Vector3)basis[3];
    foreach(float aspect in new[]{4f/3,16f/9,21f/9,32f/9}){
     float required=Mathf.Max(6,aspect*3.5f);
     foreach(float x in new[]{-.6f,.4f})foreach(float y in new[]{-.57f,.43f}){
      var ray=fw+right*x*aspect+up*y;var ground=eye+ray*(-eye.y/ray.y);
      Check(ray.y<0&&Mathf.Abs(ground.x)<required&&Mathf.Abs(ground.z)<required,"viewport corner covered at time "+time+" aspect "+aspect);
     }
    }
    foreach(var location in new[]{new Vector2(.15f,.50f),new Vector2(.36f,.43f),new Vector2(.48f,.43f),new Vector2(.68f,.37f),new Vector2(.55f,.68f)}){
     var screen=new Rect(0,0,1600,900);var projected=(Vector2)Call(view,"ProjectLocation",location,screen);
     float height=tex.GetPixelBilinear((location.x-.5f)/(2*extent)+.5f,(location.y-.5f)/(2*extent)+.5f).r*.24f;
     var ray=fw+right*((projected.x-960)/900)+up*((387-projected.y)/900);
     var hit=eye+ray*((height-eye.y)/ray.y);
     Check(new Vector2(hit.x-location.x+.5f,hit.z-location.y+.5f).magnitude<.0001f,"annotation stays on uploaded terrain coordinate at time "+time);
    }
   }
   view.Elapsed=elapsed;
   Check(Field<int>(view,"terrainSeed")==seed,"rotation preserves the generated terrain seed");
   var other=new MissionTwoPresentation();
   try{
    AccessTools.Field(typeof(MissionTwoPresentation),"third").SetValue(other,!BriefingAuditBootstrap.Third);Call(other,"Initialize");
    var first=Field<Color32[]>(other,"terrainPixels");
    Check(Field<int>(other,"terrainSeed")!=seed&&!first.SequenceEqual(pixels),"other chapter receives different procedural terrain");
    int prior=Field<int>(other,"terrainSeed");other.End();Call(other,"Initialize");
    Check(Field<int>(other,"terrainSeed")!=prior&&!Field<Color32[]>(other,"terrainPixels").SequenceEqual(first),"reopening creates a fresh terrain layout");
   }finally{other.End();}
  }
  public override void GameComponentUpdate(){
   if(!BriefingAuditBootstrap.Enabled)return;
   if(done){if(++exitFrames>60)Application.Quit();return;}
   Directory.CreateDirectory(Folder);if(start==0)start=Time.realtimeSinceStartup;
   try{
    if(Time.realtimeSinceStartup-start>150)throw new Exception("Briefing audit timed out");
    var view=MissionEffectsMapComponent.CurrentPresentation;if(view?.Briefing!=true)return;
    if(!early&&view.Elapsed>1&&Field<Texture2D>(view,"terrain")!=null){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"01-fullscreen-start.png"));early=true;}
    if(!rotated&&view.Elapsed>10){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"02-rotated.png"));rotated=true;}
    if(view.Elapsed<20)return;
    Validate(view);
    Check(Field<RenderTexture[]>(view,"captures").All(t=>t!=null&&t.IsCreated()),"three live map preview windows retained");
    Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native error log during briefing");
    ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"03-full-briefing.png"));
    File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Fullscreen briefing, rotating terrain anchors and fresh procedural seeds passed.");done=true;
   }catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),ex.ToString());Log.Error(ex.ToString());done=true;}
  }
 }
}
