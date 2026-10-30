using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Campaign {
    // Opt-in regression: real result window button -> map transition -> visible briefing -> opening fight.
    public sealed class TransitionOpeningAudit:GameComponent {
        internal static bool Enabled=>GenCommandLine.CommandLineArgPassed("pmp-transition-opening-audit");
        static bool TestDowned=>GenCommandLine.CommandLineArgPassed("pmp-opening-downed-audit");
        static bool TestInside=>GenCommandLine.CommandLineArgPassed("pmp-forge-inside-audit");
        internal static bool ClickContinue;
        internal static readonly HashSet<string> Fired=new HashSet<string>();
        readonly HashSet<string> outside=new HashSet<string>();
        List<string> checks=new List<string>();
        static readonly string[] sentries={"sentry-01","m2-entry-guard"};
        static string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"TransitionOpeningAudit");
        bool finished,briefChecked,fightShot,incapacitated;int stage,waitFrames;float started=-1,nextTrace;
        Map previousMap;MissionEffectsMapComponent previousEffects;string heroId,forgeId;bool warpSeen,insideEnabled;
        const string ForgeTrigger="m2-forge-arrival";
        public TransitionOpeningAudit(Game game){}
        public override void ExposeData(){Scribe_Values.Look(ref stage,"entryAuditStage");Scribe_Collections.Look(ref checks,"entryAuditChecks",LookMode.Value);Scribe_Values.Look(ref forgeId,"entryAuditForge");}
        void Flush(CampaignController c){c.PollTriggers();Traverse.Create(c).Field<Engine>("engine").Value.Tick(0,0);}
        void Teleport(Pawn p,IntVec3 at){p.jobs.StopAll();p.Position=at;p.Notify_Teleported();}
        void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add("PASS "+label);File.WriteAllLines(Path.Combine(Folder,"results.txt"),checks);}
        void Shot(string name)=>ScreenCapture.CaptureScreenshot(Path.Combine(Folder,name+".png"));
        public override void GameComponentUpdate(){
            if(!Enabled||finished)return;
            Directory.CreateDirectory(Folder);if(started<0)started=Time.realtimeSinceStartup;
            try{
                if(Time.realtimeSinceStartup-started>240)throw new Exception("Transition opening timed out at stage "+stage);
                var c=Current.Game?.GetComponent<CampaignController>();
                if(c?.Active!=true||c.EntryPending||c.Transitioning)return;
                if(Time.realtimeSinceStartup>=nextTrace){
                    nextTrace=Time.realtimeSinceStartup+3;
                    File.WriteAllText(Path.Combine(Folder,"progress.txt"),"stage="+stage+" cinematic="+c.Cinematic+" freeze="+c.Freeze+" speed="+Find.TickManager.CurTimeSpeed+"\n"+RimCampaign.Core.Json.Write(c.State.Fibers)+"\n"+string.Join("\n",sentries.Select(id=>{var p=c.ResolveTriggerPawn(id);return id+" at="+p?.Position+" down="+p?.Downed+" dead="+p?.Dead+" job="+p?.CurJob;})));
                }
                if(stage==0){
                    if(c.Cinematic||c.Presentation||!c.Actors["hero"].Spawned)return;
                    Check(c.NodeId=="mission-one","starts in actual first mission");
                    previousMap=c.Map;previousEffects=c.Map.GetComponent<MissionEffectsMapComponent>();heroId=c.Actors["hero"].ThingID;
                    Find.Selector.Select(c.Actors["hero"]);Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Inspect,false);
                    Check(Find.WindowStack.Windows.OfType<MainTabWindow_Inspect>().Any(),"native inspect pane open before result transition");
                    c.State.WantSuccess=true;stage=1;return;
                }
                if(stage==1){
                    var result=Find.WindowStack.Windows.OfType<PersonaMissionResultWindow>().FirstOrDefault();
                    if(result==null)return;
                    Check(c.State.Phase=="Succeeded"&&c.HasNextMission,"first mission result offers next chapter");
                    result.Reveal();Shot("01-first-mission-result");ClickContinue=true;stage=2;return;
                }
                if(stage==2){
                    if(c.NodeId!="mission-two")return;
                    Check(!ClickContinue,"next chapter triggered through result window button");
                    Check(c.Actors["hero"].ThingID==heroId&&c.Completed.Contains("mission-one"),"same campaign and Mason preserved");
                    Check(c.Map!=previousMap&&!previousEffects.Active,"previous map effects detached");
                    Check(MissionEffectsMapComponent.For(c.Map)!=null,"second mission owns active effects component");
                    Check(!c.Objects.ContainsKey("turret-01")&&c.Objects.Values.OfType<Building_TurretGun>().Count()==11,"entrance turret absent and inner defenses preserved");
                    Check(!c.MapSpec.Rules.Single(r=>r.Id==ForgeTrigger).Enabled&&!c.State.Enabled.GetValueOrDefault(ForgeTrigger),"forge trigger initially disabled during entrance battle");
                    stage=3;
                }
                if(stage==3){
                    var view=MissionEffectsMapComponent.CurrentPresentation;
                    if(view?.Briefing==true&&view.Elapsed>19&&!briefChecked){
                        var material=Traverse.Create(view).Field<Material>("terrainMaterial").Value;
                        var captures=Traverse.Create(view).Field<RenderTexture[]>("captures").Value;
                        Check(c.Cinematic&&material!=null&&material.shader.isSupported,"native terrain shader rendered while normal map HUD suppressed");
                        Check(captures.All(t=>t!=null&&t.IsCreated()),"all three in-world briefing windows captured through real GUI draw");
                        Check(Find.CurrentMap==c.Map,"briefing belongs to current mission map");
                        Check(!Find.WindowStack.Windows.OfType<MainTabWindow_Inspect>().Any(),"briefing closes stale inspect window, not just its contents");
                        Shot("02-transition-sand-table");briefChecked=true;
                    }
                    foreach(var id in sentries){
                        var pawn=c.ResolveTriggerPawn(id);
                        if(pawn?.Spawned==true&&!pawn.Dead&&pawn.Position.z<20)outside.Add(id);
                    }
                    if(TestDowned&&briefChecked&&!incapacitated&&sentries.All(id=>c.ResolveTriggerPawn(id)?.pather.Moving==true)){
                        foreach(var id in sentries)c.ResolveTriggerPawn(id).health.AddHediff(DefDatabase<HediffDef>.GetNamed("Anesthetic"));
                        Check(sentries.All(id=>c.ResolveTriggerPawn(id).Downed),"both sentries incapacitated during unfinished pursuit");
                        incapacitated=true;Shot("03-downed-during-pursuit");
                    }
                    if(!fightShot&&outside.Count==2&&Fired.Count==2){Check(!Find.WindowStack.Windows.OfType<MainTabWindow_Inspect>().Any(),"entrance battle has no empty inspect window");Shot("03-sentries-outside-return-fire");fightShot=true;}
                    if(TestInside&&c.State.Firings.GetValueOrDefault(ForgeTrigger)==1&&!insideEnabled){
                        var r=c.MapSpec.Rules.Single(r=>r.Id==ForgeTrigger);var hero=c.Actors["hero"];var stop=c.MapSpec.Objects.Single(o=>o.Id=="m2-intro-end").Position;
                        Check(c.State.Enabled.GetValueOrDefault(ForgeTrigger)&&hero.Position.x==stop.X&&hero.Position.z==stop.Z,"trigger fires at entrance stop without moving Mason again");
                        Check(TriggerGeometry.Contains(r,r.Position.X,r.Position.Z,hero.Position.x,hero.Position.z),"Mason already inside current entrance forge radius when enabled");insideEnabled=true;
                    }
                    if(TestInside&&insideEnabled&&!warpSeen&&c.Objects.Any(p=>p.Key.StartsWith("building-warp:")&&p.Value?.TryGetComp<ProtossMech.CompWarpConstructionBeacon>()!=null)){
                        Check(!Find.WindowStack.Windows.OfType<MainTabWindow_Inspect>().Any(),"forge cinematic closes inspect pane reopened after entrance");warpSeen=true;Shot("05-trigger-building-warp");
                    }
                    if(c.Cinematic||c.State.Variables.GetValueOrDefault("m2-stage")!=1)return;
                    if(TestInside){
                        if(c.Presentation||c.State.Variables.GetValueOrDefault("m2-forge-ready")!=1)return;
                        Check(insideEnabled&&briefChecked,"existing-inside activation follows visible briefing and entrance battle");
                        Check(outside.Count==2&&sentries.All(Fired.Contains),"both entrance guards still pursue and return fire");
                        var forge=c.Objects["m2-forge"];var at=c.MapSpec.Objects.Single(o=>o.Id=="m2-forge").Position;
                        Check(forge.Spawned&&forge.Position.x==at.X&&forge.Position.z==at.Z,"forge uses latest author-moved entrance position");forgeId=forge.ThingID;
                        Check(!c.Actors["hero"].health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("PMP_SceneProtection")),"forge scene releases temporary protection");
                        Check(c.Map.GetComponent<SceneInteractionMapComponent>().Available(c.MapSpec.Interactions.Single(i=>i.Id=="m2-extract")),"forge delivery unlocks extraction tutorial");
                        Check(Find.TickManager.CurTimeSpeed==TimeSpeed.Normal,"previous result window pause is not inherited by new mission");
                        stage=9;waitFrames=0;return;
                    }
                    Check(briefChecked,"visible briefing completed after result transition");
                    if(TestDowned)Check(incapacitated,"opening continues after both pursuit actors are downed");
                    else{
                        Check(outside.Count==2,"both sentries crossed the outer gate toward Mason");
                        Check(sentries.All(Fired.Contains),"both sentries launched real projectiles at Mason");
                    }
                    Check(!c.Objects.ContainsKey("m2-forge")&&c.State.Enabled.GetValueOrDefault(ForgeTrigger),"entrance ends with forge absent and approach trigger enabled");
                    Check(c.Actors["hero"].Spawned&&!c.Actors["hero"].Dead&&!c.Actors["hero"].Downed,"Mason survives opening and control returns");
                    Check(!c.Actors["hero"].health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("PMP_SceneProtection")),"opening protection removed");
                    Check(c.Actors["hero"].drafter.FireAtWill,"ordinary automatic fire restored after opening");
                    stage=4;Shot("04-entry-control");return;
                }
                if(stage==4){
                    var interactions=c.Map.GetComponent<SceneInteractionMapComponent>();
                    Check(!interactions.Available(interactions.FindSpec("m2-extract")),"gunsmith extraction waits until forge exists");
                    var hero=c.Actors["hero"];var intruder=c.ResolveTriggerPawn("guard-01-1");var old=intruder.Position;
                    Teleport(intruder,new IntVec3(140,0,30));Flush(c);
                    Check(!c.State.Firings.ContainsKey(ForgeTrigger),"another pawn entering radius does not trigger forge");Teleport(intruder,old);
                    var origin=hero.Position;var phase=c.State.Variables["m2-stage"];c.State.Variables["m2-stage"]=0;
                    Teleport(hero,new IntVec3(140,0,30));Flush(c);Check(!c.State.Firings.ContainsKey(ForgeTrigger),"entry event cannot bypass battle-complete condition");
                    Teleport(hero,origin);Flush(c);c.State.Variables["m2-stage"]=phase;
                    stage=5;GameDataSaveLoader.SaveGame("Forge-BeforeApproach");GameDataSaveLoader.LoadGame("Forge-BeforeApproach");return;
                }
                if(stage==5){
                    Check(!c.Objects.ContainsKey("m2-forge")&&!c.State.Firings.ContainsKey(ForgeTrigger)&&c.State.Enabled.GetValueOrDefault(ForgeTrigger),"save/load retains enabled but untriggered forge");
                    var hero=c.Actors["hero"];hero.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(140,0,30)),JobCondition.InterruptForced);stage=6;return;
                }
                if(stage==6){
                    if(!warpSeen&&c.Objects.Any(p=>p.Key.StartsWith("building-warp:")&&p.Value?.TryGetComp<ProtossMech.CompWarpConstructionBeacon>()!=null)){
                        Check(c.State.Firings.GetValueOrDefault(ForgeTrigger)==1,"walking into radius starts one trigger");warpSeen=true;Shot("05-trigger-building-warp");
                    }
                    if(c.Presentation||c.State.Variables.GetValueOrDefault("m2-forge-ready")!=1)return;
                    Check(warpSeen&&c.Objects.TryGetValue("m2-forge",out var forge)&&forge.Spawned,"trigger completed building warp and communications");
                    forgeId=c.Objects["m2-forge"].ThingID;
                    var interactions=c.Map.GetComponent<SceneInteractionMapComponent>();Check(interactions.Available(interactions.FindSpec("m2-extract")),"extraction tutorial available after forge delivery");
                    var hero=c.Actors["hero"];Teleport(hero,new IntVec3(140,0,17));Flush(c);Teleport(hero,new IntVec3(140,0,30));Flush(c);
                    Check(c.State.Firings.GetValueOrDefault(ForgeTrigger)==1&&!c.Presentation,"leaving and re-entering does not repeat warp or dialogue");Shot("06-forge-trigger-complete");
                    stage=7;GameDataSaveLoader.SaveGame("Forge-AfterDelivery");GameDataSaveLoader.LoadGame("Forge-AfterDelivery");return;
                }
                if(stage==9){
                    // SelectorOnGUI reopens the native pane after Update; let it render before checking.
                    if(++waitFrames<4)return;
                    Check(warpSeen&&Find.Selector.SingleSelectedThing==c.Actors["hero"]&&Find.WindowStack.Windows.OfType<MainTabWindow_Inspect>().Any(),"normal Mason inspect pane restored after forge cinematic");
                    Shot("06-forge-at-entrance");stage=10;return;
                }
                if(stage==10){
                    // CaptureScreenshot completes at frame end; do not replace the map that same frame.
                    stage=7;GameDataSaveLoader.SaveGame("Forge-InsideOnEnable");GameDataSaveLoader.LoadGame("Forge-InsideOnEnable");return;
                }
                if(stage==7){
                    var hero=c.Actors["hero"];Teleport(hero,new IntVec3(140,0,17));Flush(c);Teleport(hero,new IntVec3(140,0,30));Flush(c);
                    Check(c.State.Firings.GetValueOrDefault(ForgeTrigger)==1&&c.Objects["m2-forge"].ThingID==forgeId&&!c.Presentation,"saved one-shot trigger retains original forge and does not replay");
                    stage=8;return;
                }
                if(stage==8&&++waitFrames>3){
                    var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();File.WriteAllLines(Path.Combine(Folder,"errors.txt"),errors);
                    Check(errors.Length==0,"zero native errors");File.WriteAllText(Path.Combine(Folder,"complete.txt"),checks.Count+" checks passed");finished=true;Application.Quit();
                }
            }catch(Exception ex){finished=true;File.WriteAllText(Path.Combine(Folder,"failure.txt"),ex.ToString());Log.Error("[TransitionOpeningAudit] "+ex);Application.Quit();}
        }
    }
    [HarmonyPatch(typeof(PersonaMissionResultWindow),"Button")]
    static class TransitionResultClick {
        static void Postfix(string text,ref bool __result){if(TransitionOpeningAudit.Enabled&&TransitionOpeningAudit.ClickContinue&&Event.current.type==EventType.Repaint&&text==RcText.T("继续战役")){TransitionOpeningAudit.ClickContinue=false;__result=true;}}
    }
    [HarmonyPatch]
    static class TransitionSentryShots {
        static IEnumerable<MethodBase> TargetMethods()=>typeof(Projectile).GetMethods().Where(m=>m.Name=="Launch"&&m.GetParameters().Length>0&&m.GetParameters()[0].ParameterType==typeof(Thing));
        static void Postfix(Thing __0){
            if(!TransitionOpeningAudit.Enabled)return;
            var c=Current.Game?.GetComponent<CampaignController>();if(!MissionScope.Matches(c,"mission-two"))return;
            foreach(var id in new[]{"sentry-01","m2-entry-guard"})if(c.ResolveTriggerPawn(id)==__0)TransitionOpeningAudit.Fired.Add(id);
        }
    }
}
