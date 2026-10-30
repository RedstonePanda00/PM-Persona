using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using PMP_Persona.Prometheus;
using ProtossMech;
using RimWorld;
using Verse;

namespace PMP.Tests {
 public sealed partial class FiveAudit {
  int phaseStart, savedPhaseEnd; bool phaseMidpoint, phaseChecked;
  Pawn[] phasePawns; Thing testGate;
  void Phase(Pawn pawn) {
   var effect=pawn.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("PMP_PrometheusPhase")).CompOfType<CompAbilityEffect_PrometheusPhase>();
   var cell=GenRadial.RadialCellsAround(pawn.Position,5,false).First(c=>effect.Valid(c));
   effect.Apply(cell,cell);Check(pawn.Position==cell,"phase shift actually teleported");
  }
  void BeginPrometheusChecks(List<Pawn> players) {
   // Isolate phase recovery from the opening sentry's automatic allied shield recharge.
   players.Single(p=>p.kindDef.defName=="PM_Sentry").drafter.Drafted=false;
   phasePawns=players.Where(p=>p.kindDef.defName=="PMP_Prometheus").OrderBy(p=>p.thingIDNumber).ToArray();
   Check(phasePawns.Length==3&&phasePawns.All(p=>p.equipment?.Primary?.def.defName=="PMP_HardlightRifle"),"three opening Prometheus carry hardlight rifles");
   Check(phasePawns.All(p=>p.GetComp<ProtossMechShield>().EnergyMax==125),"Prometheus shield cap remains 125");
   var warp=DefDatabase<WarpReinforcementDef>.GetNamed("PMP_WarpPrometheus");
   var sentinel=DefDatabase<WarpReinforcementDef>.GetNamed("PM_WarpSentinel");
   Check(warp.PawnKindDef==phasePawns[0].kindDef&&warp.requiredGateDef==sentinel.requiredGateDef&&warp.researchPrerequisites.SequenceEqual(sentinel.researchPrerequisites),"Prometheus shares Sentinel research and warp gate");
   Check(warp.costList.Count==2&&warp.costList.Single(c=>c.thingDef==ThingDefOf.Plasteel).count==125&&warp.costList.Single(c=>c.thingDef.defName=="PM_Solarite").count==50,"warp costs exactly 125 plasteel and 50 solarite");
   Check(WarpOperations.GetAvailableReinforcementDefs().Contains(warp),"Prometheus appears in native unlocked reinforcement list");
   phasePawns[0].GetComp<ProtossMechShield>().ConsumeShieldEnergy(100);
   phasePawns[1].GetComp<ProtossMechShield>().ConsumeShieldEnergy(10);
   phasePawns[2].GetComp<ProtossMechShield>().ConsumeShieldEnergy(100);
   Phase(phasePawns[0]);Phase(phasePawns[1]);
   var invalid=phasePawns[2].abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("PMP_PrometheusPhase")).CompOfType<CompAbilityEffect_PrometheusPhase>();
   invalid.Apply(phasePawns[2].Position,phasePawns[2].Position);
   Check((int)AccessTools.Field(typeof(CompPrometheusTactics),"phaseShieldUntilTick").GetValue(phasePawns[2].GetComp<CompPrometheusTactics>())==-1,"invalid target does not start a recovery window");
   phasePawns[0].drafter.Drafted=false;phaseStart=Find.TickManager.TicksGame;
  }
  bool CheckOpeningRecovery() {
   if(phaseChecked)return true;
   int elapsed=Find.TickManager.TicksGame-phaseStart;
   if(elapsed>=150&&!phaseMidpoint){
    Check(Math.Abs(phasePawns[0].GetComp<ProtossMechShield>().Energy-(25+Math.Min(300,elapsed)/6f))<.02f,"phase shield recovers gradually while undrafted");phaseMidpoint=true;
   }
   if(elapsed<310)return false;
   Check(Math.Abs(phasePawns[0].GetComp<ProtossMechShield>().Energy-75)<.02f,"phase restores exactly 50 over 300 ticks and stops");
   Check(phasePawns[1].GetComp<ProtossMechShield>().Energy==125,"phase recovery clamps to shield maximum");
   var untouched=phasePawns[2].GetComp<ProtossMechShield>();
   Check(Math.Abs(untouched.Energy-25)<.02f,"invalid phase target gives no shield recovery: energy="+untouched.Energy+", elapsed="+elapsed+", delay="+untouched.Props.ticksToBeginRecharge);
   foreach(var pawn in phasePawns){pawn.drafter.Drafted=true;pawn.GetComp<ProtossMechShield>().RestoreShield();}
   phasePawns[0].Map.mapPawns.AllPawnsSpawned.Single(p=>p.kindDef.defName=="PM_Sentry").drafter.Drafted=true;
   phaseChecked=true;return true;
  }
  void PreparePhaseSave(Map map) {
   var pawn=map.mapPawns.AllPawnsSpawned.Where(p=>p.kindDef.defName=="PMP_Prometheus").OrderBy(p=>p.thingIDNumber).First();
   pawn.GetComp<ProtossMechShield>().ConsumeShieldEnergy(100);Phase(pawn);savedPhaseEnd=Find.TickManager.TicksGame+300;
  }
  void BeginNativeWarpCheck(Map map) {
   foreach(var name in new[]{"PMP_FleetBeacon","PMPH_conclave"}){
    var building=(Building)ThingMaker.MakeThing(ThingDef.Named(name));building.SetFaction(Faction.OfPlayer);
    GenSpawn.Spawn(building,new IntVec3(146,0,164),map);
    Check(building.MaxHitPoints==400&&building.GetComp<ProtossMechShield>().Energy==400,name+" has 400 health plus 400 shield");
    Check(building.Graphic.MatSingle.mainTexture.width==1024,name+" loads replacement sprite");
    building.TakeDamage(new DamageInfo(DamageDefOf.Bullet,100));
    Check(building.HitPoints==400&&Math.Abs(building.GetComp<ProtossMechShield>().Energy-300)<.01f,name+" native shield absorbs damage before health");building.Destroy(DestroyMode.Vanish);
   }
   var warp=DefDatabase<WarpReinforcementDef>.GetNamed("PMP_WarpPrometheus");
   testGate=ThingMaker.MakeThing(warp.requiredGateDef);testGate.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(testGate,new IntVec3(146,0,170),map);
   AccessTools.Field(typeof(CompWarpGate),"cooldownTicksRemaining").SetValue(testGate.TryGetComp<CompWarpGate>(),0);
   var cabinets=map.listerThings.ThingsOfDef(ThingDef.Named("PM_HyperspaceStorage")).Select(t=>t.TryGetComp<CompHyperspaceStorage>()).Where(s=>s.StoredCount==0&&s.AssignedDef==null).Take(2).ToArray();
   Check(cabinets.Length==2,"two empty cabinets available for isolated warp payment check");
   var deposit=AccessTools.Method(typeof(CompHyperspaceStorage),"DepositInternal");
   deposit.Invoke(cabinets[0],new object[]{ThingDefOf.Plasteel,125L});deposit.Invoke(cabinets[1],new object[]{ThingDef.Named("PM_Solarite"),50L});
   Check(WarpOperations.ExecuteReinforcementWarp(warp,new IntVec3(166,0,170)),"native paid warp starts at pylon");
   Check(cabinets.All(s=>s.StoredCount==0)&&!testGate.TryGetComp<CompWarpGate>().IsReady,"native warp consumes exactly both costs and gate charge");
  }
  bool FinishNativeWarpCheck(Map map) {
   var pawns=map.mapPawns.AllPawnsSpawned.Where(p=>p.kindDef.defName=="PMP_Prometheus").OrderBy(p=>p.thingIDNumber).ToArray();
   if(pawns.Length<4)return false;
   Check(pawns.Length==4&&pawns.Last().equipment?.Primary?.def.defName=="PMP_HardlightRifle","paid native warp delivers one armed Prometheus");
   Check(Math.Abs(pawns[0].GetComp<ProtossMechShield>().Energy-75)<.02f,"phase recovery survives real save/load without resetting duration or amount");
   Check(Find.TickManager.TicksGame>=savedPhaseEnd,"saved phase window fully elapsed");
   pawns.Last().Destroy(DestroyMode.Vanish);testGate.Destroy(DestroyMode.Vanish);pawns[0].GetComp<ProtossMechShield>().RestoreShield();return true;
  }
 }
}
