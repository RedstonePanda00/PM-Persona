using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using PMP_Persona.PrismSupport;
using PMP_Persona.ShipSupport;
using PMP_Persona.VanguardSupport;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace PMP.Campaign {
 public sealed partial class MissionFiveMapComponent {
  internal readonly MissionFiveBriefing briefing=new MissionFiveBriefing();
  void Say(string group,string source="") {
   Controller.QueueStory(Controller.MapSpec.Groups.Single(g=>g.Id==group).Actions,source);
  }
  public void AnnounceWave(int index,List<Pawn> pawns) {
   if(index<4)Say("m5-direction-"+index);
   if(pawns.Any(p=>p.kindDef.defName=="PMP_SanctuaryChampion"))Say("m5-champion-warning-"+Rand.Range(0,5));
  }
  void CheckFirstContacts() {
   var defenders=map.listerThings.AllThings.Where(t=>t.Spawned&&t.Faction==Faction.OfPlayer&&(t is Pawn||t is Building)).ToList();
   foreach(string kind in new[]{"PMP_M3Believer","PMP_LegionGuard","PMP_LegionChosen","PMP_LegionCenturion"}) {
    string key="m5-contacted-"+kind;if(Controller.State.Variables.GetValueOrDefault(key)!=0)continue;
    var pawn=map.mapPawns.AllPawnsSpawned.FirstOrDefault(p=>p.kindDef.defName==kind&&!p.Dead&&p.HostileTo(Faction.OfPlayer)
     && defenders.Any(t=>p.Position.DistanceToSquared(t.Position)<=35*35&&GenSight.LineOfSight(p.Position,t.Position,map)));
    if(pawn==null)continue;
    Controller.State.Variables[key]=1;Say("m5-contact-"+kind,pawn.ThingID);
   }
  }
  void DrawEntrances() {
   float pulse=.55f+.45f*Mathf.Sin(Time.realtimeSinceStartup*5);
   Vector2 Screen(Vector3 point){var v=Find.CameraDriver.MyCamera.WorldToViewportPoint(point);return new Vector2(v.x*UI.screenWidth,(1-v.y)*UI.screenHeight);}
   foreach(var direction in new[]{new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,-1),new Vector2(1,1)}) {
    var axis=direction.normalized;var cross=new Vector2(-axis.y,axis.x);var center=new Vector2(162,162)+axis*93;
    var points=new Vector2[4];int i=0;
    foreach(var corner in new[]{new Vector2(-1,-1),new Vector2(-1,1),new Vector2(1,1),new Vector2(1,-1)}){
     var p=center+axis*corner.x*18+cross*corner.y*10;points[i++]=Screen(new Vector3(p.x,0,p.y));
    }
    for(i=0;i<4;i++){
     Widgets.DrawLine(points[i],points[(i+1)%4],new Color(1,.78f,.2f,.12f*pulse),8);
     Widgets.DrawLine(points[i],points[(i+1)%4],new Color(1,.84f,.35f,pulse),2);
    }
   }
  }
  internal void StartFinale() {
   var v=Controller.State.Variables;if(v.ContainsKey("m5-finale-start"))return;
   v["m5-finale-start"]=Find.TickManager.TicksGame;
   foreach(var pawn in map.mapPawns.AllPawnsSpawned.Where(p=>p.HostileTo(Faction.OfPlayer)&&!p.Dead&&!p.Downed)){
    var job=JobMaker.MakeJob(JobDefOf.Goto,CellFinder.StandableCellNear(map.Center,map,16));job.playerForced=true;
    pawn.jobs.StartJob(job,JobCondition.InterruptForced);
   }
  }
  void TickFinale() {
   var v=Controller.State.Variables;if(!v.TryGetValue("m5-finale-start",out var start))return;
   int age=Find.TickManager.TicksGame-(int)start;
   var config=DefDatabase<ShipSupportDef>.GetNamed("PMP_Support_Prism").GetModExtension<OrbitalSupportConfig>();
   // All rounds use the ordinary game clock. Results pause later rounds along with the map.
   for(int i=(int)v.GetValueOrDefault("m5-finale-shots");i<9;i++) {
    int due=180+(i/3)*240+(i%3)*24;if(age<due)break;
    var effects=(PrismFractureEffects)AccessTools.Method(typeof(PrismFractureEffects),"Get").Invoke(null,new object[]{map});
    var offset=new[]{new IntVec3(-12,0,0),new IntVec3(10,0,8),new IntVec3(0,0,-12)}[i%3];
    AccessTools.Method(typeof(PrismFractureEffects),"Add").Invoke(effects,new object[]{map.Center+offset,config});
    v["m5-finale-shots"]=i+1;
   }
   if(age>=180+48+config.fractureGuidanceTicks+2&&v.GetValueOrDefault("m5-finale-first-complete")==0){
    v["m5-finale-first-complete"]=1;Find.CameraDriver.shaker.DoShake(3);
   }
  }
 }
 [CampaignModule("pmp.m5.brief","Action","Fixed overhead fifth-mission terrain briefing; authored stage variables reveal insignia and arrows.")]
 public sealed class MissionFiveBriefAction:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var m=MissionFiveMapComponent.For(c.Map);if(m==null)return true;if(c.State.Variables.GetValueOrDefault("m5-brief-done")==1){m.briefing.End();return true;}m.briefing.Visible=true;return false;}
 }
 [CampaignModule("pmp.m5.recall","Action","Recall surviving player units and buildings with the existing stationary recall effect, scaled by footprint.")]
 public sealed class MissionFiveRecall:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;string key="recall:"+a.Id;var fleck=DefDatabase<FleckDef>.GetNamed("PM_WarpResourceExtractionFleck");
   if(!c.State.Variables.TryGetValue(key,out var elapsed)){
    c.State.Variables[key]=0;
    foreach(var thing in c.Map.listerThings.AllThings.Where(t=>t.Faction==Faction.OfPlayer&&(t is Building||t is Pawn p&&!p.Dead)).ToList()){
     c.Objects[key+":"+thing.ThingID]=thing;
     if(thing is Pawn pawn){pawn.jobs?.StopAll();pawn.pather?.StopDead();var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=(int)(fleck.Lifetime*60)+2;pawn.jobs?.StartJob(wait,JobCondition.InterruptForced);}
     float size=thing is Building?Mathf.Sqrt(thing.def.size.x*thing.def.size.z):1;
     FleckMaker.Static(thing.DrawPos+new Vector3(0,0,3*size),c.Map,fleck,.5f*size);
     SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(thing.Position,c.Map));
    }
    c.State.Variables["m5-recalled-buildings"]=c.Objects.Count(p=>p.Key.StartsWith(key+":")&&p.Value is Building);
    return false;
   }
   elapsed+=delta;c.State.Variables[key]=elapsed;if(elapsed<fleck.Lifetime)return false;
   foreach(var pair in c.Objects.Where(p=>p.Key.StartsWith(key+":")))if(pair.Value?.Spawned==true&&pair.Value.Map==c.Map)pair.Value.DeSpawn();
   return true;
  }
 }
 [CampaignModule("pmp.m5.finale","Action","Let the Legion advance, fire three native fracture shots, then finish during impact shake. Native results pause remaining rounds.")]
 public sealed class MissionFiveFinale:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;var m=MissionFiveMapComponent.For(c.Map);m.StartFinale();return c.State.Variables.GetValueOrDefault("m5-finale-first-complete")==1;}
 }
}
