using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using PMP.Campaign;
using PMP_Persona.VanguardSupport;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 public sealed partial class MapDefenseAudit {
  List<Thing> endingGallery=new();int endingWalkCells;IntVec3 lastEndingCell;bool endingConversation,endingGone;float endingStart;float firstWalk=-1,lastWalkShot=-1,lastViewportY;int walkShots,visibleWalkFrames;
  void EndingAudit(CampaignController c){
   var hero=c.Actors["hero"];var module=c.Map.GetComponent<MissionThreeMapComponent>();
   if(stage==0){
    endingStart=Time.realtimeSinceStartup;c.State.Fibers.Clear();c.RestoreControl();c.Communications.Messages.Clear();
    var briefing=AccessTools.Property(typeof(MissionEffectsMapComponent),"CurrentPresentation")?.GetValue(null);
    if(briefing!=null)AccessTools.Method(briefing.GetType(),"End").Invoke(briefing,null);
    Check(!c.MapSpec.Rules.Any(r=>r.Id=="m3-engineers-3")&&!c.MapSpec.Objects.Any(o=>o.Group=="outpost-plans-3"||o.Group=="engineers-3"),"third outpost construction rule and dormant assets removed");
    Check(c.MapSpec.Rules.Count(r=>r.Id.StartsWith("m3-engineers-"))==2,"first two outpost construction sequences retained");
    Check(c.MapSpec.Objectives.Where(o=>o.Required).Select(o=>o.Id).OrderBy(s=>s).SequenceEqual(new[]{"champion","hub"}),"only final champion and hub are required objectives");
    var scene=c.MapSpec.Rules.SelectMany(r=>r.Actions).Single(a=>a.Id=="m3-ending-cinematic").Steps;
    int reply=scene.FindIndex(a=>a.Text=="我会的。");
    Check(scene[reply+1].Kind=="Wait"&&scene[reply+1].Value==.5&&!scene[reply+1].Freeze&&scene[reply+2].Kind=="pmp.m3.endingExit","Mason reply precedes a 30-game-tick pause and Cassandra departure");
    Check(!MissionThreeMapComponent.FinalTargetsDestroyed(c),"live champion and hub do not trigger victory");
    var champion=(Pawn)c.Objects["m3-champion"];var hub=c.Objects["anomaly-hub"];
    c.Objects.Remove("m3-champion");Check(!MissionThreeMapComponent.FinalTargetsDestroyed(c),"missing champion reference is not a kill");c.Objects["m3-champion"]=champion;
    c.Objects.Remove("anomaly-hub");Check(!MissionThreeMapComponent.FinalTargetsDestroyed(c),"missing hub reference is not destruction");c.Objects["anomaly-hub"]=hub;
    for(int i=0;i<3;i++){
     var def=DefDatabase<ThingDef>.GetNamed(new[]{"PMP_RomulusObelisk","PMP_RomulusDome","PMP_RomulusHub"}[i]);
     var building=GenSpawn.Spawn(ThingMaker.MakeThing(def),new IntVec3(92+i*7,0,340),c.Map);endingGallery.Add(building);
     Check(def.graphicData.texPath==new[]{"Things/Building/Romulus/Obelisk","Things/Building/Romulus/Dome","Things/Building/Romulus/Hub"}[i],"distinct structure texture "+def.defName);
    }
    Find.CameraDriver.SetRootPosAndSize(new Vector3(100,0,342),9);stage=1;frames=0;return;
   }
   if(stage==1){if(++frames<20)return;ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"structures-native.png"));stage=2;frames=0;return;}
   if(stage==2){
    foreach(var item in endingGallery)item.Destroy();
    hero.jobs.StopAll();hero.pather.StopDead();hero.Position=new IntVec3(474,0,90);hero.Drawer.tweener.ResetTweenedPosToRoot();
    c.State.Variables["sector-3-captured"]=0;c.State.Variables["m3-mason-safe"]=0;
    var champion=(Pawn)c.Objects["m3-champion"];var record=BossMapComponent.For(champion);record.Active=true;record.Health=1;record.HealthLocked=false;
    champion.TakeDamage(new DamageInfo(DamageDefOf.Blunt,100));
    Check(champion.Dead||champion.Destroyed,"champion dies through the registered boss damage pipeline");
    Check(!MissionThreeMapComponent.FinalTargetsDestroyed(c),"champion death alone does not finish the mission");
    c.Objects.Remove("m3-champion");
    Check(!MissionThreeMapComponent.FinalTargetsDestroyed(c),"recorded champion death survives reference loss but still requires the hub");
    module.Begin();c.Objects["anomaly-hub"].Destroy();
    Check(MissionThreeMapComponent.FinalTargetsDestroyed(c),"both final targets destroyed permits victory with sector three uncaptured and Mason route unfinished");
    c.Objects.Remove("anomaly-hub");Check(MissionThreeMapComponent.FinalTargetsDestroyed(c),"both recorded target deaths remain complete after destroyed references disappear");
    stage=3;
   }
   Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
   if(c.Objects.TryGetValue("m3-cassandra",out var actor)&&actor is Pawn knight){
    if(knight.Spawned&&!endingConversation&&c.Communications.Messages.Count>0){
     Check(c.Cinematic&&knight.kindDef.defName=="PMP_ImperialKnight"&&knight.gender==Gender.Female,"ending uses second-mission Cassandra pawn model under cinematic control");
     Check(knight.Position.DistanceTo(hero.Position)<=8,"Cassandra staged in front of Mason");
     Check(Find.CameraDriver.MapPosition.DistanceTo(hero.Position)<10,"ending camera focuses on Mason");
     Check(c.Map.GetComponent<OrbitalSupportSession>()==null,"Prism support closed before ending dialogue");
     endingConversation=true;lastEndingCell=knight.Position;ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"ending-conversation.png"));
    }
    if(endingConversation&&knight.Spawned&&knight.Position!=lastEndingCell){endingWalkCells++;lastEndingCell=knight.Position;if(firstWalk<0)firstWalk=Time.realtimeSinceStartup;}
    if(firstWalk>=0&&knight.Spawned){
     var viewport=Find.Camera.WorldToViewportPoint(knight.DrawPos);
     lastViewportY=viewport.y;
     if(viewport.y>0&&viewport.y<1&&viewport.x>0&&viewport.x<1&&!knight.Position.Fogged(c.Map))visibleWalkFrames++;
     if(Time.realtimeSinceStartup-lastWalkShot>=.35f){
      ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"walk-"+(walkShots++).ToString("D3")+".png"));lastWalkShot=Time.realtimeSinceStartup;
      File.AppendAllText(Path.Combine(Folder,"walk.txt"),$"{Time.realtimeSinceStartup-firstWalk:F2}s tick={Find.TickManager.TicksGame} cell={knight.Position} draw={knight.DrawPos} viewport={viewport} fog={knight.Position.Fogged(c.Map)} speed={knight.GetStatValue(StatDefOf.MoveSpeed)}\n");
     }
    }
    if(endingConversation&&!knight.Spawned&&!endingGone){
     Check(visibleWalkFrames>20&&Time.realtimeSinceStartup-firstWalk>2,"departure visibly renders over multiple seconds at normal game speed");
     Check(lastViewportY>1.1f,"Cassandra's last rendered position is fully above the screen before despawning");
     Check(endingWalkCells>5,"Cassandra walks multiple native cells before despawning");
     var exit=c.MapSpec.Objects.Single(o=>o.Id=="m3-ending-exit");Check(Find.Camera.WorldToViewportPoint(new IntVec3(exit.Position.X,0,exit.Position.Z).ToVector3Shifted()).y>1.25f,"Cassandra's destination is above the actual focused viewport");
     endingGone=true;ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"ending-mason-alone.png"));
    }
   }
   if(c.State.Phase=="Succeeded"){
    Check(endingConversation&&endingGone,"full staged ending and walking exit completed before results");
    Check(c.State.Variables.GetValueOrDefault("sector-3-captured")==0,"third checkpoint capture is not required for victory");
    Check(c.State.Objectives["capture-3"]!="Complete","result does not falsely complete the optional third checkpoint");
    Check(hero.drafter.FireAtWill,"ending restores Mason's fire-at-will setting");
    Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native errors during new structure rendering and full ending sequence");Finish();
   }
   if(Time.realtimeSinceStartup-endingStart>150)throw new Exception("Ending timeout: "+c.State.Phase+" cinematic="+c.Cinematic+" freeze="+c.Freeze+" messages="+c.Communications.Messages.Count);
  }
 }
}
