using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using PMP.Campaign;
using PMP_Persona.VanguardSupport;

namespace PMP.Tests {
 [HarmonyPatch(typeof(BossMapComponent),nameof(BossMapComponent.MapComponentOnGUI))]
 static class OrbitalBossBarProbe {
  public static int Draws;
  static void Postfix(){if((bool)AccessTools.Property(typeof(VanguardFireControl),"OwnsView").GetValue(null)&&Event.current?.type==EventType.Repaint)Draws++;}
 }
 public sealed partial class MapDefenseAudit {
  bool Revision=>GenCommandLine.CommandLineArgPassed("pmp-behavior-revision-audit");
  void RunBehaviorChecks(CampaignController c){
   var arena=c.Map.GetComponent<BossArenaMapComponent>();var champion=(Pawn)c.Objects["m3-champion"];var rule=arena.RuleFor(champion);var hero=c.Actors["hero"];
   Check(rule!=null&&!arena.Activated(rule)&&!BossMapComponent.BehaviorsAllowed(champion),"champion AI and automatic boss plugins dormant before second capture");
   var original=champion.Position;champion.jobs.StopAll();champion.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackMelee,hero),JobCondition.InterruptForced);
   Check(champion.CurJob?.def!=JobDefOf.AttackMelee,"native attack job rejected during standby");
   champion.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto,hero.Position),JobCondition.InterruptForced);Check(champion.CurJob?.def!=JobDefOf.Goto,"native wandering/chase cannot leave standby post");
   for(int i=0;i<120;i++)Find.TickManager.DoSingleTick();
   Check(champion.Position==original&&!arena.Activated(rule),"champion remains at final northern post through 120 actual game ticks");
   c.State.Variables["sector-2-captured"]=1;
   var empire=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec,"Empire");
   var dummy=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,empire);GenSpawn.Spawn(dummy,original+IntVec3.West*5,c.Map);
   Check(BossMapComponent.BehaviorsAllowed(champion)&&BossMapComponent.TargetAllowed(champion,dummy),"second capture enables champion against northern Empire targets");
   Check(!BossMapComponent.TargetAllowed(champion,hero),"Mason excluded even after activation");
   var dummyAt=dummy.Position;dummy.Position=new IntVec3(450,0,130);Check(!BossMapComponent.TargetAllowed(champion,dummy),"Empire targets in southern battlefield also excluded");dummy.Position=dummyAt;
   foreach(var boundary in new[]{(new IntVec3(430,0,175),new IntVec3(430,0,174)),(new IntVec3(220,0,250),new IntVec3(219,0,250)),(new IntVec3(482,0,250),new IntVec3(483,0,250)),(new IntVec3(430,0,309),new IntVec3(430,0,310))}){
    champion.pather.StopDead();champion.Position=boundary.Item1;champion.pather.nextCell=boundary.Item2;Traverse.Create(champion.pather).Method("TryEnterNextPathCell").GetValue();Check(champion.Position==boundary.Item1,"actual path step rejected at arena edge "+boundary.Item2);
   }
   champion.Position=original;champion.pather.StopDead();champion.jobs.StopAll();
   for(int i=0;i<120;i++)Find.TickManager.DoSingleTick();Check(arena.Bounds(rule).Contains(champion.Position),"active champion remains inside north during actual combat ticks");
   if(!dummy.Destroyed)dummy.Destroy(DestroyMode.Vanish);c.State.Variables["sector-2-captured"]=0;champion.Position=original;champion.pather.StopDead();champion.jobs.StopAll();
   var heroAt=hero.Position;hero.jobs.StopAll();hero.Position=new IntVec3(356,0,130);hero.drafter.Drafted=true;hero.drafter.FireAtWill=true;
   var enemy=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_M3Believer"),champion.Faction);GenSpawn.Spawn(enemy,new IntVec3(362,0,130),c.Map);
   var move=new Act{Kind="rc.task.move",Target="mason-route-3-0",Text="Combat",Value=1};new TaskMove().Execute(move,c,"hero",0);
   Check(hero.CurJob?.def==JobDefOf.AttackStatic||hero.CurJob?.def==JobDefOf.AttackMelee,"Mason stops moving and returns fire at an encountered hostile");
   new TaskMove().Execute(move,c,"hero",0);Check(hero.CurJob?.def!=JobDefOf.Goto,"repeated movement action does not interrupt valid counterattack");
   enemy.Destroy(DestroyMode.Vanish);new TaskMove().Execute(move,c,"hero",0);Check(hero.CurJob?.def==JobDefOf.Goto,"Mason resumes route after threat disappears");
   var safe=c.MapSpec.Objects.Single(o=>o.Id=="extraction").Position;hero.jobs.StopAll();hero.pather.StopDead();hero.Position=new IntVec3(safe.X,0,safe.Z);
   var hold=new Act{Kind="rc.task.hold",Target="extraction",Text="Combat",Value=1,When=new Condition{Op="Equal",Key="audit-hold-end",Value=1}};
   Check(!new TaskHold().Execute(hold,c,"hero",0)&&hero.CurJob?.def==JobDefOf.Wait_Combat,"Mason holds southeast safe point with self-defense enabled");
   foreach(var target in new[]{"mason-safe-route-1","mason-safe-route-2","extraction"}){var p=c.MapSpec.Objects.Single(o=>o.Id==target);Check(hero.CanReach(new IntVec3(p.Position.X,0,p.Position.Z),PathEndMode.OnCell,Danger.Deadly),"safe exit route reachable: "+target);}
   hero.jobs.StopAll();hero.Position=heroAt;
   var turrets=c.MapSpec.Objects.Where(p=>p.Id.StartsWith("m3-route-charge-")).ToList();Check(turrets.Count==3&&turrets.All(p=>c.Objects[p.Id].Spawned&&p.Faction=="Hostile"&&p.Position.X>310),"three light route turrets begin at second shield section");
   foreach(var p in turrets)Check(Footprint(p).All(cell=>c.Map.thingGrid.ThingsListAtFast(cell).Count(t=>t.def.category==ThingCategory.Building&&t.def.defName!="HiddenConduit")==1),"route turret footprint free: "+p.Id);
   Check(DefDatabase<ThingDef>.GetNamed("PMP_RomulusRedWall").graphic is Graphic_LinkedCornerFiller,"Romulus wall retains native cardinal atlas rendering");
  }
  void WallGallery(CampaignController c){
   var def=DefDatabase<ThingDef>.GetNamed("PMP_RomulusRedWall");
   foreach(var o in new[]{new IntVec3(0,0,0),new IntVec3(0,0,1),new IntVec3(1,0,0),new IntVec3(4,0,0),new IntVec3(4,0,1),new IntVec3(4,0,2),new IntVec3(3,0,1),new IntVec3(5,0,1),new IntVec3(8,0,0),new IntVec3(9,0,0),new IntVec3(8,0,1),new IntVec3(9,0,1)})GenSpawn.Spawn(ThingMaker.MakeThing(def),new IntVec3(350,0,340)+o,c.Map);
   var corner=new IntVec3(350,0,340).GetEdifice(c.Map);var graphic=(Graphic_Linked)corner.Graphic;
   GenSpawn.Spawn(ThingMaker.MakeThing(def),new IntVec3(365,0,340),c.Map);GenSpawn.Spawn(ThingMaker.MakeThing(def),new IntVec3(366,0,341),c.Map);
   Check(HullConnections.TryCorner(c.Map,new IntVec3(365,0,341),out var partial)&&!partial.Full,"diagonal hull links work without Odyssey enabled");
   Check(graphic.ShouldLinkWith(corner.Position+IntVec3.North,corner)&&graphic.ShouldLinkWith(corner.Position+IntVec3.East,corner)&&!graphic.ShouldLinkWith(corner.Position+IntVec3.West,corner),"native link grid registers custom walls and selects adjacent atlas tiles");
   Find.CameraDriver.config.sizeRange=new FloatRange(3,60);Find.CameraDriver.SetRootPosAndSize(new Vector3(354,0,341),5);
  }
  void RevisionCaptures(CampaignController c){
   if(stage==3&&++frames==25)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"beveled-wall-gallery.png"));
   if(stage==3&&frames>=50){c.State.Variables["sector-2-captured"]=1;c.Map.GetComponent<MissionThreeMapComponent>().Begin();var session=c.Map.GetComponent<OrbitalSupportSession>();var phase=AccessTools.Field(typeof(OrbitalSupportSession),"Phase");phase.SetValue(session,Enum.Parse(phase.FieldType,"FireControl"));stage=4;frames=0;}
   if(stage==4&&++frames==50){Check((bool)AccessTools.Property(typeof(VanguardFireControl),"OwnsView").GetValue(null)&&OrbitalBossBarProbe.Draws>0,"boss health bar drawn during exclusive orbital support view");ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"orbital-boss-health.png"));}
   if(stage==4&&frames>=80){Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no errors after encounter/return-fire/bevel/orbital regression tests");Finish();}
  }
 }
}
