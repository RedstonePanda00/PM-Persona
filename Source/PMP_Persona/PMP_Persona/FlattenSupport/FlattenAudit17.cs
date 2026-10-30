using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona.FlattenSupport
{
    public sealed class FlattenAudit17 : GameComponent
    {
        private int frames,stage,startTick,humanId,animalId,mechId,enemyId,entityId;
        private long frozenAge;
        private float frozenFood,knowledgeBefore;
        private bool loading,done,transitionSaved;
        private int lastVisualShot=-1;
        private IntVec3 center;
        private Pawn researcher;
        private Thing wall,steel,corpse;
        private string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"FlattenAudit17");
        public FlattenAudit17(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage,"flattenAuditStage"); Scribe_Values.Look(ref center,"flattenAuditCenter");
            Scribe_Values.Look(ref humanId,"flattenAuditHuman");Scribe_Values.Look(ref animalId,"flattenAuditAnimal");
            Scribe_Values.Look(ref mechId,"flattenAuditMech");Scribe_Values.Look(ref enemyId,"flattenAuditEnemy");
            Scribe_Values.Look(ref entityId,"flattenAuditEntity");Scribe_Values.Look(ref frozenAge,"flattenAuditAge");
            Scribe_Values.Look(ref frozenFood,"flattenAuditFood");Scribe_References.Look(ref researcher,"flattenAuditResearcher");
            Scribe_References.Look(ref wall,"flattenAuditWall");Scribe_References.Look(ref steel,"flattenAuditSteel");
            Scribe_References.Look(ref corpse,"flattenAuditCorpse");
            Scribe_Values.Look(ref startTick,"flattenAuditStartTick");
            Scribe_Values.Look(ref transitionSaved,"flattenAuditTransitionSaved");
        }
        public override void LoadedGame()
        {if(GenCommandLine.CommandLineArgPassed("pmp17flattenaudit"))Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;}
        private List<Thing_FlattenedPawn> Sheets(Map map)=>map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PMP_FlattenedPawn")).Cast<Thing_FlattenedPawn>().ToList();
        private Thing_FlattenedPawn Sheet(Map map,int id)=>Sheets(map).Single(s=>s.HeldPawn.thingIDNumber==id);
        public override void GameComponentUpdate()
        {
            if(!GenCommandLine.CommandLineArgPassed("pmp17flattenaudit")||done||loading||Find.CurrentMap==null||++frames<90)return;
            try
            {
                Directory.CreateDirectory(Folder);
                var map=Find.CurrentMap;
                if(stage==0)
                {
                    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;center=map.Center;
                    var rect=CellRect.CenteredOn(center,20).ClipInsideMap(map);GenDebug.ClearArea(rect,map);
                    foreach(var cell in rect){map.fogGrid.Unfog(cell);map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);}
                    var human=Spawn(PawnKindDefOf.Colonist,Faction.OfPlayer,new IntVec3(-4,0,0));humanId=human.thingIDNumber;
                    human.Rotation=Rot4.West;
                    human.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_Parka"),ThingDefOf.Cloth));
                    human.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(ThingDefOf.Steel));
                    human.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_Autopistol")));
                    var animal=Spawn(DefDatabase<PawnKindDef>.GetNamed("Muffalo"),Faction.OfPlayer,new IntVec3(-1,0,0));animalId=animal.thingIDNumber;
                    var mech=Spawn(DefDatabase<PawnKindDef>.GetNamed("Mech_Scyther"),Faction.OfMechanoids,new IntVec3(2,0,0));mechId=mech.thingIDNumber;
                    var enemy=Spawn(PawnKindDefOf.Colonist,Faction.OfAncientsHostile,new IntVec3(7,0,7));enemyId=enemy.thingIDNumber;
                    for(int attempt=0;attempt<100;attempt++)
                    {
                        var candidate=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);
                        if(!candidate.skills.GetSkill(SkillDefOf.Intellectual).TotallyDisabled
                            && (!ModsConfig.AnomalyActive || FlattenAnomaly.CanResearch(candidate)))
                        {researcher=candidate;break;}
                        candidate.Destroy();
                    }
                    Check(researcher!=null,"Could not generate a capable research fixture");
                    GenSpawn.Spawn(researcher,center+new IntVec3(11,0,0),map);
                    researcher.skills.GetSkill(SkillDefOf.Intellectual).Level=20;
                    researcher.needs.food.CurLevel=1;researcher.needs.rest.CurLevel=1;
                    var dead=Spawn(PawnKindDefOf.Colonist,Faction.OfAncientsHostile,new IntVec3(0,0,-3));dead.Kill(null);corpse=dead.Corpse;
                    wall=GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel),center+new IntVec3(-3,0,3),map);
                    steel=GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Steel),center+new IntVec3(0,0,3),map);
                    map.roofGrid.SetRoof(center,RoofDefOf.RoofConstructed);
                    if(ModsConfig.AnomalyActive)
                    {
                        var entity=Spawn(DefDatabase<PawnKindDef>.GetNamed("Sightstealer"),Faction.OfEntities,new IntVec3(4,0,0));entityId=entity.thingIDNumber;
                        var comp=entity.TryGetComp<CompStudiable>();comp.studyPoints=1.25f;comp.studyInteractions=2;
                        comp.lastStudiedTick=Find.TickManager.TicksGame-1000000;
                    }
                    Check(FlattenShipSupportWorker.Area(center,map).Area==256,"Wrong footprint size");
                    Check(FlattenShipSupportWorker.Activate(map,center,new FlattenConfig()),"Activation failed");
                    Check(Sheets(map).Count==(ModsConfig.AnomalyActive?5:4),"Non-pawns captured / expected pawn missed");
                    var held=Sheet(map,humanId).HeldPawn;frozenAge=held.ageTracker.AgeBiologicalTicks;frozenFood=held.needs.food.CurLevel;
                    Check(!held.Spawned&&held.Suspended,"Pawn not suspended");
                    Check(!Sheet(map,humanId).TryRelease(),"Released before transition finished");
                    Check(Sheets(map).All(s=>s.DisplayFacing==Rot4.South),"Default facing not south");
                    Check(wall.Spawned&&steel.Spawned&&corpse.Spawned&&researcher.Spawned&&center.Roofed(map),"Non-target changed");
                    Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(),14);
                    startTick=Find.TickManager.TicksGame;stage=1;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
                    return;
                }
                if(stage==1)
                {
                    int elapsed=Find.TickManager.TicksGame-startTick;
                    int shot=elapsed/20;
                    if(shot!=lastVisualShot && elapsed<=140)
                    {lastVisualShot=shot;ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"Live-"+shot.ToString("00")+".png"));}
                    if(elapsed>=35 && !transitionSaved)
                    {
                        Check(FlattenVisual.ActiveCount>0,"Transition never captured a RenderTree");
                        transitionSaved=true;stage=5;loading=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                        GameDataSaveLoader.SaveGame("PMPFlattenTransition");
                        LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("PMPFlattenTransition"));return;
                    }
                    if(Find.TickManager.TicksGame-startTick<180)return;
                    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                    var sheet=Sheet(map,humanId);var held=sheet.HeldPawn;
                    Check(held.ageTracker.AgeBiologicalTicks==frozenAge&&held.needs.food.CurLevel==frozenFood,"Contained pawn ticked");
                    Check(!sheet.def.useHitPoints&&!sheet.def.destroyable&&!sheet.CanStackWith(Sheet(map,animalId)),"Not an indestructible unique item");
                    sheet.TakeDamage(new DamageInfo(DamageDefOf.Bomb,100000));Check(sheet.Spawned&&!held.Dead,"Sheet damaged");
                    Check(FlattenAnomaly.StudyComp(Sheet(map,animalId))==null,"Ordinary animal exposed anomaly study");
                    Check(FlattenVisual.ActiveCount==0,"Transition textures leaked after completion");
                    CheckVisualTransition(sheet);
                    if(!ModsConfig.AnomalyActive)
                    {
                        Check(DefDatabase<WorkGiverDef>.GetNamedSilentFail("PMP_StudyFlattenedPawn")==null,"Anomaly work def loaded without DLC");
                        Check(FlattenAnomaly.StudyComp(sheet)==null&&!FlattenAnomaly.CanStudy(sheet),"Anomaly logic active without DLC");
                    }
                    CaptureRender(sheet,"Human");CaptureRender(Sheet(map,animalId),"Animal");CaptureRender(Sheet(map,mechId),"Mech");
                    sheet.CycleFacing();Check(sheet.DisplayFacing==Rot4.East,"South to east failed");CaptureRender(sheet,"HumanEast");
                    sheet.CycleFacing();Check(sheet.DisplayFacing==Rot4.North,"East to north failed");CaptureRender(sheet,"HumanNorth");
                    ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"Sheets.png"));
                    stage=2;loading=true;GameDataSaveLoader.SaveGame("PMPFlattenAudit");
                    LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("PMPFlattenAudit"));return;
                }
                if(stage==5)
                {
                    Check(!Sheet(map,humanId).Ready && Sheet(map,humanId).Progress>.1f,"In-progress transition reset or skipped on load");
                    stage=1;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;return;
                }
                if(stage==2)
                {
                    var sheet=Sheet(map,humanId);var held=sheet.HeldPawn;
                    Check(sheet.DisplayFacing==Rot4.North,"Display facing lost on save/load");
                    sheet.CycleFacing();Check(sheet.DisplayFacing==Rot4.South,"North to south failed");
                    Check(held.Suspended&&held.ageTracker.AgeBiologicalTicks==frozenAge&&held.Faction==Faction.OfPlayer,"Held identity/faction/suspension lost");
                    Check(held.apparel.WornApparel.Any(a=>a.def.defName=="Apparel_Parka")&&held.equipment.Primary.def.defName=="Gun_Autopistol"&&held.inventory.innerContainer.Any(t=>t.def==ThingDefOf.Steel),"Equipment/inventory lost");
                    var blocked=GenRadial.RadialCellsAround(sheet.Position,5,true).Where(c=>c.InBounds(map)).ToList();
                    foreach(var c in blocked)map.terrainGrid.SetTerrain(c,DefDatabase<TerrainDef>.GetNamed("WaterDeep"));
                    Check(!sheet.TryRelease()&&sheet.HeldPawn==held&&!sheet.Destroyed,"Failed release lost pawn");
                    foreach(var c in blocked)map.terrainGrid.SetTerrain(c,TerrainDefOf.Soil);
                    Check(sheet.TryRelease()&&sheet.Destroyed&&held.Spawned&&!held.Suspended,"Successful release failed");
                    Check(held.Rotation==Rot4.West,"Display rotation altered the release facing");
                    var enemy=Sheet(map,enemyId);var enemyPawn=enemy.HeldPawn;
                    Check(enemy.TryRelease()&&enemyPawn.Faction==Faction.OfAncientsHostile,"Enemy converted on release");
                    var resealedEnemy=(Thing_FlattenedPawn)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_FlattenedPawn"));
                    resealedEnemy.Capture(enemyPawn,0); // Isolate the research fixture from combat.
                    // Hauling/transport nesting must preserve the suspension contract.
                    var animalSheet=Sheet(map,animalId);animalSheet.DeSpawn();researcher.inventory.innerContainer.TryAdd(animalSheet,false);
                    Check(animalSheet.HeldPawn.Suspended&&!animalSheet.TryRelease(),"Nested sheet can escape/release while carried");
                    Check(researcher.inventory.innerContainer.TryDrop(animalSheet,center,map,ThingPlaceMode.Near,out _),"Cannot drop hauled sheet");
                    CaptureRender(animalSheet,"RestoredAnimal");
                    ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"Restored.png"));
                    if(!ModsConfig.AnomalyActive){stage=4;frames=0;return;}
                    var entitySheet=Sheet(map,entityId);var study=FlattenAnomaly.StudyComp(entitySheet);
                    Check(study!=null&&Mathf.Abs(study.studyPoints-1.25f)<.0001f&&study.studyInteractions==2,"Study progress lost on load");
                    study.Props.minMonolithLevelForStudy=0; // Unlock study in the isolated fixture.
                    study.studyEnabled=true;
                    Check(FlattenAnomaly.CanStudy(entitySheet),"Sealed entity is not study eligible");
                    var work=DefDatabase<WorkGiverDef>.GetNamed("PMP_StudyFlattenedPawn").Worker as WorkGiver_StudyFlattenedPawn;
                    Check(work.HasJobOnThing(researcher,entitySheet,true),"Researcher cannot reach/reserve sheet");
                    var job=work.JobOnThing(researcher,entitySheet,true);job.playerForced=true;
                    researcher.jobs.StartJob(job,JobCondition.InterruptForced);
                    knowledgeBefore=study.anomalyKnowledgeGained;
                    startTick=Find.TickManager.TicksGame;stage=3;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;return;
                }
                if(stage==3)
                {
                    var sheet=Sheet(map,entityId);var comp=FlattenAnomaly.StudyComp(sheet);
                    if(comp.lastStudiedTick<startTick)
                    {Check(Find.TickManager.TicksGame-startTick<16000,"Study job failed to finish: "+researcher.CurJob+" rate="+researcher.GetStatValue(StatDefOf.EntityStudyRate)+" interactions="+comp.studyInteractions);return;}
                    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                    Check(comp.anomalyKnowledgeGained>knowledgeBefore&&comp.studyInteractions==0,"Study did not grant native knowledge / finish session");
                    Check(sheet.HeldPawn.Suspended&&!sheet.HeldPawn.Spawned&&!FlattenAnomaly.CanStudy(sheet),"Entity escaped / cooldown missing");
                    int last=comp.lastStudiedTick;float points=comp.studyPoints;var entity=sheet.HeldPawn;
                    Check(sheet.TryRelease()&&entity.Spawned,"Entity release failed");
                    var again=(Thing_FlattenedPawn)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_FlattenedPawn"));again.Capture(entity,0);
                    Check(FlattenAnomaly.StudyComp(again)==comp&&comp.lastStudiedTick==last&&comp.studyPoints==points&&!FlattenAnomaly.CanStudy(again),"Release/recapture resets study");
                    stage=4;frames=0;return;
                }
                if(stage==4)
                {
                    if(frames==90) {Find.Selector.ClearSelection();Find.Selector.Select(Sheet(map,animalId));return;}
                    if(frames==92) {ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"SelectedSheet.png"));return;}
                    if(frames<95)return;
                    Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),string.Join("\n",Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text)));
                    File.WriteAllText(Path.Combine(Folder,"results.txt"),"PASS: 16x16 Pawn-only capture; all factions; human/animal/mech RenderTree; identity/apparel/equipment/inventory; suspension and damage immunity; save/load; blocked release retains pawn; valid release restores original faction; nested hauling; native GLSL transition samples/endpoints, in-progress save/load and resource cleanup; Anomaly active="+ModsConfig.AnomalyActive+"; native entity study/cooldown preserved when active; zero Verse errors.");
                    Finish("PASS");
                }
            }
            catch(Exception ex){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"results.txt"),ex.ToString());Finish("FAIL");}
        }
        private Pawn Spawn(PawnKindDef kind,Faction faction,IntVec3 offset)
        {var pawn=PawnGenerator.GeneratePawn(kind,faction);GenSpawn.Spawn(pawn,center+offset,Find.CurrentMap);return pawn;}
        private void CheckVisualTransition(Thing_FlattenedPawn sheet)
        {
            using(var capture=FlattenVisual.CaptureTree(sheet))
            {
                var camera=TargetOutlineCaptureCamera.Create();
                var rt=RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGB32);
                var image=new Texture2D(256,256,TextureFormat.RGBA32,false);
                var previous=RenderTexture.active;
                try
                {
                    float[] times={0,.15f,.35f,.55f,.75f,.9f,1};
                    var counts=new List<int>();Color32[] early=null;
                    for(int i=0;i<times.Length;i++)
                    {
                        FlattenVisual.Configure(capture.Material,times[i],capture.HalfSize,true);
                        camera.RenderTo(rt,capture.HalfSize*2,()=>
                        {capture.Material.SetPass(0);Graphics.DrawMeshNow(MeshPool.plane10,Matrix4x4.Scale(new Vector3(capture.HalfSize*4,1,capture.HalfSize*4)));});
                        RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                        var pixels=image.GetPixels32();int count=pixels.Count(p=>p.a>10);counts.Add(count);
                        if(i==0||i==times.Length-1)Check(count==0,"Transition endpoint is not transparent");
                        else Check(count>300,"Transition invisible: "+times[i]);
                        if(i==1)early=pixels;
                        if(i==3)Check(pixels.Where((p,k)=>Math.Abs(p.a-early[k].a)>12).Count()>500,"Transition appears static");
                        File.WriteAllBytes(Path.Combine(Folder,"Transition-"+i+".png"),image.EncodeToPNG());
                    }
                    File.WriteAllText(Path.Combine(Folder,"transition-counts.txt"),string.Join(",",counts));
                }
                finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(camera.gameObject);}
            }
        }
        private void CaptureRender(Thing_FlattenedPawn sheet,string name)
        {
            var camera=TargetOutlineCaptureCamera.Create();var rt=RenderTexture.GetTemporary(256,256,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(256,256,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
            try
            {
                var parms=sheet.DrawParameters(Vector3.zero);parms.flags|=PawnRenderFlags.DrawNow;
                var tree=sheet.HeldPawn.Drawer.renderer.renderTree;tree.EnsureInitialized(parms.flags);tree.ParallelPreDraw(parms);
                camera.RenderTo(rt,2,()=>tree.Draw(parms));RenderTexture.active=rt;
                image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                Check(image.GetPixels32().Count(c=>c.a>20&&c.r+c.g+c.b>20)>100,"Blank RenderTree: "+name);
                File.WriteAllBytes(Path.Combine(Folder,name+".png"),image.EncodeToPNG());
                tree.SetDirty();
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(image);RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(camera.gameObject);}
        }
        private static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
        private void Finish(string result){done=true;File.WriteAllText(Path.Combine(Folder,"complete.txt"),result);Application.Quit();}
    }
}
