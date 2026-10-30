using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace PMP.Tests.Landscape {
 [StaticConstructorOnStartup] public static class ReviewBootstrap {
  static ReviewBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-landscape-review"))new Harmony("pmp.landscape.review").PatchAll();}
 }
 [HarmonyPatch(typeof(Root_Play),nameof(Root_Play.SetupForQuickTestPlay))] static class Setup {
  static void Postfix(){if(!GenCommandLine.CommandLineArgPassed("pmp-landscape-review"))return;
   Current.Game.Scenario=DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario;Find.Scenario.PreConfigure();
   var c=Current.Game.GetComponent<CampaignController>();var def=DefDatabase<CampaignDef>.GetNamed("PMP_MissionOne");
   c.Prepare(Json.Load<Project>(Path.Combine(Generate.Folder,"final.project.json")),def.campaignId,"mission-four");
   Find.GameInitData.startingAndOptionalPawns=c.Actors.Values.ToList();Find.GameInitData.startingPawnCount=c.Actors.Count;Find.GameInitData.mapSize=c.MapSpec.Width;Find.GameInitData.mapGeneratorDef=DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
  }
 }
 [HarmonyPatch] static class Bounds {
  static MethodBase TargetMethod()=>AccessTools.Method(AccessTools.TypeByName("PMP.Campaign.PersonaCommunicationRenderer"),"Draw");
  static void Postfix(CommunicationDrawContext context){if(context.Data?.ContainsKey("landscape-review")==true)Review.Bounds=context.Panel;}
 }
 public sealed class Review:GameComponent {
  public static Rect? Bounds;bool done,wide;int stage,frames;float start;
  public Review(Game game){}
  void Check(bool ok,string message){File.AppendAllText(Path.Combine(Generate.Folder,"review.txt"),(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
  static IEnumerable<Act> Flatten(IEnumerable<Act> acts){foreach(var a in acts){yield return a;foreach(var b in Flatten(a.Steps.Concat(a.Else)))yield return b;}}
  public override void GameComponentUpdate(){
   if(done||!GenCommandLine.CommandLineArgPassed("pmp-landscape-review"))return;
   var c=Current.Game.GetComponent<CampaignController>();if(c?.Map==null||c.EntryPending||c.Transitioning)return;
   if(start==0)start=Time.realtimeSinceStartup;
   try {
    Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
    if(!wide&&Find.CameraDriver.RootSize>100&&++frames>12){wide=true;ScreenCapture.CaptureScreenshot(Path.Combine(Generate.Folder,"four-overview.png"));}
    var hero=c.Actors["hero"];
    if(stage==0){
     if(c.State.Variables.GetValueOrDefault("m4-ready")!=1)return;
     Check(wide,"native fourth-map overview rendered");
     Check(hero.CanReach(c.Objects["m4-subnode"],PathEndMode.Touch,Danger.Deadly),"Mason reaches node after landscape initialization");
     for(int i=1;i<=3;i++)Check(hero.CanReach(c.Objects["m4-outpost-"+i+"-obelisk"],PathEndMode.Touch,Danger.Deadly),"Mason reaches outpost "+i);
     Check(c.Map.Biome.defName=="TemperateForest","initialized native biome");
     Check(c.Map.listerThings.AllThings.Any(t=>t is Plant)&&c.Map.listerThings.AllThings.Any(t=>t.def.building?.isNaturalRock==true),"native plants and mountains spawned");
     var changed=Json.Read<Dictionary<string,List<string>>>(File.ReadAllText(Path.Combine(Generate.Folder,"changes.json")))["factions"];
     Check(changed.All(id=>c.Objects[id].Faction!=null&&c.Objects[id].Faction.HostileTo(Faction.OfPlayer)),"all added walls belong to hostile Romulus faction");
     Find.Selector.ClearSelection();Find.Selector.Select(hero);Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect);
     var line=Json.Copy(Flatten(c.MapSpec.Rules.SelectMany(r=>r.Actions)).First(a=>a.Kind=="Communication"&&!a.Freeze));line.Id="landscape-review-line";line.Communication.Duration=15;line.Communication.RendererData["landscape-review"]="true";
     Traverse.Create(c).Field("engine").GetValue<Engine>().Enqueue(new List<Act>{line});frames=0;stage=1;
    }else if(stage==1&&Bounds.HasValue&&++frames>12){
     Check(Math.Abs(Bounds.Value.y-260)<1,"normal communication uses shared Y=260 layout");
     Check(Find.Selector.SelectedObjects.Contains(hero)&&Find.MainTabsRoot.OpenTab==MainButtonDefOf.Inspect,"pawn inspection panel open during communication");
     ScreenCapture.CaptureScreenshot(Path.Combine(Generate.Folder,"four-selected-communication.png"));
     Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no native errors");
     File.WriteAllText(Path.Combine(Generate.Folder,"REVIEW-PASS.txt"),"PASS");done=true;
    }
    if(Time.realtimeSinceStartup-start>120)throw new Exception("Review timeout stage "+stage);
   }catch(Exception ex){File.WriteAllText(Path.Combine(Generate.Folder,"REVIEW-ERROR.txt"),ex.ToString());done=true;Log.Error(ex.ToString());}
  }
 }
}
