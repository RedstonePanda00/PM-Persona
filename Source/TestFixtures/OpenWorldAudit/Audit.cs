using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using PMP.Campaign;
using RimCampaign.Core;
using RimCampaign.Editor;
using RimCampaign.Runtime;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 public sealed class AuditObstacles : GenStep {
  public static bool Seeded;
  public override int SeedPart=>736529;
  public override void Generate(Map map,GenStepParams parms){
   foreach(var cell in CellRect.CenteredOn(map.Center,1))foreach(var t in cell.GetThingList(map).ToList())if(t is Building||t is Plant){if(t.def.destroyable)t.Destroy();else t.DeSpawn();}
   GenSpawn.Spawn(ThingMaker.MakeThing(ThingDef.Named("Granite")),map.Center,map);
   GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel),map.Center+IntVec3.East,map);
   map.roofGrid.SetRoof(map.Center,RoofDefOf.RoofRockThick);Seeded=true;
  }
 }

 public sealed class OpenWorldAudit : GameComponent {
  int stage,frames,exit,expectedCap;bool done;Map missionMap;Thing podThing;Quest missionQuest;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"OpenWorldAudit");
  public OpenWorldAudit(Game game){}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"auditStage");Scribe_Values.Look(ref expectedCap,"auditCap");Scribe_References.Look(ref missionMap,"auditMap");Scribe_References.Look(ref podThing,"auditPodThing");Scribe_References.Look(ref missionQuest,"auditQuest");}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-open-world-audit"))return;
   if(done){if(++exit>60)Application.Quit();return;}
   if(Current.ProgramState!=ProgramState.Playing||Find.CurrentMap==null||++frames<90||LongEventHandler.AnyEventNowOrWaiting)return;
   Directory.CreateDirectory(Folder);
   try {
    Find.TickManager.CurTimeSpeed=TimeSpeed.Fast;
    if(stage==0){
     TestEditor();TestOutposts();TestMission();TestRotationAndPods();
     stage=3;return;
    }
    if(stage==3){
     if(missionMap.GetComponent<MissionMapComponent>().Controller.State.Variables.GetValueOrDefault("entry-runs")!=1)return;
     stage=1;GameDataSaveLoader.SaveGame("OpenWorldAudit");
     Check(File.Exists(GenFilePaths.FilePathForSavedGame("OpenWorldAudit")),"save written with mission runner and incoming pods");
     GameDataSaveLoader.LoadGame("OpenWorldAudit");return;
    }
    if(stage==1){
     var c=missionMap.GetComponent<MissionMapComponent>().Controller;
     if(!podThing.Spawned)return;
     Check(Current.Game.GetComponent<OpenCampaignOutposts>().HardCap==expectedCap,"world cap survives save/load");
     Check(podThing.Position==missionMap.Center+new IntVec3(10,0,0)&&podThing.Rotation==Rot4.East,"native pod preserves exact building position and orientation after load");
     Check(c.State.Variables.GetValueOrDefault("entry-runs")==1,"mission entry executes once across save/load");
     Check(c.State.Variables.GetValueOrDefault("pulse")>0,"mission periodic trigger runs after load");
     Check(missionMap.GetComponent<CombatPatrolMapComponent>().Units.Count==1,"mission patrol bindings survive save/load");
     c.Signal("Signal","audit-complete");stage=2;return;
    }
    if(stage==2){
     var c=missionMap.GetComponent<MissionMapComponent>().Controller;if(c.State.Phase!="Succeeded")return;
     Check(!c.Freeze,"completed local mission does not freeze the game");
     Check(((MissionSite)missionMap.Parent).Result=="MissionSucceeded","mission reports result to native site");
     Check(missionQuest.State==QuestState.EndedSuccess,"native quest reaches success from mission-site completion signal");
     Check(Current.Game.GetComponent<CampaignController>().Project==null,"local mission does not overwrite the global campaign runner");
     File.WriteAllLines(Path.Combine(Folder,"errors.txt"),Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text));
     Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"native run has no error log entries");
     File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Native editor, daily cap, mission map, patrol, pods and save/load passed.");done=true;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());done=true;}
  }
  static Placement P(string id,string kind,string def,int x,int z,string faction="Player")=>new Placement{Id=id,Kind=kind,Name=id,Def=new DefRef{Name=def,Kind=kind=="PawnKind"?"PawnKindDef":"ThingDef"},Position=new Cell{X=x,Z=z},Faction=faction};
  void TestEditor(){
   var w=new Workspace();var tr=Traverse.Create(w);w.Map.Width=w.Map.Height=40;w.Map.Roles.Clear();
   var wall=P("wall","Thing","Wall",10,10);wall.Stuff=new DefRef{Name="Steel"};
   var guard=P("guard","PawnKind","Colonist",12,12);
   w.Map.Objects.AddRange(new[]{wall,guard,P("floor","Terrain","Concrete",10,10)});
   w.Map.PatrolRoutes.Add(new PatrolRoute{Id="route",Points=new List<PatrolPoint>{new PatrolPoint{Position=new Cell{X=12,Z=12}},new PatrolPoint{Position=new Cell{X=16,Z=12}}}});
   w.Map.CombatPatrols.Add(new CombatPatrolSpec{Pawn="guard",Route="route"});
   tr.Field("selection").SetValue(new HashSet<string>{"wall","guard","floor"});
   tr.Method("SetSelectionFaction","Hostile").GetValue();
   Check(w.Map.Objects.Where(p=>p.Kind!="Terrain").All(p=>p.Faction=="Hostile")&&w.Map.Objects.Single(p=>p.Kind=="Terrain").Faction=="Player","bulk faction edits only faction-capable selected entities");
   tr.Field("history").GetValue<History<Project>>().Undo();Check(w.Map.Objects.All(p=>p.Faction=="Player"),"bulk faction edit is undoable");
   tr.Field("selection").SetValue(new HashSet<string>{"wall","guard"});
   var preset=tr.Method("CaptureStructure",false).GetValue<StructureSpec>();
   Check(preset.Objects.Count==2&&preset.Patrols.Count==1&&preset.PatrolRoutes.Single().Points[1].Position.X==6,"selected preset includes patrol and normalized footprint");
   tr.Method("PastePreset",preset).GetValue();Check(w.Map.CombatPatrols.Count==2&&w.Map.Objects.Select(p=>p.Id).Distinct().Count()==w.Map.Objects.Count,"editable preset import remaps IDs and retains patrol bindings");
   tr.Method("InitializePocketMission").GetValue();w.Map.Entry.Add(new Act{Kind="Set",Target="audit",Value=1});
   var mission=tr.Method("CaptureMissionMap").GetValue<MissionMapSpec>();
   Check(mission.Project.Maps.Single().Base=="PocketMission"&&mission.Project.Maps[0].TerrainInitialization==null&&mission.Project.Maps[0].Entry.Count==1,"pocket export retains logic without source biome initialization");
   MissionMaps.Export(Path.Combine(Folder,"editor.rcmission.json"),mission);
   Check(File.Exists(Path.Combine(Folder,"editor.rcmission.xml")),"mission export writes a companion native definition");
  }
  void TestOutposts(){
   var state=Current.Game.GetComponent<OpenCampaignOutposts>();int initial=OpenCampaignOutposts.SettlementCount;
   state.Begin(Find.CurrentMap.Tile);expectedCap=initial+4;Check(state.HardCap==expectedCap,"cap includes all initial factions and adds four");
   state.GameComponentTick();Check(OpenCampaignOutposts.SettlementCount==initial,"no replenishment before the next local day");
   var tr=Traverse.Create(state);tr.Field("lastDay").SetValue(tr.Field("lastDay").GetValue<long>()-1);state.GameComponentTick();
   Check(OpenCampaignOutposts.SettlementCount==expectedCap,"one daily check fills every vacancy");
   var spawned=Find.WorldObjects.SettlementBases.Where(s=>s.def.defName=="PMP_RomulusOutpost").ToList();
   Check(spawned.Count==4&&spawned.All(s=>s.Faction.def.defName=="PMP_RomulusLegion"),"new settlements use the Romulus faction and outpost definition");
   foreach(var s in spawned.Take(2))Find.WorldObjects.Remove(s);
   state.GameComponentTick();Check(OpenCampaignOutposts.SettlementCount==expectedCap-2,"same-day destruction waits until the next daily check");
   tr.Field("lastDay").SetValue(tr.Field("lastDay").GetValue<long>()-1);state.GameComponentTick();
   Check(OpenCampaignOutposts.SettlementCount==expectedCap,"next day replaces all destroyed settlements");
   state.Begin(Find.CurrentMap.Tile);Check(state.HardCap==expectedCap&&state.Replenish()==0,"re-entry does not raise cap and full worlds do not spawn more");
   var outpost=Find.WorldObjects.SettlementBases.First(s=>s.def.defName=="PMP_RomulusOutpost");
   var map=MapGenerator.GenerateMap(new IntVec3(100,1,100),outpost,outpost.MapGeneratorDef);
   Check(map.listerThings.AllThings.Any(t=>t.def.defName=="PMP_RomulusSubnode")&&map.mapPawns.AllPawnsSpawned.Count(p=>p.Faction==outpost.Faction)==7,"world outpost is enterable with structure, subnode and legion defenders");
  }
  void TestMission(){
   var map=new MapSpec{Id="audit-map",Name="Audit mission",Base="PocketMission",Width=40,Height=40,Roles=new List<string>()};
   map.Objects.Add(P("guard","PawnKind","Colonist",18,18));
   var wall=P("wall","Thing","Wall",15,15);wall.Stuff=new DefRef{Name="Steel"};map.Objects.Add(wall);
   map.PatrolRoutes.Add(new PatrolRoute{Id="route",Points=new List<PatrolPoint>{new PatrolPoint{Position=new Cell{X=18,Z=18}},new PatrolPoint{Position=new Cell{X=24,Z=18}}}});
   map.CombatPatrols.Add(new CombatPatrolSpec{Pawn="guard",Route="route"});
   map.Components.Add(new MapComponentSpec{Type=typeof(CombatPatrolMapComponent).FullName});
   map.Entry.Add(new Act{Kind="Add",Target="entry-runs",Value=1});
   map.Rules.Add(new Rule{Once=false,Events=new List<EventSpec>{new EventSpec{Kind="Periodic",Seconds=.1}},Actions=new List<Act>{new Act{Kind="Add",Target="pulse",Value=1}}});
   map.Rules.Add(new Rule{Events=new List<EventSpec>{new EventSpec{Kind="Signal",Target="audit-complete"}},Actions=new List<Act>{new Act{Kind="Win"}}});
   var node=new Node{Map=map.Id};var template=new MissionMapSpec{Project=new Project{Maps=new List<MapSpec>{map},Campaigns=new List<RimCampaign.Core.Campaign>{new RimCampaign.Core.Campaign{Start=node.Id,Nodes=new List<Node>{node}}}}};
   MissionMaps.Validate(template);
   var def=new MissionMapDef{defName="RC_AuditMission",label="Audit mission",sitePart=DefDatabase<SitePartDef>.GetNamed("RC_MissionMapPart")};Traverse.Create(def).Field("cached").SetValue(template);DefDatabase<MissionMapDef>.Add(def);
   var tile=TileFinder.RandomSettlementTileFor(Faction.OfPlayer);var site=def.CreateSite(tile,Faction.OfPlayer);Find.WorldObjects.Add(site);
   missionQuest=new Quest{id=Find.UniqueIDsManager.GetNextQuestID(),name="Mission completion audit",root=DefDatabase<QuestScriptDef>.AllDefsListForReading.First(),hidden=true};
   var tag="Quest"+missionQuest.id+".missionSite";QuestUtility.AddQuestTag(ref site.questTags,tag);
   missionQuest.AddPart(new QuestPart_QuestEnd{inSignal=tag+".MissionSucceeded",outcome=QuestEndOutcome.Success,sendLetter=false,signalListenMode=QuestPart.SignalListenMode.Always});Find.QuestManager.Add(missionQuest);missionQuest.Accept(null);
   var gen=site.MapGeneratorDef;var obstacles=new GenStepDef{defName="RC_AuditObstacles",order=399,genStep=new AuditObstacles()};gen.genSteps.Add(obstacles);
   try{missionMap=MapGenerator.GenerateMap(new IntVec3(100,1,100),site,gen);}finally{gen.genSteps.Remove(obstacles);}
   Check(AuditObstacles.Seeded,"test seeded thick roof, natural mountain and obstructing building before mission overlay");
   Check(!(missionMap.Center.GetEdifice(missionMap)?.def.building?.isNaturalRock??false)&&missionMap.Center.GetRoof(missionMap)==null,"mission overlay removed seeded mountain and thick roof");
   Check(missionMap.Biome==Find.WorldGrid[tile].PrimaryBiome,"mission ecology matches its destination tile");
   var c=missionMap.GetComponent<MissionMapComponent>().Controller;
   Check(c.Objects.ContainsKey("guard")&&c.Objects.ContainsKey("wall"),"authored entities populate the native biome map");
   Check(missionMap.GetComponent<CombatPatrolMapComponent>().Units.Count==1,"mission guard patrol component is attached");
   Check(new CellRect(31,31,40,40).All(cell=>cell.GetRoof(missionMap)?.isThickRoof!=true),"mission footprint has no thick mountain roof");
  }
  void TestRotationAndPods(){
   var bed=P("bed","Thing","Bed",1,1);bed.Stuff=new DefRef{Name="WoodLog"};bed.Rotation=1;
   var bounds=StructureSpawner.Bounds(bed);bed.Position.X-=bounds.minX;bed.Position.Z-=bounds.minZ;
   var s=new StructureSpec{Width=bounds.Width,Height=bounds.Height,Objects=new List<Placement>{bed}};
   foreach(var rotation in new[]{Rot4.North,Rot4.East,Rot4.South,Rot4.West}){
    var placed=StructureSpawner.At(s,missionMap.Center,rotation).Single();
    Check(StructureSpawner.Bounds(placed).Equals(GenAdj.OccupiedRect(missionMap.Center,rotation,new IntVec2(s.Width,s.Height))),"even-sized rotated footprint: "+rotation);
   }
   var wall=P("podwall","Thing","DiningChair",0,0);wall.Rotation=1;wall.Stuff=new DefRef{Name="WoodLog"};
   var pod=new StructureSpec{Width=1,Height=1,Objects=new List<Placement>{wall}};
   var at=missionMap.Center+new IntVec3(10,0,0);foreach(var t in at.GetThingList(missionMap).ToList())if(t is Building||t is Plant)t.Destroy();missionMap.roofGrid.SetRoof(at,null);
   podThing=StructureSpawner.Spawn(pod,missionMap,at,Rot4.North,"Pods",id=>Faction.OfPlayer).Single();
   ((ActiveTransporterInfo)podThing.ParentHolder).openDelay=600;
   Check(!podThing.Spawned&&podThing.ParentHolder!=null,"preset begins inside a native incoming pod");
  }
 }
}
