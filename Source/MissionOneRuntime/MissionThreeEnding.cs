using System;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PMP.Campaign {
 [CampaignModule("pmp.m3.endingStage","Action","Stage Cassandra beside Mason using the mission-two knight model and anchor the ending camera.")]
 public sealed class MissionThreeEndingStage:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;var hero=c.Actors["hero"];
   // Cancel Mason's route even if the two final targets were destroyed early.
   void Cancel(Fiber fiber){if(fiber.ActorBound&&c.ResolveTriggerPawn(fiber.Source)==hero){fiber.Index=fiber.Steps.Count;fiber.Children.Clear();return;}foreach(var child in fiber.Children)Cancel(child);}
   foreach(var fiber in c.State.Fibers)Cancel(fiber);
   hero.jobs.StopAll();hero.pather.StopDead();hero.Rotation=Rot4.South;hero.drafter.FireAtWill=false;
   var focus=c.MapSpec.Objects.Single(o=>o.Id=="m3-ending-focus");focus.Position=new Cell{X=hero.Position.x,Z=hero.Position.z};
   if(!c.Objects.TryGetValue("m3-cassandra",out var existing)||existing.Destroyed){
    var placement=Json.Copy(c.MapSpec.Objects.Single(o=>o.Id=="m3-cassandra"));
    var front=hero.Position+IntVec3.South*3;
    var spot=GenRadial.RadialCellsAround(front,8,true).First(cell=>cell.InBounds(c.Map)&&cell.Standable(c.Map)&&cell!=hero.Position&&hero.CanReach(cell,PathEndMode.OnCell,Danger.Deadly));
    placement.Position=new Cell{X=spot.x,Z=spot.z};existing=c.Place(placement,c.Map);
   }
   var knight=(Pawn)existing;knight.Name=new NameSingle(RcText.T("卡珊德拉"));
   knight.GetLord()?.Notify_PawnLost(knight,PawnLostCondition.ForcedToJoinOtherLord);
   knight.jobs.StopAll();knight.pather.StopDead();knight.Rotation=Rot4.North;
   if(knight.equipment.Primary==null)knight.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_ChargeRifle")));
   var protection=DefDatabase<HediffDef>.GetNamed("PMP_KnightProtection");if(!knight.health.hediffSet.HasHediff(protection))knight.health.AddHediff(protection);
   var wait=JobMaker.MakeJob(JobDefOf.Wait);wait.expiryInterval=60000;knight.jobs.StartJob(wait,JobCondition.InterruptForced);
   return true;
  }
 }
 [CampaignModule("pmp.m3.endingExit","Action","Choose a reachable destination outside the fixed ending camera for Cassandra's native walking exit.")]
 public sealed class MissionThreeEndingExit:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;var knight=(Pawn)c.Objects["m3-cassandra"];
   var destination=c.Map.AllCells.Where(cell=>cell.z>knight.Position.z&&cell.Standable(c.Map)&&Find.Camera.WorldToViewportPoint(cell.ToVector3Shifted()).y>1.25f).OrderBy(cell=>cell.DistanceToSquared(knight.Position)+Math.Abs(cell.x-knight.Position.x)*20)
    .First(cell=>knight.CanReach(cell,PathEndMode.OnCell,Danger.Deadly));
   var exit=c.MapSpec.Objects.Single(o=>o.Id=="m3-ending-exit");exit.Position=new Cell{X=destination.x,Z=destination.z};return true;
  }
 }
 [CampaignModule("pmp.m3.endingWalk","Action","Walk Cassandra north until her rendered position is fully above the fixed camera.")]
 public sealed class MissionThreeEndingWalk:IActionModule {
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;var knight=(Pawn)c.Objects[a.Target];
   if(!knight.Spawned||knight.Dead||knight.Downed)throw new InvalidOperationException("Cassandra cannot walk out of the ending scene.");
   // Root cells lead the render tweener. Test the actual rendered position, with room
   // for her sprite, rather than removing her as soon as a cell or timer is reached.
   if(Find.Camera.WorldToViewportPoint(knight.DrawPos).y>1.15f){knight.pather.StopDead();return true;}
   var marker=c.MapSpec.Objects.Single(o=>o.Id==a.Text);var destination=new IntVec3(marker.Position.X,0,marker.Position.Z);
   if(knight.CurJob?.def!=JobDefOf.Goto||knight.CurJob.targetA.Cell!=destination){
    var job=JobMaker.MakeJob(JobDefOf.Goto,destination);job.playerForced=true;
    knight.jobs.StartJob(job,JobCondition.InterruptForced);
   }
   return false;
  }
 }
}
