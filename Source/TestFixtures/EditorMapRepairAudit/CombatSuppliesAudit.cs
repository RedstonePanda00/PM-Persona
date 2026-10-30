using System;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimCampaign.Runtime;
using RimWorld;
using Verse;
using Verse.AI;

namespace PMP.Tests {
 public sealed partial class MapDefenseAudit {
  void CombatSuppliesAudit(CampaignController c){
   var supplies=c.Map.GetComponent<CombatSuppliesMapComponent>();
   Check(supplies!=null&&ModsConfig.RoyaltyActive,"Royalty and combat supply map module loaded");
   c.State.Fibers.Clear();c.RestoreControl();c.Communications.Messages.Clear();
   Check(c.MapSpec.Groups.Single(g=>g.Id=="m3-empire-tasks").Actions[0].Kind=="rc.task.prepareCombat","reinforcement task starts with combat preparation");
   var faction=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec,"Empire");
   Check(faction.def.defName=="RC_MapFaction","Sophia Empire remains a campaign-local faction");
   var soldiers=new List<Pawn>();
   for(int i=0;i<4;i++){
    var p=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_M3Knight_ChargeRifle_Good"),faction);
    GenSpawn.Spawn(p,new IntVec3(45+i,0,350),c.Map);soldiers.Add(p);
    Check(p.apparel.WornApparel.Any(a=>a.def.defName=="Apparel_ArmorCataphract")&&p.apparel.WornApparel.Any(a=>a.def.defName=="Apparel_ArmorHelmetCataphract"),"native cataphract armor and helmet generated "+i);
    Check(p.inventory.innerContainer.Where(t=>t.def==ThingDefOf.GoJuice).Sum(t=>t.stackCount)==3,"three carried go-juice before preparation "+i);
    Check(!supplies.Prepare(p)&&p.CurJob?.def==JobDefOf.Ingest,"preparation starts native ingestion and blocks advancement "+i);
   }
   var high=DefDatabase<HediffDef>.GetNamed("GoJuiceHigh");
   for(int i=0;i<700&&!soldiers.All(p=>p.health.hediffSet.HasHediff(high));i++)Find.TickManager.DoSingleTick();
   foreach(var p in soldiers){
    Check(p.health.hediffSet.HasHediff(high)&&supplies.Prepare(p),"actual go-juice effect completes preparation");
    Check(p.inventory.innerContainer.Where(t=>t.def==ThingDefOf.GoJuice).Sum(t=>t.stackCount)==2,"exactly one go-juice consumed");
    Check(supplies.Prepare(p)&&p.inventory.innerContainer.Where(t=>t.def==ThingDefOf.GoJuice).Sum(t=>t.stackCount)==2,"repeated preparation does not consume again");
    p.jobs.StopAll();p.Position=new IntVec3(45+soldiers.IndexOf(p),0,350);
   }
   var shields=soldiers.Where(p=>p.apparel.WornApparel.Any(a=>a is BroadshieldPack)).ToList();
   var foams=soldiers.Where(p=>p.apparel.WornApparel.Any(a=>a.TryGetComp<CompFirefoamPack>()!=null)).ToList();
   Check(shields.Count==2&&foams.Count==2,"packs distributed one-to-one without belt conflict");
   Check(soldiers.All(p=>!supplies.Permit(p,"foam")&&!supplies.Permit(p,"shield")),"no packs spent without local fire or combat");
   var enemy=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec,"Hostile"));
   GenSpawn.Spawn(enemy,new IntVec3(58,0,350),c.Map);
   Check(!supplies.Permit(shields[0],"shield"),"visible enemy alone does not spend a shield before combat");
   foreach(var p in shields)p.mindState.lastAttackTargetTick=Find.TickManager.TicksGame;
   CompApparelReloadable Pack(Pawn p,bool shield)=>p.apparel.WornApparel.First(a=>shield?a is BroadshieldPack:a.TryGetComp<CompFirefoamPack>()!=null).TryGetComp<CompApparelReloadable>();
   Check(Verb_DeployBroadshield.Deploy(Pack(shields[0],true)),"first native shield deploy succeeds during combat");
   Check(!Verb_DeployBroadshield.Deploy(Pack(shields[1],true)),"second native shield deployment is staggered");
   var stamps=(List<SupplyUse>)AccessTools.Field(typeof(CombatSuppliesMapComponent),"uses").GetValue(supplies);
   foreach(var stamp in stamps)stamp.Tick-=180;
   Check(!supplies.Permit(shields[1],"shield"),"active shield coverage blocks overlap even after local cooldown");
   foreach(var projector in c.Map.listerThings.ThingsOfDef(ThingDefOf.BroadshieldProjector).ToList())projector.Destroy();
   Check(Verb_DeployBroadshield.Deploy(Pack(shields[1],true)),"next native shield deploy succeeds once coverage expires");
   var fire=(Fire)ThingMaker.MakeThing(ThingDefOf.Fire);fire.fireSize=.5f;GenSpawn.Spawn(fire,foams[0].Position+IntVec3.North,c.Map);
   var foam=Pack(foams[0],false);Check(Verb_FirefoamPop.Pop(foams[0],foam.parent.TryGetComp<CompExplosive>(),foam),"fire triggers native foam pack");
   var second=Pack(foams[1],false);Check(!Verb_FirefoamPop.Pop(foams[1],second.parent.TryGetComp<CompExplosive>(),second),"second nearby foam pack is not spent simultaneously");
   supplies.SetEnabled(false);Check(!supplies.Manages(soldiers[0]),"map disable releases supply coordination");
   Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native errors during load, drug ingestion and native pack activation");Finish();
  }
 }
}
