using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PMP.Anomaly;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Tests {
 public sealed class StasisAudit : GameComponent {
  int frames,stage,startTick,entityId,cubeId,workerId,exit;
  bool done,loaded;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"StasisAudit");
  public StasisAudit(Game game){}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"stasisAuditStage");Scribe_Values.Look(ref entityId,"stasisEntity");Scribe_Values.Look(ref cubeId,"stasisCube");Scribe_Values.Look(ref workerId,"stasisWorker");}
  public override void LoadedGame(){loaded=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;}
  void Check(bool ok,string why){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+why+"\n");if(!ok)throw new Exception(why);}
  void Finish(){var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);Check(errors.Length==0,"no native errors");File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Anomaly="+ModsConfig.AnomalyActive);done=true;}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-stasis-audit")&&!GenCommandLine.CommandLineArgPassed("pmp-stasis-visual"))return;
   if(done){if(++exit>50)Application.Quit();return;}
   if(Find.CurrentMap==null||++frames<80)return;
   Directory.CreateDirectory(Folder);
   try{
    if(!ModsConfig.AnomalyActive){
     Check(GenTypes.GetTypeInAnyAssembly("PMP.Anomaly.Building_StasisField")==null,"Anomaly assembly excluded without DLC");
     Check(DefDatabase<ThingDef>.GetNamedSilentFail("PMP_StasisField")==null,"building excluded without DLC");
     Check(!"PMP.Stasis.Assign".CanTranslate(),"translations excluded without DLC");Finish();return;
    }
    RunAnomaly();
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text));done=true;}
  }
  [MethodImpl(MethodImplOptions.NoInlining)]
  void RunAnomaly(){
   var map=Find.CurrentMap;var center=map.Center;
   var fields=map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PMP_StasisField")).Cast<Building_StasisField>().OrderBy(f=>f.Position.x).ToList();
   if(stage==0){
    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
    foreach(var cell in CellRect.CenteredOn(center,12)) {foreach(var t in cell.GetThingList(map).ToList())if(t is not Pawn)t.Destroy(DestroyMode.Vanish);map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.roofGrid.SetRoof(cell,null);map.fogGrid.Unfog(cell);}
    for(int i=0;i<3;i++){
     var field=(Building_StasisField)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_StasisField"));field.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(field,center+new IntVec3((i-1)*4,0,0),map);fields.Add(field);
     var gen=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("VanometricPowerCell"));gen.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(gen,center+new IntVec3((i-1)*4,0,4),map);
    }
    for(int x=-5;x<=5;x++){var wire=ThingMaker.MakeThing(ThingDefOf.PowerConduit);wire.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(wire,center+new IntVec3(x,0,4),map);}
    var reserve=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("VanometricPowerCell"));reserve.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(reserve,center+new IntVec3(5,0,5),map);
    var def=fields[0].def;
    Check(def.size==new IntVec2(3,3)&&def.costList.Single(c=>c.thingDef==ThingDefOf.Plasteel).count==100&&def.costList.Single(c=>c.thingDef==ThingDefOf.ComponentSpacer).count==3,"3x3 and exact construction costs");
    Check(def.researchPrerequisites.Count==2&&def.researchPrerequisites.Any(r=>r.defName=="PM_HigherDimensionalSpacetime")&&def.researchPrerequisites.Any(r=>r.defName=="EntityContainment")&&def.GetCompProperties<CompProperties_Power>().PowerConsumption==1000,"Adanium and entity containment research plus 1000W consumption");
    var worker=map.mapPawns.FreeColonistsSpawned.First(p=>GenCommandLine.CommandLineArgPassed("pmp-stasis-visual")||StasisWork.CanResearch(p));workerId=worker.thingIDNumber;worker.Position=center+new IntVec3(-4,0,-4);worker.drafter.Drafted=false;
    var cube=ThingMaker.MakeThing(ThingDefOf.GoldenCube);GenSpawn.Spawn(cube,center+new IntVec3(-6,0,-3),map);cubeId=cube.thingIDNumber;
    var entity=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Sightstealer"),Faction.OfEntities);GenSpawn.Spawn(entity,center+new IntVec3(0,0,-3),map);entityId=entity.thingIDNumber;
    HealthUtility.DamageUntilDowned(entity);
    Check(Building_StasisField.CanCarry(entity)&&Building_StasisField.IsEntity(cube)&&!Building_StasisField.IsEntity(ThingMaker.MakeThing(ThingDefOf.Steel)),"accepts downed entities and cube, rejects ordinary cargo");
    File.WriteAllLines(Path.Combine(Folder,"solar-coverage.txt"),DefDatabase<ThingDef>.AllDefsListForReading.Where(d=>d.category==ThingCategory.Building&&(d.modContentPack?.PackageIdPlayerFacing?.ToLowerInvariant().Contains("protossmechcore")==true||d.modContentPack?.PackageIdPlayerFacing?.ToLowerInvariant().Contains("pmpersona")==true)&&d.GetCompProperties<CompProperties_Power>() is CompProperties_Power p&&p.PowerConsumption>0).Select(d=>d.defName+" immune="+d.HasComp(typeof(ProtossMech.CompIgnoreSolarFlareElectricity))));
    startTick=Find.TickManager.TicksGame;stage=1;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;return;
   }
   var researcher=map.mapPawns.FreeColonistsSpawned.First(p=>p.thingIDNumber==workerId);
   if(stage==1){
    if(!fields.All(f=>f.Powered)){if(Find.TickManager.TicksGame-startTick>=2500){File.WriteAllLines(Path.Combine(Folder,"power.txt"),fields.Select(f=>f.Position+" connected="+(f.GetComp<CompPowerTrader>().PowerNet!=null)+" on="+f.Powered));Check(false,"real generators power all fields");}return;}
    var cube=map.listerThings.ThingsOfDef(ThingDefOf.GoldenCube).Single();fields[0].Assign(cube);
    Check(StasisWork.CanHaul(researcher,fields[0]),"hauler can reserve assigned cube");
    researcher.jobs.StartJob(StasisWork.HaulJob(fields[0]),JobCondition.InterruptForced);
    var entity=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==entityId);
    Check(fields[1].TryAcceptThing(entity),"capture original pawn");
    startTick=Find.TickManager.TicksGame;stage=2;return;
   }
   if(stage==2){
    if(!fields[0].HasAnyContents){Check(Find.TickManager.TicksGame-startTick<4000,"native haul job completes: "+researcher.CurJob);return;}
    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
    Check(fields[0].ContainedThing.thingIDNumber==cubeId&&fields[0].pendingEntity==null,"hauling stores original cube and clears assignment");
    Check(fields[1].HeldPawn.Suspended&&!fields[1].HeldPawn.Spawned,"entity is suspended outside map simulation");
    Check(!fields[1].HeldPawn.IsOutside()&&!fields[0].ContainedThing.IsOutside(),"sealed entities are indoors even on an outdoor platform");
    var interest=(Hediff_CubeInterest)researcher.health.AddHediff(HediffDefOf.CubeInterest);var withdrawal=(Hediff_CubeWithdrawal)researcher.health.AddHediff(HediffDefOf.CubeWithdrawal);interest.Severity=.5f;withdrawal.Severity=.2f;
    var age=interest.ageTicks;interest.TickInterval(60000);withdrawal.TickInterval(60000);
    Check(interest.ageTicks==age&&Math.Abs(withdrawal.Severity-.2f)<.001,"sealed cube pauses existing interest and withdrawal");
    fields[0].EjectContents();interest.TickInterval(1);
    Check(interest.ageTicks>age,"cube influence resumes on release");
    Check(fields[0].TryAcceptThing(map.listerThings.ThingsOfDef(ThingDefOf.GoldenCube).Single()),"cube recapture");
    var study=fields[1].StudyComp;study.Props.minMonolithLevelForStudy=0;study.studyEnabled=true;study.studyInteractions=4;study.studyPoints=1.25f;
    Check(fields[1].CanStudy,"study unlocked while suspended");
    if(GenCommandLine.CommandLineArgPassed("pmp-stasis-visual")){
     Find.CameraDriver.JumpToCurrentMapLoc(center.ToVector3Shifted());Find.CameraDriver.SetRootSize(7);
     stage=6;frames=0;return;
    }
    researcher.jobs.StartJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("PMP_StudyStasis"),fields[1]),JobCondition.InterruptForced);
    startTick=Find.TickManager.TicksGame;stage=3;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;return;
   }
   if(stage==3){
    var study=fields[1].StudyComp;
    if(study.lastStudiedTick<startTick){Check(Find.TickManager.TicksGame-startTick<16000,"native study job finishes: "+researcher.CurJob);return;}
    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
    Check(study.anomalyKnowledgeGained>0&&study.studyPoints>1.25f&&study.studyInteractions==0&&!fields[1].CanStudy,"study grants knowledge, preserves progress and starts cooldown");
    var field=fields[1];var before=field.ContainedThing;
    var flare=GameConditionMaker.MakeCondition(DefDatabase<GameConditionDef>.GetNamed("SolarFlare"),6000);map.gameConditionManager.RegisterCondition(flare);
    Find.TickManager.DebugSetTicksGame((Find.TickManager.TicksGame/20+1)*20);
    for(int i=0;i<100;i++){field.GetComp<CompPowerTrader>().PowerNet.PowerNetTick();AccessTools.Method(typeof(Building_StasisField),"Tick").Invoke(field,null);}
    Check(field.Powered&&field.ContainedThing==before,"WildHunt solar flare immunity prevents entity release");flare.End();
    Find.CameraDriver.JumpToCurrentMapLoc(center.ToVector3Shifted());Find.CameraDriver.SetRootSize(12);
    stage=4;frames=0;return;
   }
   if(stage==6){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"empty-cube-entity.png"));stage=7;frames=0;return;}
   if(stage==7){Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame+90);stage=8;frames=0;return;}
   if(stage==8){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"flow-after-90ticks.png"));Finish();return;}
   if(stage==4){
    ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"empty-cube-entity.png"));
    stage=5;GameDataSaveLoader.SaveGame("StasisAudit");LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("StasisAudit"));return;
   }
   if(stage==5&&loaded){
    Check(fields.Count==3&&fields[0].ContainedThing?.thingIDNumber==cubeId&&fields[1].ContainedThing?.thingIDNumber==entityId,"pawn and cube ownership survive save/load without duplication");
    Check(fields[1].HeldPawn.Suspended&&fields[1].StudyComp.studyPoints>1.25f,"stasis and native study progress survive load");
    var entity=fields[1].HeldPawn;var faction=entity.Faction;var study=fields[1].StudyComp;var points=study.studyPoints;var last=study.lastStudiedTick;
    fields[1].GetComp<CompPowerTrader>().PowerOn=false;AccessTools.Method(typeof(Building_StasisField),"Tick").Invoke(fields[1],null);
    Check(!fields[1].HasAnyContents&&entity.Spawned&&!entity.Suspended&&entity.Faction==faction,"power failure releases the original hostile entity");
    fields[1].GetComp<CompPowerTrader>().PowerOn=true;fields[1].TryAcceptThing(entity);
    Check(fields[1].StudyComp==study&&study.studyPoints==points&&study.lastStudiedTick==last,"release/recapture preserves research and cooldown");
    var cube=fields[0].ContainedThing;fields[0].Destroy(DestroyMode.Deconstruct);
    Check(cube.Spawned&&!cube.Destroyed,"deconstruction releases the original cube");
    fields[1].Destroy(DestroyMode.KillFinalize);Check(entity.Spawned&&!entity.Dead,"destruction releases the entity alive");
    Finish();
   }
  }
 }
}
