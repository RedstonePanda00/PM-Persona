using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using PMP.Campaign;
using ProtossMech;
using PMP_Persona.PrismSupport;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PMP.Tests {
 [StaticConstructorOnStartup] public static class FiveAuditBootstrap { static FiveAuditBootstrap() { if(GenCommandLine.CommandLineArgPassed("pmp-mission-five-audit"))new Harmony("pmp.mission-five.audit").PatchAll(); } }
 [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
 static class FiveSetup {
  static void Postfix() {
   if(!GenCommandLine.CommandLineArgPassed("pmp-mission-five-audit")) return;
   Current.Game.Scenario=DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario; Find.Scenario.PreConfigure();
   var c=Current.Game.GetComponent<CampaignController>(); var def=DefDatabase<CampaignDef>.GetNamed("PMP_MissionOne"); c.Prepare(def.LoadProject(),def.campaignId,"mission-five");
   Find.GameInitData.startingAndOptionalPawns=c.Actors.Values.ToList(); Find.GameInitData.startingPawnCount=c.Actors.Count;
   Find.GameInitData.mapSize=325; Find.GameInitData.mapGeneratorDef=DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
  }
 }
 public sealed partial class FiveAudit : GameComponent {
  int stage,frames,wideFrames,exitFrames,resultTick; bool done,wide,parallel,briefShot,arrowShot,recallShot,impactShot; float started;
  readonly List<int> buildingStarts=new List<int>(),probeStarts=new List<int>();
  readonly HashSet<string> probeBeacons=new HashSet<string>();
  readonly HashSet<int> focusedSites=new HashSet<int>();
  bool sawSmoothPan,sawGrowingIcon,focusChecked;
  readonly HashSet<string> drillBeacons=new HashSet<string>();
  readonly List<int> drillStarts=new List<int>();bool drillsChecked,drillShot;
  readonly HashSet<string> spoken=new HashSet<string>();
  Pawn refuelProbe; bool sawRefuel,nativeMining;
  double savedSeconds; int savedOre,savedFuel; readonly HashSet<string> buildingBeacons=new HashSet<string>();
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"MissionFiveAudit");
  public FiveAudit(Game game) { }
  void Check(bool ok,string message) { File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message); }
  void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Folder,name+".png"));
  int Ore(Map map)=>map.AllCells.Sum(cell=>map.deepResourceGrid.ThingDefAt(cell)==ThingDefOf.Steel?map.deepResourceGrid.CountAt(cell):0);
  void SetSeconds(MissionFiveMapComponent m,int seconds)=>AccessTools.Field(typeof(MissionFiveMapComponent),"elapsedTicks").SetValue(m,seconds*60);
  public override void ExposeData() {base.ExposeData(); Scribe_Values.Look(ref stage,"fiveAuditStage");Scribe_Values.Look(ref savedSeconds,"savedSeconds");Scribe_Values.Look(ref savedOre,"savedOre");Scribe_Values.Look(ref savedFuel,"savedFuel");Scribe_Values.Look(ref savedPhaseEnd,"savedPhaseEnd");}
  public override void GameComponentUpdate() {
   if(!GenCommandLine.CommandLineArgPassed("pmp-mission-five-audit"))return;
   if(done) {if(++exitFrames>60)Application.Quit();return;}
   var c=Current.Game.GetComponent<CampaignController>(); if(c?.Map==null||c.EntryPending||c.Transitioning)return;
   var m=MissionFiveMapComponent.For(c.Map); if(m==null)return;
   Directory.CreateDirectory(Folder); if(started==0)started=Time.realtimeSinceStartup;
   try {
    if(Time.realtimeSinceStartup-started>420)throw new Exception("Timeout stage="+stage+" seconds="+m.Seconds);
    Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
    if(c.Communications.Current!=null)spoken.Add(c.Communications.Current.Text);
    if(stage<=1&&m.Ready&&!drillsChecked){
     foreach(var b in c.Map.listerThings.AllThings.Where(t=>t.TryGetComp<CompWarpConstructionBeacon>()!=null&&t.Position.z<132))if(drillBeacons.Add(b.ThingID)){
      Check(!c.Cinematic&&!c.Freeze,"deep drill starts after player control returns without pausing");
      var comp=b.TryGetComp<CompWarpConstructionBeacon>();
      drillStarts.Add(Find.TickManager.TicksGame-((int)AccessTools.Field(typeof(CompWarpConstructionBeacon),"totalWarpTicks").GetValue(comp)-(int)AccessTools.Field(typeof(CompWarpConstructionBeacon),"ticksToWarp").GetValue(comp)));
     }
     if(!drillShot&&drillStarts.Count==16&&Find.TickManager.TicksGame-drillStarts[0]>120){Shot("03a-drills-warp-after-control");drillShot=true;}
     if(c.State.Variables.GetValueOrDefault("m5-drills-ready")==1){
      var drills=c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("DeepDrill"));
      Check(drills.Count==16&&drills.All(d=>d.Faction==Faction.OfPlayer),"sixteen native player deep drills delivered");
      Check(drillStarts.Count==16&&Enumerable.Range(1,15).All(i=>Math.Abs(drillStarts[i]-drillStarts[0]-i*5)<=2),"deep drill starts staggered about five ticks");
      Check(CellRect.FromLimits(150,124,173,131).All(cell=>drills.Any(d=>GenRadial.RadialPattern.Take(21).Any(offset=>d.Position+offset==cell))),"native drill ranges cover every rectangular deposit cell");
      Check(drills.All(d=>d.InteractionCell.Standable(c.Map)),"all drill interaction cells remain accessible");
      File.WriteAllText(Path.Combine(Folder,"drill-start-ticks.txt"),string.Join(",",drillStarts));drillsChecked=true;Shot("03b-rectangular-drill-field");
     }
    }
    if(stage==0) {
     frames++;
     var briefing=AccessTools.Field(typeof(MissionFiveMapComponent),"briefing").GetValue(m);
     if(((MissionFiveBriefing)briefing).Visible){
      var type=typeof(MissionFiveBriefing);
      int site=(int)AccessTools.Field(type,"focusSite").GetValue(briefing);
      float age=Time.realtimeSinceStartup-(float)AccessTools.Field(type,"panStarted").GetValue(briefing);
      var focus=(Vector2)AccessTools.Field(type,"focus").GetValue(briefing);
      var from=(Vector2)AccessTools.Field(type,"fromFocus").GetValue(briefing);
      var to=(Vector2)AccessTools.Field(type,"toFocus").GetValue(briefing);
      var reveals=(float[])AccessTools.Field(type,"revealedAt").GetValue(briefing);
      if(site>=0){
       if(age>.4f&&age<.9f&&Vector2.Distance(focus,from)>.01f&&Vector2.Distance(focus,to)>.01f)sawSmoothPan=true;
       float growthAge=Time.realtimeSinceStartup-reveals[site];
       if(growthAge>.15f&&growthAge<.45f&&!sawGrowingIcon){sawGrowingIcon=true;Shot("00b-insignia-growing");}
       if(age>1.6f&&focusedSites.Add(site)){Check(Vector2.Distance(focus,to)<.001f,"camera centered on outpost "+site);Shot("00c-focus-"+site);}
      }
      if(!focusChecked&&c.State.Variables.GetValueOrDefault("m5-brief-stage")>=5&&site==-1&&age>1.5f){
       Check(sawSmoothPan&&sawGrowingIcon&&focusedSites.Count==4,"all four outposts pan smoothly and insignia grow from points");
       Check(reveals[3]<reveals[2]&&reveals[2]<reveals[1]&&reveals[1]<reveals[0]&&reveals[2]-reveals[3]>2,"outposts reveal separately in NE/SE/NW/SW order");
       Check(focus.sqrMagnitude<.000001f,"camera returns to central camp before arrows extend");focusChecked=true;
      }
     }
     if(c.State.Variables.GetValueOrDefault("m5-brief-stage")>=5&&!briefShot){
      var brief=AccessTools.Field(typeof(MissionFiveMapComponent),"briefing").GetValue(m);
      float arrowStart=(float)AccessTools.Field(typeof(MissionFiveBriefing),"arrowsStarted").GetValue(brief);
      float arrowAge=arrowStart<0?0:Time.realtimeSinceStartup-arrowStart;
      if(!arrowShot&&arrowAge>.7f&&arrowAge<1.5f){Shot("00a-arrows-extending");arrowShot=true;}
      if(arrowAge>=2.2f){
      var captures=(RenderTexture[])AccessTools.Field(typeof(MissionFiveBriefing),"captures").GetValue(brief);
      Check(captures.All(t=>t!=null&&t.IsCreated()),"overhead briefing captured three real terrain views");
      Shot("00-overhead-briefing-four-directions");briefShot=true;
      }
     }
     if(!wide&&c.State.Variables.GetValueOrDefault("m5-brief-done")==1&&Find.CameraDriver.RootSize>100&&++wideFrames>12){wide=true;Shot("01-valley-overview");}
     var beacons=c.Map.listerThings.AllThings.Where(t=>t.TryGetComp<CompWarpConstructionBeacon>()!=null).ToList();
     foreach(var b in beacons)if(buildingBeacons.Add(b.ThingID)){
      var comp=b.TryGetComp<CompWarpConstructionBeacon>();
      buildingStarts.Add(Find.TickManager.TicksGame-((int)AccessTools.Field(typeof(CompWarpConstructionBeacon),"totalWarpTicks").GetValue(comp)-(int)AccessTools.Field(typeof(CompWarpConstructionBeacon),"ticksToWarp").GetValue(comp)));
      Check(Math.Abs(Find.CameraDriver.RootSize-24)<.1f,"close camera during building warp");
      if(buildingStarts.Count==1)Check(b.Position.x==c.MapSpec.Objects.Single(o=>o.Id=="m5-pylon").Position.X&&b.Position.z==c.MapSpec.Objects.Single(o=>o.Id=="m5-pylon").Position.Z,"pylon starts first");
      else Check(c.Objects.TryGetValue("m5-pylon",out var pylon)&&pylon.Spawned,"pylon finished before second batch");
     }
     foreach(var b in c.Map.listerThings.AllThings.Where(t=>t.TryGetComp<CompWarpIn>()!=null))if(probeBeacons.Add(b.ThingID)){
      probeStarts.Add(Find.TickManager.TicksGame-(180-b.TryGetComp<CompWarpIn>().ticksToWarp));
      Check(c.Map.listerBuildings.allBuildingsColonist.Count==7,"all buildings finished before probe batch");
     }
     if(beacons.Count==6&&!parallel&&beacons.Any(b=>{
      var comp=b.TryGetComp<CompWarpConstructionBeacon>();
      return (int)AccessTools.Field(typeof(CompWarpConstructionBeacon),"totalWarpTicks").GetValue(comp)-(int)AccessTools.Field(typeof(CompWarpConstructionBeacon),"ticksToWarp").GetValue(comp)>=150;
     })){parallel=true;Shot("02-native-building-warp");}
     if(!m.Ready)return;
     Check(focusChecked&&briefShot&&arrowShot&&wide&&parallel&&buildingBeacons.Count==7,"animated briefing and three native warp batches rendered");
     File.WriteAllText(Path.Combine(Folder,"warp-start-ticks.txt"),"Buildings: "+string.Join(",",buildingStarts)+"\nProbes: "+string.Join(",",probeStarts));
     Check(buildingStarts.Count==7&&Enumerable.Range(2,5).All(i=>Math.Abs(buildingStarts[i]-buildingStarts[1]-(i-1)*5)<=2),"six second-batch buildings start five ticks apart");
     Check(probeStarts.Count==15&&Enumerable.Range(1,14).All(i=>Math.Abs(probeStarts[i]-probeStarts[0]-i*5)<=2),"eight probes and seven defenders start five ticks apart");
     Check(!c.Cinematic&&!c.Freeze,"control restored before Mason's construction communications");
     Check(c.Node.OpeningTitle.Title=="坚守"&&c.Map.Size.x==325&&c.Map.Size.z==325,"title and map dimensions");
     Check(c.Actors.Count==0,"no inherited heroes or fourth-mission reinforcements");
     var players=c.Map.mapPawns.AllPawnsSpawned.Where(p=>p.Faction==Faction.OfPlayer).ToList();
     var probes=players.Where(p=>p.kindDef.defName=="PM_Probe").ToList();
     File.WriteAllText(Path.Combine(Folder,"probe-state.txt"),string.Join("\n",probes.Select(p=>$"{p.ThingID} kind={p.kindDef.defName} race={p.def.defName} drafted={p.Drafted} controlled={p.IsPlayerControlled} dead={p.Dead} downed={p.Downed} manipulation={p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)} equipment={p.equipment?.Primary?.def.defName} fuel={p.equipment?.Primary?.TryGetComp<CompRefuelable>()?.Fuel} canUse={MiningDrillUtility.CanUse(p)} job={p.CurJob?.def.defName}")));
     Shot("arrival-diagnostic");
     Check(probes.Count==8&&probes.All(p=>p.kindDef.defName=="PM_Probe"&&!p.Drafted&&MiningDrillUtility.CanUse(p)&&MiningDrillUtility.GetDrill(p).HasFuel),"eight working player probes with fueled mining guns");
     Check(players.Count==15&&players.Count(p=>p.kindDef.defName=="PM_Sentinel")==3&&players.Count(p=>p.kindDef.defName=="PM_Sentry")==1,"three sentinels and one sentry added to opening force");
     Check(players.Where(p=>p.kindDef.defName!="PM_Probe").All(p=>p.Drafted&&p.equipment?.Primary?.def.defName!="PM_MiningDrill"),"defenders remain drafted with their native combat equipment");
     var fuelStacks=c.Map.listerThings.ThingsOfDef(ThingDefOf.Chemfuel);
     Check(fuelStacks.Sum(t=>t.stackCount)==1000&&fuelStacks.All(t=>t.stackCount<=t.def.stackLimit&&!t.IsForbidden(Faction.OfPlayer)),"1000 usable starting chemfuel in legal stacks");
     var oreCells=c.Map.AllCells.Where(cell=>c.Map.deepResourceGrid.ThingDefAt(cell)==ThingDefOf.Steel).ToList();
     Check(oreCells.Count==192&&oreCells.All(cell=>cell.x>=150&&cell.x<=173&&cell.z>=124&&cell.z<=131&&c.Map.deepResourceGrid.CountAt(cell)==ThingDefOf.Steel.deepCountPerCell),"192 rectangular steel cells use vanilla full deep-resource quantity");
     Check(Ore(c.Map)==57600,"initial full rectangular deposit totals 57600");
     Check(typeof(MissionFiveMapComponent).Assembly.GetType("PMP.Campaign.MissionFiveDrillPermission")==null
      && !DefDatabase<WorkGiverDef>.AllDefsListForReading.Any(d=>d.defName.StartsWith("PMP_M5")),"no chapter-specific drill patch or duplicate work givers");
     var worker=probes[0]; var drill=MiningDrillUtility.GetDrill(worker);
     Check(!worker.IsPlayerControlled,"test probe has no vanilla overseer control");
     var enabled=AccessTools.Field(typeof(MissionFiveMapComponent),"enabled");
     enabled.SetValue(m,false);
     try {
      Check(MissionFiveMapComponent.For(c.Map)==null && MiningDrillUtility.CanUse(worker)
       && !DefDatabase<WorkGiverDef>.GetNamed("PM_MiningDrillDeep").Worker.ShouldSkip(worker),"WildHunt admits autonomous drill user with chapter module disabled");
      Check(worker.equipment.GetGizmos().Contains(drill.StatusGizmo),"autonomous player probe sees mining drill status and fuel controls");
      worker.drafter.Drafted=true;
      Check(!MiningDrillUtility.CanUse(worker),"drafted probes still do not perform drill work");
      worker.drafter.Drafted=false;
      var equipment=worker.equipment.Primary;
      worker.equipment.Remove(equipment);
      try {Check(!MiningDrillUtility.CanUse(worker),"a probe without a mining drill cannot perform drill work");}
      finally {worker.equipment.AddEquipment(equipment);}
     } finally {worker.drafter.Drafted=false;enabled.SetValue(m,true);}
     Check(c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PM_Replicator")).Count==1&&c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("Pylon")).Count==1,"one replicator and one pylon");
     Check(c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PM_HyperspaceStorage")).Count==5,"five hyperspace cabinets");
     Check(c.Objects["m5-storage-0"].TryGetComp<CompHyperspaceStorage>().AssignedDef==ThingDefOf.Steel&&c.Objects["m5-storage-0"].TryGetComp<CompHyperspaceStorage>().StoredCount==0,"steel cabinet preassigned without free stock");
     Check(c.Objects["m5-replicator"].TryGetComp<CompReplicator>().HasEnergyField,"pylon covers replicator");
     Check(DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p=>p.tab?.defName=="ProtossTech").All(p=>p.IsFinished),"all ProtossTech research completed");
     Check(Ore(c.Map)>=7500,"deep steel exceeds 7500");
     Check(c.Map.listerThings.AllThings.OfType<Plant>().Count()>1000&&!c.Map.roofGrid.Roofed(c.Map.Center),"initial vegetation and open central basin");
     foreach(var building in c.Map.listerBuildings.allBuildingsColonist)Check(building.OccupiedRect().All(cell=>c.Map.deepResourceGrid.CountAt(cell)==0),"no deep ore beneath "+building.def.defName);
     foreach(var corner in new[]{new IntVec3(18,0,18),new IntVec3(306,0,18),new IntVec3(18,0,306),new IntVec3(306,0,306)})Check(probes[0].CanReach(corner,PathEndMode.OnCell,Danger.Deadly),"reachable corner "+corner);
     Check(!m.BeginExtraction(),"cannot extract before minimum hold");
     Shot("03-four-entrance-highlights-and-right-hud");
     refuelProbe=worker;worker.jobs.StopAll();drill.Fuel.ConsumeFuel(drill.Fuel.Fuel);
     Check(!DefDatabase<WorkGiverDef>.GetNamed("PM_RefuelMiningDrill_Mining").Worker.ShouldSkip(worker)
      && MiningDrillUtility.RefuelJob(worker)!=null,"native refuel giver admits autonomous probe");
     BeginPrometheusChecks(players);stage=1;
    }
    if(stage==1) {
     bool recoveryDone=CheckOpeningRecovery();
     if(drillsChecked&&recoveryDone&&GenCommandLine.CommandLineArgPassed("pmp-mission-five-opening-audit")){
      Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native error during revised opening");
      File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Fifth-mission opening audit passed, including starting supplies and post-control deep drill warps.");done=true;return;
     }
     sawRefuel |= refuelProbe.CurJob?.def.defName=="PM_MiningDrillRefuel";
     nativeMining |= c.Map.mapPawns.AllPawnsSpawned.Any(p=>p.CurJob?.def.defName=="PM_MiningDrillDeep" && p.CurJob.workGiverDef?.defName=="PM_MiningDrillDeep");
     var storage=c.Objects["m5-storage-0"].TryGetComp<CompHyperspaceStorage>();
     if(storage.StoredCount<75 || !sawRefuel || !MiningDrillUtility.GetDrill(refuelProbe).HasFuel || !drillsChecked || !recoveryDone) {
      if(m.Seconds>60)throw new Exception("No autonomous steel delivery after 60 seconds: "+string.Join("; ",c.Map.mapPawns.AllPawnsSpawned.Select(p=>p.Label+":"+p.CurJob?.def.defName)));
      return;
     }
     Check(sawRefuel&&MiningDrillUtility.GetDrill(refuelProbe).HasFuel,"autonomous probe completed a real native refuel job");
     Check(nativeMining,"deep steel mined through original WildHunt work giver");
     Check(Ore(c.Map)<192*ThingDefOf.Steel.deepCountPerCell,"native mining consumed rectangular deposit resources");Check(storage.StoredCount>=75,"autonomous workers delivered mined steel to locked cabinet");
     var request=new ReplicatorConversionRequest{output=ThingDefOf.Plasteel,inputs=new List<ConversionInput>{new ConversionInput(ThingDefOf.Steel,75,ResourcePaymentSource.Hyperspace)}};
     Check(c.Objects["m5-replicator"].TryGetComp<CompReplicator>().TryStartConversion(request,out var error),"replicator accepts steel-to-plasteel conversion: "+error);
     stage=2;
    }
    if(stage==2) {
     if(c.Map.listerThings.ThingsOfDef(ThingDefOf.Plasteel).Count==0)return;
     Check(true,"replicator produced plasteel using mined steel");Shot("04-working-economy");
     foreach(var p in c.Map.mapPawns.AllPawnsSpawned.Where(p=>p.Faction==Faction.OfPlayer)){p.drafter.Drafted=true;p.jobs.StopAll();}
     PreparePhaseSave(c.Map);
     savedOre=Ore(c.Map);savedFuel=c.Map.listerThings.ThingsOfDef(ThingDefOf.Chemfuel).Sum(t=>t.stackCount);savedSeconds=m.Seconds;stage=3;
     GameDataSaveLoader.SaveGame("MissionFive-Midgame");LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("MissionFive-Midgame"),"LoadingLongEvent",false,null);return;
    }
    if(stage==3) {
     Check(Ore(c.Map)==savedOre,"real save/load does not replenish mined steel");
     Check(c.Map.listerThings.ThingsOfDef(ThingDefOf.Chemfuel).Sum(t=>t.stackCount)==savedFuel&&savedFuel<1000,"starting chemfuel used for native refueling and not duplicated on reload");
     Check(c.Map.mapPawns.AllPawnsSpawned.Count(p=>p.Faction==Faction.OfPlayer)==15,"reload preserves all fifteen opening units without duplicates");
     Check(Math.Abs(m.Seconds-savedSeconds)<2,"hold timer survives save/load");
     Check(c.Map.mapPawns.AllPawnsSpawned.Where(p=>p.Faction==Faction.OfPlayer).All(p=>p.Drafted),"reload preserves player draft changes");
     Check(c.Map.listerBuildings.allBuildingsColonist.Count==23,"reload preserves seven opening buildings and sixteen drills without duplicates");
     Check(DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p=>p.tab?.defName=="ProtossTech").All(p=>p.IsFinished),"research grants survive real save/load");
     BeginNativeWarpCheck(c.Map);stage=31;
    }
    if(stage==31) {
     if(!FinishNativeWarpCheck(c.Map))return;
     SetSeconds(m,180);stage=4;
    }
    if(stage==4) {
     var enemies=c.Map.mapPawns.AllPawnsSpawned.Where(p=>p.HostileTo(Faction.OfPlayer)).ToList();if(enemies.Count==0)return;
     Check(enemies.Count==4&&enemies.All(p=>p.GetLord()?.LordJob is LordJob_AssaultColony),"first authored four-unit wave uses native assault AI");
     foreach(var rule in c.MapSpec.Rules.Where(r=>r.Id.StartsWith("m5-wave-")||r.Id=="m5-endless"))c.State.Enabled[rule.Id]=false;
     foreach(int i in new[]{1,2,3})Check(new MissionFiveWave().Execute(c.MapSpec.Rules.Single(r=>r.Id=="m5-wave-"+i.ToString("00")).Actions[0],c,"",0),"native next wave in SW/NW/SE/NE rotation: "+i);
     enemies=c.Map.mapPawns.AllPawnsSpawned.Where(p=>p.HostileTo(Faction.OfPlayer)).ToList();
     Check(enemies.Count==18,"all four corner spawns present in scripted order");
     Find.CameraDriver.SetRootPosAndSize(enemies[0].DrawPos,22);Shot("05-corner-assault");
     foreach(var pawn in enemies)pawn.Kill(null);
     Check(c.State.Variables.GetValueOrDefault("m5-kills")==18,"native deaths update the score exactly once");
     Check(new MissionFiveWave().Execute(c.MapSpec.Rules.Single(r=>r.Id=="m5-wave-16").Actions[0],c,"",0),"late wave includes guards, chosen, centurions and champion");
     enemies=c.Map.mapPawns.AllPawnsSpawned.Where(p=>p.HostileTo(Faction.OfPlayer)).ToList();
     foreach(string kind in new[]{"PMP_M3Believer","PMP_LegionGuard","PMP_LegionChosen","PMP_LegionCenturion","PMP_SanctuaryChampion"})Check(enemies.Any(p=>p.kindDef.defName==kind),"spawned native "+kind);
     foreach(var pawn in enemies){pawn.jobs.StopAll();pawn.Position=CellFinder.StandableCellNear(c.Map.Center,c.Map,24);pawn.Notify_Teleported();}
     AccessTools.Method(typeof(MissionFiveMapComponent),"CheckFirstContacts").Invoke(m,null);
     foreach(string kind in new[]{"PMP_M3Believer","PMP_LegionGuard","PMP_LegionChosen","PMP_LegionCenturion"})Check(c.State.Variables.GetValueOrDefault("m5-contacted-"+kind)==1,"first-contact line triggered for "+kind);
     int fibers=c.State.Fibers.Count;AccessTools.Method(typeof(MissionFiveMapComponent),"CheckFirstContacts").Invoke(m,null);Check(c.State.Fibers.Count==fibers,"first-contact dialogue is one-shot");
     foreach(var pawn in enemies)pawn.Kill(null);
     var extra=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Pylon"));extra.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(extra,new IntVec3(175,0,170),c.Map);
     Check(c.Map.listerBuildings.allBuildingsColonist.Count==24,"additional player building participates in recall test");
     SetSeconds(m,900);stage=5;
    }
    if(stage==5) {
     if(c.State.Variables.GetValueOrDefault("m5-seconds")<900)return;
     Check(m.BeginExtraction(),"extraction available once minimum time reached");Check(!m.BeginExtraction(),"extraction cannot be queued twice");stage=6;
    }
    if(stage==6) {
     if(!recallShot&&c.State.Variables.Any(k=>k.Key.StartsWith("recall:")&&k.Value>.35)&&c.Map.listerBuildings.allBuildingsColonist.Count>0){Shot("06-unit-and-building-recall");recallShot=true;}
     var effects=c.Map.GetComponent<PrismFractureEffects>();
     if(!impactShot&&effects!=null&&((List<PrismFractureStrike>)AccessTools.Field(typeof(PrismFractureEffects),"strikes").GetValue(effects)).Any(s=>s.Impacted)){Shot("07-native-fracture-impact");impactShot=true;}
     if(c.State.Phase!="Succeeded")return;
     Check(!c.Map.mapPawns.AllPawnsSpawned.Any(p=>p.Faction==Faction.OfPlayer),"all surviving probes recalled from current positions");
     Check(c.State.Objectives["m5-hold"]=="Complete"&&c.State.Objectives["m5-extract"]=="Complete","hold and extraction objectives complete");
     Check(c.Map.listerBuildings.allBuildingsColonist.Count==0&&c.State.Variables.GetValueOrDefault("m5-recalled-buildings")==24,"all twenty-four player buildings recalled, including drills and newly built structure");
     Check(recallShot&&impactShot&&c.State.Variables.GetValueOrDefault("m5-finale-shots")==3,"three native fracture strikes precede results");
     Check(effects!=null&&((List<PrismFractureStrike>)AccessTools.Field(typeof(PrismFractureEffects),"strikes").GetValue(effects)).Count(s=>s.Impacted)==3,"all three first-salvo shots have actually impacted before results");
     resultTick=Find.TickManager.TicksGame;frames=0;stage=7;Shot("08-results-during-shake");return;
    }
    if(stage==7){
     if(++frames<90)return;
     Check(Find.TickManager.TicksGame==resultTick&&c.State.Variables.GetValueOrDefault("m5-finale-shots")==3,"native results pause simulation and remaining salvos without special clock patches");
     Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native error log through opening, economy, save/load, waves and extraction");
     m.RestoreResearch();Check(DefDatabase<ResearchProjectDef>.AllDefsListForReading.Any(p=>p.tab?.defName=="ProtossTech"&&!p.IsFinished),"chapter research grants can be restored without affecting other chapters");
     File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Fifth-mission native audit passed. Timeline advanced after real mining/production; combat isolated. Not a balance verdict.");done=true;
    }
   } catch(Exception ex) {File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),ex.ToString());Log.Error(ex.ToString());done=true;}
  }
 }
}
