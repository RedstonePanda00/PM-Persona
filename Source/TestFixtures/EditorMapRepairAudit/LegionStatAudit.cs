using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Runtime;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using PMP.Campaign;
using PMP_Persona.Romulus;
using PMP_Persona.VanguardSupport;

namespace PMP.Tests {
 public sealed partial class MapDefenseAudit {
  void LegionStatAudit(CampaignController c){
   Prefs.DevMode=true;var guard=c.Map.mapPawns.AllPawnsSpawned.OfType<Pawn_Legion>().First(p=>p.def.defName=="PMP_LegionGuard");
   Check(guard.story!=null&&guard.skills==null,"guard has apparel story metadata and native mech null skills");
   Check(c.Map.GetComponent<OrbitalSupportSession>()==null,"no orbital support started before stat reproduction");
   if(GenCommandLine.CommandLineArgPassed("pmp-legion-stat-baseline")){
    bool reproduced=false;try{guard.GetStatValue(StatDefOf.ShootingAccuracyPawn,true,0);}catch(NullReferenceException e){reproduced=true;File.WriteAllText(Path.Combine(Folder,"reproduced.txt"),e.ToString());}
    Check(reproduced,"same IsDisabledFor null reference reproduced before Prism support");Finish();return;
   }
   void Verify(Pawn pawn){
    var story=pawn.story;pawn.story=null;float expected=StatDefOf.ShootingAccuracyPawn.Worker.GetValue(pawn,true,0);pawn.story=story;
    float actual=StatDefOf.ShootingAccuracyPawn.Worker.GetValue(pawn,true,0);Check(Math.Abs(expected-actual)<.0001f,pawn.def.defName+" retains vanilla mechanical accuracy formula");
    foreach(var stat in DefDatabase<StatDef>.AllDefsListForReading.Where(s=>s.disableIfSkillDisabled!=null||s.skillNeedFactors?.Count>0||s.skillNeedOffsets?.Count>0))stat.Worker.IsDisabledFor(pawn);
    Check(pawn.skills==null,pawn.def.defName+" keeps fixed mechanical skills instead of adding human skill tracker");
   }
   foreach(var pawn in c.Map.mapPawns.AllPawnsSpawned.OfType<Pawn_Legion>().ToList())Verify(pawn);
   var chosen=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_LegionChosen"),guard.Faction);GenSpawn.Spawn(chosen,new IntVec3(370,0,330),c.Map);Verify(chosen);
   var human=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);Check(human.skills!=null&&!StatDefOf.ShootingAccuracyPawn.Worker.IsDisabledFor(human),"ordinary human skill availability remains native");
   c.State.Fibers.Clear();c.RestoreControl();c.Communications.Messages.Clear();
   c.Map.GetComponent<MissionThreeMapComponent>().Begin();var session=c.Map.GetComponent<OrbitalSupportSession>();
   var phase=AccessTools.Field(typeof(OrbitalSupportSession),"Phase");phase.SetValue(session,Enum.Parse(phase.FieldType,"FireControl"));Verify(guard);
   session.Close();Check(c.Map.GetComponent<OrbitalSupportSession>()==null,"Prism session closes cleanly");Verify(guard);
   c.Signal("AssaultVictory","mission-three");Verify(guard);
   guard.GetLord()?.Notify_PawnLost(guard,Verse.AI.Group.PawnLostCondition.ForcedToJoinOtherLord);guard.jobs.StopAll();guard.pather.StopDead();guard.Position=new IntVec3(360,0,330);
   GenSpawn.Spawn(human,new IntVec3(367,0,330),c.Map);human.health.AddHediff(DefDatabase<HediffDef>.GetNamed("PMP_SceneProtection"));
   int shots=0;var verb=guard.equipment.PrimaryEq.PrimaryVerb;
   for(int i=0;i<600;i++){
    if(i%120==0){var job=JobMaker.MakeJob(JobDefOf.AttackStatic,human);job.maxNumStaticAttacks=1;guard.jobs.StartJob(job,JobCondition.InterruptForced);}
    Find.TickManager.DoSingleTick();if(c.Map.listerThings.AllThings.Any(t=>t is Projectile pr&&pr.Launcher==guard))shots++;
   }
   Check(shots>0,"guard launches real projectiles during 600 combat ticks after support closure");
   Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no red errors through stat queries, support closure and combat ticks");Finish();
  }
 }
}
