using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Campaign {
    public sealed class MissionTwoAudit:GameComponent {
        public static bool Enabled=>GenCommandLine.CommandLineArgPassed("pmp-mission-two-audit");
        public static Vector2? Click;
        public static bool Injecting;
        static bool finished;
        int stage,frames,stepFrames,lastStep=-1;
        float started=-1;
        float stepStarted;
        bool stepChecked;
        bool sawBrief,sawForge;
        bool sawBuildingWarp;
        int buildingWarpStart=-1;
        bool sawBuildingOutline;
        bool sawTurret;
        List<string> checks=new List<string>();
        static string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"MissionTwoAudit");
        public MissionTwoAudit(Game game){}
        public override void ExposeData(){Scribe_Values.Look(ref stage,"m2AuditStage");Scribe_Collections.Look(ref checks,"m2AuditChecks",LookMode.Value);}
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add("PASS "+label);File.WriteAllLines(Path.Combine(Folder,"results.txt"),checks);}
        void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Folder,name+".png"));
        void Flush(CampaignController c)=>Traverse.Create(c).Field<Engine>("engine").Value.Tick(0,0);
        void Teleport(Pawn p,IntVec3 cell){p.jobs.StopAll();p.Position=cell;p.Notify_Teleported();}
        void CheckSpear(CampaignController c,int row,bool miss,bool obstruction){
            var start=new IntVec3(20,0,row);var end=new IntVec3(32,0,row);
            var hitCell=obstruction?new IntVec3(27,0,row):end;
            Thing wall=null;
            if(!miss){wall=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_RomulusRedWall"));GenSpawn.Spawn(wall,hitCell,c.Map);}
            var events=c.Map.weatherManager.eventHandler.LiveEventsListForReading;
            int before=events.Count;
            var p=(Projectile_RomulusSpear)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_RomulusSpearBolt"));
            GenSpawn.Spawn(p,start,c.Map);
            p.Launch(c.Actors["hero"],start.ToVector3Shifted(),new LocalTargetInfo(end),miss?new LocalTargetInfo(end+IntVec3.North*2):new LocalTargetInfo(end),ProjectileHitFlags.All);
            var tick=AccessTools.Method(typeof(Projectile),"TickInterval");
            for(int i=0;i<100&&!p.Destroyed;i++)tick.Invoke(p,new object[]{1});
            string kind=miss?"miss":obstruction?"intervening wall":"target hit";
            Check(p.Destroyed&&events.Count==before+1&&events.Last() is WeatherEvent_LightningStrike,"one native lightning on spear "+kind);
            Check(Traverse.Create(events.Last()).Field<IntVec3>("strikeLoc").Value==p.Position,"lightning uses final projectile cell for "+kind);
            if(!miss)Check(wall.Destroyed||wall.HitPoints<wall.MaxHitPoints,"native direct impact damages "+kind);
            if(obstruction)Check(p.Position.x<end.x,"wall intercept stops spear before intended target");
            p.Destroy();Check(events.Count==before+1,"repeated destroy does not duplicate lightning for "+kind);
            if(wall!=null&&!wall.Destroyed)wall.Destroy();
        }
        void Use(CampaignController c,string id){var module=c.Map.GetComponent<SceneInteractionMapComponent>();var spec=module.FindSpec(id);var host=c.Objects[spec.Host];var pawn=c.Actors["hero"];
            var cell=GenAdj.CellsAdjacent8Way(host).First(at=>at.InBounds(c.Map)&&at.Standable(c.Map));Teleport(pawn,cell);Check(module.Complete(spec,pawn),"complete real interaction "+id);Flush(c);}
        public override void GameComponentUpdate(){
            if(!Enabled||finished)return;var c=Current.Game.GetComponent<CampaignController>();if(c?.Active!=true||c.EntryPending||c.Transitioning||c.Map==null)return;
            Directory.CreateDirectory(Folder);frames++;if(started<0)started=Time.realtimeSinceStartup;
            try{
                if(Time.realtimeSinceStartup-started>420)throw new Exception("Mission-two audit timed out at stage "+stage+"; tutorial="+c.Map.GetComponent<TutorialMapComponent>()?.Target);
                if(frames%120==0)File.WriteAllText(Path.Combine(Folder,"progress.txt"),"stage="+stage+" phase="+c.State.Phase+" variables="+Json.Write(c.State.Variables)+"\n"+Json.Write(c.State.Fibers));
                if(stage==0){
                    var view=MissionEffectsMapComponent.CurrentPresentation;if(view.Briefing&&view.Elapsed>19&&!sawBrief){sawBrief=true;Shot("01-tactical-briefing");}
                    if(c.Objects.Any(pair=>pair.Key.StartsWith("building-warp:")&&pair.Value?.TryGetComp<ProtossMech.CompWarpConstructionBeacon>()!=null)){
                        if(buildingWarpStart<0)buildingWarpStart=Find.TickManager.TicksGame;
                        int elapsed=Find.TickManager.TicksGame-buildingWarpStart;
                        if(elapsed>=145&&!sawBuildingWarp){sawBuildingWarp=true;Shot("02-building-warp");}
                        if(elapsed>=300&&!sawBuildingOutline){sawBuildingOutline=true;Shot("02-building-outline");}
                    }
                    if(c.Objects.ContainsKey("m2-forge")&&!sawForge){sawForge=true;Shot("02-forge-arrival");}
                    if(c.Cinematic||!c.State.Variables.TryGetValue("m2-stage",out var value)||value!=1)return;
                    if(c.State.Variables.GetValueOrDefault("m2-forge-ready")!=1){
                        var hero=c.Actors["hero"];if(hero.CurJob?.def!=JobDefOf.Goto)hero.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(140,0,30)),JobCondition.InterruptForced);return;
                    }
                    if(!sawTurret){
                        var turrets=c.Objects.Values.OfType<Building_TurretGun>().ToList();
                        Check(turrets.Count==c.MapSpec.Objects.Count(o=>o.Def?.Name=="PMP_RomulusSpearTurret")&&!c.Objects.ContainsKey("turret-01")&&turrets.All(t=>t.def.defName=="PMP_RomulusSpearTurret"&&t.Active&&t.GetComp<CompPowerTrader>()==null&&t.GetComp<CompRefuelable>()==null),"all authored turrets active without power or fuel; entrance turret absent");
                        Check(turrets.All(t=>t.AttackVerb.verbProps.defaultProjectile.defName=="PMP_RomulusSpearBolt"&&t.TurretTopMaterial.mainTexture!=null),"original turret art and energy projectile resolved");
                        Check(turrets.All(t=>t.def.size==new IntVec2(3,3)&&t.AttackVerb.verbProps.burstShotCount==1&&t.OccupiedRect().All(at=>!at.GetThingList(c.Map).Any(other=>other!=t&&other.def.category==ThingCategory.Building))),"3x3 single-shot ballistas have clear footprints");
                        var spear=DefDatabase<ThingDef>.GetNamed("PMP_RomulusSpearBolt");Check(spear.projectile.GetDamageAmount(null)==80&&spear.projectile.GetArmorPenetration(null)==1.2f&&spear.thingClass==typeof(Projectile_RomulusSpear),"heavy damage and penetration use lightning projectile");
                        CheckSpear(c,3,false,false);CheckSpear(c,6,true,false);CheckSpear(c,9,false,true);
                        Check(!c.MapSpec.Objects.Any(o=>o.Def?.Name=="Wall"||o.Group=="turret-power"||o.Def?.Name=="Table2x2c"||o.Def?.Name=="Stool")&&c.MapSpec.Objects.Any(o=>o.Group=="maze-connections"),"connected Romulus wall layout without obsolete power or domestic furniture");
                        Find.CameraDriver.SetRootPosAndSize(c.Objects["turret-02"].DrawPos,12);Shot("02-romulus-turret-walls");sawTurret=true;return;
                    }
                    Check(sawBrief&&sawForge,"native briefing and forge warp reached");Check(StealthMapComponent.For(c.Map)==null,"mission two has no stealth component");
                    Check(sawBuildingWarp,"forge uses PMC construction warp component");
                    Check(c.Map.GetComponent<SceneInteractionMapComponent>()!=null&&c.Map.GetComponent<TutorialMapComponent>()!=null,"opt-in interaction and tutorial modules attached");
                    var boss=c.Map.GetComponent<BossMapComponent>().Records.Single(r=>r.Id=="centurion");Check(!boss.Active&&boss.Health==5000,"centurion dormant with 5000 HP");
                    Check(!c.Objects.ContainsKey("champion")&&!c.Objects.ContainsKey("knight"),"champion and knight absent before phase three");
                    Check(!c.Map.GetComponent<SceneInteractionMapComponent>().Available(c.Map.GetComponent<SceneInteractionMapComponent>().FindSpec("m2-scan")),"scan locked until centurion defeated");
                    Check(c.RewardStocks.Single(s=>s.Id=="m2-solarite-30").GetDirectlyHeldThings().Sum(t=>t.stackCount)==50,"50 Solarite puzzle reward generated once before runtime");
                    Check(c.RewardStocks.Single(s=>s.Id=="m2-solarite-15").GetDirectlyHeldThings().Sum(t=>t.stackCount)==30,"30 Solarite tutorial reward generated once before runtime");
                    Check(Traverse.Create(c).Field<Engine>("engine").Value.VisibleObjectives().Select(o=>o.Id).SequenceEqual(new[]{"m2-data"}),"exploration displays only scanner installation objective");
                    // The focused objective/reward run keeps the real chapter flow while
                    // leaving the separate cross-map protection audit to its full run.
                    if(!GenCommandLine.CommandLineArgPassed("pmp-mission-two-objectives-audit"))CampaignIsolationAudit.Run(c,Check);
                    Shot("03-objective-install-scanner");
                    // Isolate the scripted feature audit after the native opening; ordinary guards are not under combat test here.
                    foreach(var pawn in c.Objects.Values.OfType<Pawn>().Where(p=>p.kindDef.defName=="PMP_OutpostGuard"&&!p.Dead).ToList())pawn.Kill(null);
                    c.Actors["hero"].health.AddHediff(DefDatabase<HediffDef>.GetNamed("PMP_SceneProtection"));
                    Use(c,"m2-activate-2");Check(c.State.Variables["m2-runes"]==0,"incorrect rune resets without alarm");
                    Use(c,"m2-activate-1");Use(c,"m2-activate-2");Use(c,"m2-activate-3");Check(c.State.Variables["m2-runes"]==3,"muster swear march sequence succeeds");
                    Find.CameraDriver.SetRootPosAndSize(new Vector3(71,0,111),16);Shot("03-activated-obelisks");stage=1;frames=0;return;
                }
                if(stage==1){
                    if(frames<30||c.Presentation)return;
                    Check(c.RewardStocks.Single(s=>s.Id=="m2-solarite-30").Collected,"puzzle transfers original reward instances");
                    if(GenCommandLine.CommandLineArgPassed("pmp-mission-two-objectives-audit")){
                        // Exercise the same native reward transfer used by rc.collect;
                        // the separate gunsmith UI tutorial is outside this focused run.
                        Check(c.CollectMapReward("m2-solarite-15"),"native tutorial reward transfer completes");
                        Check(c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PM_Solarite")).Sum(t=>t.stackCount)==80,"both revised rewards deliver 80 Solarite");
                        Check(c.CollectMapReward("m2-solarite-15")&&c.CollectMapReward("m2-solarite-30")&&c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PM_Solarite")).Sum(t=>t.stackCount)==80,"recollecting either reward cannot duplicate Solarite");
                        Check(c.Map.zoneManager.AllZones.OfType<Zone_Stockpile>().Count()==1,"both rewards reuse one native stockpile");
                        var center=c.MapSpec.Rules.Single(r=>r.Id=="m2-center-enter").Position;
                        Teleport(c.Actors["hero"],CellFinder.StandableCellNear(new IntVec3(center.X,0,center.Z),c.Map,3));stage=3;frames=0;return;
                    }
                    Use(c,"m2-extract");stage=2;frames=0;return;
                }
                if(stage==2){
                    var tutorial=c.Map.GetComponent<TutorialMapComponent>();
                    if(tutorial.Running){
                        if(lastStep!=tutorial.Step){lastStep=tutorial.Step;stepFrames=0;stepStarted=Time.realtimeSinceStartup;stepChecked=false;}stepFrames++;
                        if(!stepChecked&&Time.realtimeSinceStartup-stepStarted>2){Shot("04-tutorial-"+tutorial.Target);Check(tutorial.TargetBounds.HasValue,"actual UI highlight present: "+tutorial.Target+" "+tutorial.TargetBounds);stepChecked=true;}
                        if(Time.realtimeSinceStartup-stepStarted>12)throw new Exception("Tutorial control did not advance: "+tutorial.Target);
                        if(stepChecked&&stepFrames%15==0&&tutorial.TargetBounds.HasValue)Click=tutorial.TargetBounds.Value.center;
                        return;
                    }
                    if(lastStep<4||c.Presentation||frames<120)return;
                    Check(c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PM_Solarite")).Sum(t=>t.stackCount)==80,"zero-price stock swap preserves 80 stored Solarite");
                    Check(c.Map.zoneManager.AllZones.OfType<Zone_Stockpile>().Count()==1,"one native stockpile created and reused for both rewards");
                    Check(c.Map.GetComponent<SceneInteractionMapComponent>().Completed.Contains("m2-extract"),"extraction cannot be repeated");
                    Teleport(c.Actors["hero"],new IntVec3(142,0,115));stage=3;frames=0;return;
                }
                if(stage==3){
                    if(c.Cinematic||!c.State.Variables.TryGetValue("m2-stage",out var value)||value!=2)return;
                    Check(Traverse.Create(c).Field<Engine>("engine").Value.VisibleObjectives().Select(o=>o.Id).SequenceEqual(new[]{"m2-data","m2-centurion"}),"center adds centurion objective alongside scanner installation");
                    var component=c.Map.GetComponent<BossMapComponent>();var boss=component.Records.Single(r=>r.Id=="centurion");Check(boss.Active,"10-cell approach activates encounter");Shot("05-centurion");
                    component.ReceiveHit(boss,new DamageInfo(DamageDefOf.Bullet,100));Check(boss.Health==4900,"centurion receives ordinary weapon damage");component.ReceiveHit(boss,new DamageInfo(DamageDefOf.Bullet,4900));
                    Check(boss.Ended&&boss.Proxy.Dead,"centurion dies normally and never revives");stage=4;frames=0;return;
                }
                if(stage==4){
                    if(frames<40||c.Presentation||c.State.Variables.GetValueOrDefault("m2-stage")!=3)return;
                    Check(c.State.Objectives["m2-centurion"]=="Complete","boss death progresses the objective");Use(c,"m2-scan");stage=5;frames=0;return;
                }
                if(stage==5){
                    if(c.Cinematic||c.State.Variables.GetValueOrDefault("m2-stage")!=4)return;
                    Check(Traverse.Create(c).Field<Engine>("engine").Value.VisibleObjectives().Select(o=>o.Id).SequenceEqual(new[]{"m2-escape"}),"completed scanner cutscene displays only follow-knight extraction objective");
                    if(GenCommandLine.CommandLineArgPassed("pmp-mission-two-objectives-audit")){
                        Check(c.State.Objectives["m2-data"]=="Complete"&&c.State.Objectives["m2-centurion"]=="Complete","scanner and centurion completion retained after objective switch");
                        Shot("06-objective-follow-knight");stage=95;frames=0;
                        GameDataSaveLoader.SaveGame("M2-Objective-Audit");GameDataSaveLoader.LoadGame("M2-Objective-Audit");return;
                    }
                    var component=c.Map.GetComponent<BossMapComponent>();var boss=component.Records.Single(r=>r.Id=="champion");var knight=c.ResolveTriggerPawn("knight");
                    Check(c.Actors["hero"].Position.DistanceTo(knight.Position)<=3&&!c.Presentation,"Mason reunites with knight before control returns during nonfreezing dialogue");
                    c.Actors["hero"].health.AddHediff(DefDatabase<HediffDef>.GetNamed("PMP_SceneProtection"));
                    Check(boss.Template.MaxHealth==77777&&boss.Active,"single 77777 HP champion pursues");Check(knight.Faction!=Faction.OfPlayer&&knight.GetStatValue(StatDefOf.IncomingDamageFactor)==0,"knight autonomous and immune to damage");
                    Check(boss.Proxy.GetStatValue(StatDefOf.PsychicSensitivity)==0,"champion immune to psychic shock");Shot("06-chase");
                    PursuitRevisionAudit.Run(c,boss,Check);
                    var hp=boss.Health;component.ReceiveHit(boss,new DamageInfo(DamageDefOf.Bullet,3000));for(int i=0;i<65;i++)Find.TickManager.DoSingleTick();Check(boss.Health>hp-3000,"champion regenerates after injury");
                    component.ReceiveHit(boss,new DamageInfo(DamageDefOf.Bullet,100000));Check(boss.FallenUntil-Find.TickManager.TicksGame==77*60&&boss.Proxy.Downed&&!boss.Proxy.Dead,"champion downed for exactly 77 seconds");Shot("07-champion-downed");
                    stage=6;GameDataSaveLoader.SaveGame("M2-Revival-Audit");Check(File.Exists(GenFilePaths.FilePathForSavedGame("M2-Revival-Audit")),"same-version chase save written");GameDataSaveLoader.LoadGame("M2-Revival-Audit");return;
                }
                if(stage==95){
                    if(frames<20||c.Cinematic)return;
                    Check(Traverse.Create(c).Field<Engine>("engine").Value.VisibleObjectives().Select(o=>o.Id).SequenceEqual(new[]{"m2-escape"}),"follow-knight objective survives native save/load");
                    Check(c.State.Objectives["m2-data"]=="Complete"&&c.State.Objectives["m2-centurion"]=="Complete","finished objectives survive native save/load");
                    Check(c.RewardStocks.All(s=>s.Collected)&&c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PM_Solarite")).Sum(t=>t.stackCount)==80,"collected 30/50 rewards survive native save/load");
                    Shot("07-objectives-restored");finished=true;File.WriteAllText(Path.Combine(Folder,"complete.txt"),checks.Count+" focused checks passed");
                    GenScene.GoToMainMenu();LongEventHandler.ExecuteWhenFinished(()=>Application.Quit());return;
                }
                if(stage==6){
                    var knightProtection=c.ResolveTriggerPawn("knight").health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamed("PMP_KnightProtection"));
                    Check(knightProtection?.CurStage!=null&&c.Map.GetComponent<MissionEffectsMapComponent>().Owns(knightProtection),"isolation: map-owned knight protection survives same-version save/load");
                    var component=c.Map.GetComponent<BossMapComponent>();var boss=component.Records.Single(r=>r.Id=="champion");Check(boss.FallenUntil>0&&boss.Proxy.Downed,"downed encounter survives save/load");
                    // Isolate revival from immediate follow-up bullets while advancing native ticks.
                    boss.Active=false;
                    for(int i=0;i<77*60+2&&boss.FallenUntil>=0;i++)Find.TickManager.DoSingleTick();Check(boss.FallenUntil<0&&!boss.Proxy.Downed&&boss.Health==77777,"champion revives fully after the saved 77-second timer; health="+boss.Health+" downed="+boss.Proxy.Downed+" deadline="+boss.FallenUntil+" tick="+Find.TickManager.TicksGame);
                    boss.Active=false;stage=60;frames=0;return;
                }
                if(stage==60){
                    if(c.Presentation)return;
                    var module=c.Map.GetComponent<SceneInteractionMapComponent>();var spec=module.FindSpec("m2-use-barrier-01");var host=c.Objects[spec.Host];var pawn=c.Actors["hero"];
                    var blocked=new HashSet<IntVec3>(spec.BlockCells.Select(at=>new IntVec3(at.X,0,at.Z)));
                    Teleport(pawn,GenAdj.CellsAdjacent8Way(host).First(at=>at.InBounds(c.Map)&&at.Standable(c.Map)&&!blocked.Contains(at)));
                    Check(module.Begin(spec,pawn),"native barrier interaction job starts after communications");stage=61;frames=0;return;
                }
                if(stage==61){
                    var module=c.Map.GetComponent<SceneInteractionMapComponent>();var spec=module.FindSpec("m2-use-barrier-01");
                    if(!module.Completed.Contains(spec.Id)){if(frames>600)throw new Exception("Barrier job did not complete; job="+c.Actors["hero"].CurJob+" available="+module.Available(spec)+" hero="+c.Actors["hero"].Position+" guide="+c.ResolveTriggerPawn("knight").Position);return;}
                    Check(spec.BlockCells.All(at=>new IntVec3(at.X,0,at.Z).GetEdifice(c.Map)?.def==ThingDefOf.CollapsedRocks),"collapse creates vanilla thick-roof fallen rocks");
                    Check(c.Objects[spec.Host].Spawned&&c.Objects[spec.Host].def.defName=="PMP_RomulusRedWall"&&!spec.DestroyHost,"activating side wall remains intact after collapse");
                    Check(c.Actors["hero"].CanReach(new IntVec3(70,0,273),PathEndMode.OnCell,Danger.Deadly),"collapse preserves a native escape path");Find.CameraDriver.SetRootPosAndSize(c.Actors["hero"].DrawPos,22);Shot("08-collapsed-route");stage=7;frames=0;return;
                }
                if(stage==7){
                    if(frames<40)return;var escort=c.Map.GetComponent<EscortMapComponent>();var knight=c.ResolveTriggerPawn("knight");
                    Check(escort.Running&&!escort.Arrived,"guide active before extraction");
                    Teleport(knight,new IntVec3(70,0,273));Teleport(c.Actors["hero"],new IntVec3(70,0,273));escort.Index=escort.Waypoints.Count;
                    stage=8;frames=0;return;
                }
                if(stage==8&&c.State.Phase=="Succeeded"){
                    Check(c.State.Objectives["m2-data"]=="Complete"&&c.State.Objectives["m2-escape"]=="Complete","data and player extraction complete");
                    Check(c.ResolveTriggerPawn("knight").Name.ToStringShort==RcText.T("卡珊德拉"),"Cassandra revealed only in ending");Check(!c.Node.Results.StealthRating,"no stealth score on combat mission");Shot("09-results");stage=9;frames=0;return;
                }
                if(stage==9&&frames>40){finished=true;File.WriteAllText(Path.Combine(Folder,"complete.txt"),checks.Count+" checks passed");GenScene.GoToMainMenu();LongEventHandler.ExecuteWhenFinished(()=>Application.Quit());}
            }catch(Exception ex){finished=true;File.WriteAllText(Path.Combine(Folder,"failure.txt"),ex.ToString());Log.Error("[MissionTwoAudit] "+ex);Application.Quit();}
        }
    }
    [HarmonyPatch(typeof(UIRoot_Play),nameof(UIRoot_Play.UIRootOnGUI))]
    static class MissionTwoAuditClick {
        [HarmonyPriority(Priority.First)]
        static void Prefix(){MissionTwoAudit.Injecting=MissionTwoAudit.Enabled&&MissionTwoAudit.Click.HasValue&&Event.current.type==EventType.Repaint;}
        [HarmonyPriority(Priority.Last)]
        static void Postfix(){MissionTwoAudit.Injecting=false;}
    }
    [HarmonyPatch(typeof(Window),"InnerWindowOnGUI")]
    static class MissionTwoAuditWindow {
        [HarmonyPriority(Priority.First)]
        static void Prefix(out Vector2? __state){
            __state=null;MissionTwoAudit.Injecting=MissionTwoAudit.Enabled&&MissionTwoAudit.Click.HasValue&&Event.current.type==EventType.Repaint;
            if(MissionTwoAudit.Injecting){__state=Event.current.mousePosition;Event.current.mousePosition=GUIUtility.ScreenToGUIPoint(MissionTwoAudit.Click.Value)*Prefs.UIScale;}
        }
        [HarmonyPriority(Priority.Last)]
        static void Postfix(Vector2? __state){if(__state.HasValue)Event.current.mousePosition=__state.Value;MissionTwoAudit.Injecting=false;}
    }
    [HarmonyPatch(typeof(UI),nameof(UI.MousePositionOnUIInverted),MethodType.Getter)]
    static class MissionTwoAuditMouse {static void Postfix(ref Vector2 __result){if(MissionTwoAudit.Injecting&&MissionTwoAudit.Click.HasValue)__result=MissionTwoAudit.Click.Value;}}
    [HarmonyPatch(typeof(Mouse),nameof(Mouse.IsOver))]
    static class MissionTwoAuditHover {static void Postfix(Rect rect,ref bool __result){if(MissionTwoAudit.Injecting&&MissionTwoAudit.Click.HasValue)__result=UI.GUIToScreenRect(rect).Contains(MissionTwoAudit.Click.Value);}}
    [HarmonyPatch]
    static class MissionTwoAuditPreview {
        static System.Reflection.MethodBase TargetMethod()=>AccessTools.Method(typeof(ProtossMech.GunSmith.GunSmithEditorUI),"DrawExpandedPreview");
        static void Prefix(Rect canvas,out Vector2? __state){
            __state=null;if(!MissionTwoAudit.Injecting||TutorialMapComponent.Current?.Target!="slot")return;
            // The canvas uses raw local Event coordinates to enable slot controls. No input event is emitted.
            __state=Event.current.mousePosition;Event.current.mousePosition=canvas.center;
        }
        static void Postfix(Vector2? __state){if(__state.HasValue)Event.current.mousePosition=__state.Value;}
    }
    // Drive the rendered native controls in the isolated audit; Waylith polls hardware state for GUI.Button.
    [HarmonyPatch]
    static class MissionTwoAuditButton {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods()=>typeof(Widgets).GetMethods().Where(m=>(m.Name=="ButtonInvisible"||m.Name=="ButtonText")&&m.ReturnType==typeof(bool)&&m.GetParameters().Length>0&&m.GetParameters()[0].ParameterType==typeof(Rect));
        static void Postfix(Rect __0,ref bool __result){
            if(!MissionTwoAudit.Enabled||!MissionTwoAudit.Injecting||!MissionTwoAudit.Click.HasValue||!GUI.enabled)return;
            var bounds=TutorialMapComponent.Current?.TargetBounds;if(!bounds.HasValue)return;
            var r=UI.GUIToScreenRect(__0);if(Vector2.Distance(r.center,bounds.Value.center)<4&&Math.Abs(r.width-bounds.Value.width)<8){__result=true;MissionTwoAudit.Click=null;}
        }
    }
}
