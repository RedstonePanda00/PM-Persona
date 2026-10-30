using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Tests {
 public sealed class KaldalisJumpAudit : GameComponent {
  int stage,frames,started,exit;bool done,sawFlight;Pawn kaldalis;Ability jump;IntVec3 destination;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"KaldalisJumpAudit");
  public KaldalisJumpAudit(Game game){}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-kaldalis-jump-audit"))return;
   if(done){if(++exit>60)Application.Quit();return;}
   if(Current.ProgramState!=ProgramState.Playing||Find.CurrentMap==null||++frames<90||LongEventHandler.AnyEventNowOrWaiting)return;
   Directory.CreateDirectory(Folder);
   try{
    var map=Find.CurrentMap;
    Find.TickManager.CurTimeSpeed=TimeSpeed.Fast;
    if(stage==0){
     foreach(var cell in CellRect.CenteredOn(map.Center,14).ClipInsideMap(map)){
      map.roofGrid.SetRoof(cell,null);
      foreach(var thing in cell.GetThingList(map).ToList())if(!(thing is Pawn))thing.Destroy();
      map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);map.fogGrid.Unfog(cell);
     }
     kaldalis=PawnGenerator.GeneratePawn(PawnKindDef.Named("PMPH_Kaldalis"),Faction.OfPlayer);GenSpawn.Spawn(kaldalis,map.Center,map);kaldalis.drafter.Drafted=true;
     jump=kaldalis.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("LegionnaireJump"));
     Check(jump.comps==null,"native Engage ability has a null effect-component list");
     AuditEnergyScope();
     var sentry=PawnGenerator.GeneratePawn(PawnKindDef.Named("PM_Sentry"),Faction.OfPlayer);GenSpawn.Spawn(sentry,map.Center+IntVec3.North*5,map);
     var guardian=sentry.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("PM_GuardianShield"));
     sentry.needs.energy.CurLevelPercentage=.39f;
     Check(!guardian.Activate(sentry,LocalTargetInfo.Invalid)&&Math.Abs(sentry.needs.energy.CurLevelPercentage-.39f)<.001f,"support skill still rejects insufficient energy without charging");
     sentry.needs.energy.CurLevelPercentage=1;
     Check(guardian.Activate(sentry,LocalTargetInfo.Invalid)&&Math.Abs(sentry.needs.energy.CurLevelPercentage-.6f)<.001f,"support skill still charges its configured energy once");
     AuditSupportPaths(map,sentry);
     StartJump(map.Center+IntVec3.East*8);stage=1;return;
    }
    if(stage==1||stage==3){
     sawFlight|=!kaldalis.Spawned;
     if(Find.TickManager.TicksGame-started>600)throw new Exception("Jump did not land");
     if(!sawFlight||!kaldalis.Spawned||kaldalis.Position!=destination)return;
     Check(true,stage==1?"first CastJump job flies and lands at its target":"second CastJump job flies and lands after cooldown");
     Check(jump.CooldownTicksRemaining>0,"successful jump starts cooldown");
     started=Find.TickManager.TicksGame;stage++;return;
    }
    if(stage==2){if(jump.CooldownTicksRemaining>0)return;StartJump(map.Center);stage=3;return;}
    if(stage==4&&Find.TickManager.TicksGame-started>360){
     File.WriteAllLines(Path.Combine(Folder,"errors.txt"),Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text));
     Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no job or ability-tick exceptions across repeated jumps");
     File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Owned support abilities, manual/automatic payment and repeated jumps passed.");done=true;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());done=true;}
  }
  void StartJump(IntVec3 target){
   destination=target;sawFlight=false;started=Find.TickManager.TicksGame;
   Check(jump.verb is Verb_CastAbilityJump,"Engage uses the native jump-order verb");
   jump.verb.OrderForceTarget(target);
  }
  void AuditEnergyScope(){
   var defs=DefDatabase<AbilityDef>.AllDefsListForReading;
   var paid=defs.Where(d=>d.comps.Any(p=>p is CompProperties_SupportEnergy)).OrderBy(d=>d.defName).ToList();
   Check(paid.Select(d=>d.defName).SequenceEqual(new[]{"MechChronoBeam","MechForceFieldWall","PM_GuardianShield","PM_PhaseMode"}),"only four declared sentry support abilities opt into energy handling");
   Check(paid.All(d=>d.abilityClass==typeof(Ability_SupportEnergy)),"all support definitions use their own energy Ability class");
   Check(typeof(Ability_SupportEnergy).Assembly.GetType("ProtossMech.Patch_SupportAbilityPayment")==null,"global support-payment patch class has been removed");
   foreach(var method in typeof(Ability).GetMethods().Where(m=>m.Name=="Activate")){
    var patches=Harmony.GetPatchInfo(method);
    Check(patches==null||!patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Any(p=>p.PatchMethod.DeclaringType?.Name.Contains("Support")==true),"base Ability.Activate has no support-energy Harmony hook: "+method);
   }
   var report=paid.Select(d=>d.defName+": "+string.Join(", ",d.comps.OfType<CompProperties_SupportEnergy>().Select(p=>$"cost={p.cost}, checkOnly={p.checkOnly}, toggle={p.toggleHediff}"))).ToList();
   var owners=DefDatabase<PawnKindDef>.AllDefsListForReading.Where(p=>p.abilities!=null&&p.abilities.Any(paid.Contains)).ToList();
   report.AddRange(owners.Select(p=>"owner "+p.defName+": "+string.Join(", ",p.abilities.Where(paid.Contains).Select(d=>d.defName))));
   int unrelated=0,empty=0;
   foreach(var def in defs.Where(d=>!paid.Contains(d))){
    if(typeof(Ability_SupportEnergy).IsAssignableFrom(def.abilityClass))throw new Exception("Energy scope leak: "+def.defName);
    unrelated++;if(def.comps.Count==0){empty++;report.Add("no-components "+def.defName);}
   }
   Check(true,$"all {unrelated} unrelated loaded abilities retain their own classes ({empty} without components)");
   File.WriteAllLines(Path.Combine(Folder,"energy-scope.txt"),report);
  }
  void AuditSupportPaths(Map map,Pawn sentry){
   var energizer=PawnGenerator.GeneratePawn(PawnKindDef.Named("PM_Energizer"),Faction.OfPlayer);
   GenSpawn.Spawn(energizer,map.Center+IntVec3.West*5,map);
   var phase=energizer.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("PM_PhaseMode"));
   var chrono=energizer.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("MechChronoBeam"));
   var phaseDef=HediffDef.Named("PM_Hediff_EnergizerPhaseMode");
   energizer.needs.energy.CurLevelPercentage=0;
   Check(!phase.Activate(energizer,LocalTargetInfo.Invalid)&&!energizer.health.hediffSet.HasHediff(phaseDef),"zero energy cannot enter phase mode");
   energizer.needs.energy.CurLevelPercentage=.5f;
   Check(phase.Activate(energizer,LocalTargetInfo.Invalid)&&Math.Abs(energizer.needs.energy.CurLevelPercentage-.5f)<.001f,"phase entry retains its zero upfront cost");
   energizer.needs.energy.CurLevelPercentage=.07f;
   Check(!chrono.Activate(sentry,LocalTargetInfo.Invalid)&&Math.Abs(energizer.needs.energy.CurLevelPercentage-.07f)<.001f,"direct automatic-cast entry rejects insufficient chrono energy");
   energizer.needs.energy.CurLevelPercentage=.5f;
   energizer.health.hediffSet.GetFirstHediffOfDef(phaseDef).TryGetComp<HediffComp_EnergizerPhaseMode>().TryAutoCastChronoBeam(energizer);
   Check(Math.Abs(energizer.needs.energy.CurLevelPercentage-.42f)<.001f&&chrono.CooldownTicksRemaining>0,"actual phase-mode automatic chrono cast pays exactly eight percent");
   energizer.needs.energy.CurLevelPercentage=0;
   Check(phase.Activate(energizer,LocalTargetInfo.Invalid)&&!energizer.health.hediffSet.HasHediff(phaseDef),"phase mode can still be switched off at zero energy");
   energizer.needs.energy.CurLevelPercentage=1;
   energizer.GetComp<ProtossMechShield>().ConsumeShieldEnergy(1000);
   float shield=energizer.GetComp<ProtossMechShield>().Energy;
   sentry.needs.energy.CurLevelPercentage=.5f;
   var recharge=sentry.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("MechForceFieldWall"));
   // Put the target within eight cells of the sentry.
   energizer.Position=sentry.Position+IntVec3.East*2;
   Check(recharge.Activate(sentry,LocalTargetInfo.Invalid)&&Math.Abs(sentry.needs.energy.CurLevelPercentage-.49f)<.001f&&energizer.GetComp<ProtossMechShield>().Energy>shield,"recharge performs its effect and pays one percent once");
   var guardian=sentry.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("PM_GuardianShield"));guardian.ResetCooldown();
   sentry.needs.energy.CurLevelPercentage=1;
   Check(guardian.verb.TryStartCastOn(sentry)&&Math.Abs(sentry.needs.energy.CurLevelPercentage-.6f)<.001f,"manual support verb also dispatches through the owned Ability payment");
  }
 }
}
