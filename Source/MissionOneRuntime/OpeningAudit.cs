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

namespace PMP.Campaign {
    // Explicit isolated native regression. Lets the actual opening play on both fresh start and retry.
    public sealed class OpeningAudit : GameComponent {
        static int runs;
        static bool finished;
        static bool testingSecond;
        static int titleLoadPhase;
        static float savedTitleElapsed;
        bool titleLoading;
        readonly HashSet<string> checks=new HashSet<string>();
        readonly HashSet<string> screenshots=new HashSet<string>();
        bool seen;float started,lastSize,nextTrace;int zoomSamples;bool walked;
        bool masonLine,priceLine,warped;string actorId;
        float completedAt=-1;
        static string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"OpeningAudit");
        public OpeningAudit(Game game){}
        void Check(bool ok,string label){
            if(!ok)throw new Exception(label);
            if(checks.Add(label))File.AppendAllText(Path.Combine(Folder,"results.txt"),"PASS run "+(runs+1)+": "+label+"\n");
        }
        void Shot(string name){if(runs==0&&screenshots.Add(name))ScreenCapture.CaptureScreenshot(Path.Combine(Folder,name+".png"));}
        static IEnumerable<Act> Leaves(Fiber fiber){
            if(fiber.Done)yield break;
            if(fiber.Children.Count==0)yield return fiber.Steps[fiber.Index];
            else foreach(var child in fiber.Children)foreach(var action in Leaves(child))yield return action;
        }
        public override void GameComponentUpdate(){
            if(finished||titleLoading||!GenCommandLine.CommandLineArgPassed("pmp-intro-audit"))return;
            var c=Current.Game.GetComponent<CampaignController>();
            if(c?.Active!=true||c.EntryPending||c.Transitioning||c.Map==null)return;
            Directory.CreateDirectory(Folder);
            try{
                if(testingSecond){CheckSecondTitle(c);return;}
                if(GenCommandLine.CommandLineArgPassed("pmp-title-audit"))CheckTitle(c,"mission-one");
                var actor=c.Actors["hero"];var start=c.MapSpec.Objects.Single(o=>o.Id=="start");
                var end=c.MapSpec.Objects.Single(o=>o.Id=="intro-walk-end");
                if(c.Cinematic){
                    if(!seen){seen=true;started=Time.realtimeSinceStartup;actorId=actor.ThingID;}
                    if(actor.Spawned&&Time.realtimeSinceStartup>=nextTrace){
                        nextTrace=Time.realtimeSinceStartup+5;
                        File.AppendAllText(Path.Combine(Folder,"movement.txt"),$"{Time.realtimeSinceStartup-started:0.0}s pawn={actor.Position} drafted={actor.Drafted} job={actor.CurJob} moving={actor.pather.Moving} destination={actor.pather.Destination} freeze={c.Freeze} speed={Find.TickManager.CurTimeSpeed}\n");
                    }
                    Check(Time.realtimeSinceStartup-started<60,"opening completes within timeout");
                    Check(c.Presentation,"control remains locked throughout opening");
                    var action=c.State.Fibers.SelectMany(Leaves).FirstOrDefault();
                    if(action==null)return;
                    if(action.Kind=="Wait"){
                        Check(!actor.Spawned,"Mason absent from overview");
                        Check(Find.CameraDriver.RootSize>=c.Map.Size.z/2f,"overview includes entire map height");
                        Check(c.Map.AllCells.All(cell=>!cell.Fogged(c.Map)),"overview temporarily reveals map");if(Time.realtimeSinceStartup-started>.5f)Shot("01-overview");
                    }
                    if(action.Kind=="Camera"&&action.Target=="start"){
                        float size=Find.CameraDriver.RootSize;
                        if(lastSize>0&&size<lastSize-.001f)zoomSamples++;
                        lastSize=size;Check(!actor.Spawned,"Mason absent during camera approach");
                        if(size>25&&size<45)Shot("02-camera-approach");
                    }
                    if(action.Kind=="rc.pmcWarpActor"){
                        var beacon=c.Objects.Values.FirstOrDefault(t=>t!=null&&!t.Destroyed&&t.def.defName=="RC_PMCActorBeacon");
                        if(beacon!=null){
                            int ticks=Traverse.Create(((ThingWithComps)beacon).AllComps.First(comp=>comp.GetType().Name=="CompWarpIn")).Field("ticksToWarp").GetValue<int>();
                            Check(!actor.Spawned,"Mason remains absent while warp countdown is active");
                            Check(Find.CameraDriver.RootSize<19,"warp shown at close camera distance");warped=true;
                            if(ticks<130&&ticks>45)Shot("03-warp");
                        }
                    }
                    if(action.Kind=="Move"){
                        Check(warped&&actor.Spawned&&actor.ThingID==actorId,"warp places existing authored Mason after effect");
                        Check(actor.Drafted&&!actor.drafter.FireAtWill,"delayed arrival is drafted with automatic fire disabled");
                        Find.Selector.Select(actor);Check(Find.Selector.SingleSelectedThing!=actor,"player cannot select Mason during opening");
                        Check(Traverse.Create(Find.CameraDriver).Property("AnythingPreventsCameraMotion").GetValue<bool>(),"camera input remains locked during opening");
                        if(actor.Position.z>start.Position.Z&&actor.Position.z<end.Position.Z){walked=true;Shot("04-walk");}
                    }
                    var message=c.Communications.Current;
                    if(message!=null){
                        Check(actor.Spawned&&actor.Position.z==end.Position.Z,"dialogue begins only after forward walk completes");
                        if(message.Spec.Speaker=="hero"){masonLine=true;Check(!priceLine,"Mason speaks before Price");Shot("05-mason");}
                        else if(message.Spec.SpeakerName=="普莱斯"){priceLine=true;Check(masonLine,"Price responds after Mason");Shot("06-price");}
                    }
                    return;
                }
                if(!seen)return;
                Check(zoomSamples>8,"camera zoom progresses across multiple frames");
                Check(walked&&masonLine&&priceLine,"all opening beats played in order");
                Check(!c.Presentation&&!c.RevealingMap,"control and normal visibility restored");
                Check(Find.CameraDriver.RootSize<19&&Find.CameraDriver.MapPosition.z<20,"handoff keeps camera near Mason");
                Check(actor.equipment.Primary?.def.defName=="PM_GunC7Templar","Mason retains default C7");
                Check(c.Map.mapPawns.AllPawnsSpawned.Count(p=>p.ThingID==actorId)==1,"no duplicate Mason generated");
                Find.Selector.Select(actor);Check(Find.Selector.SingleSelectedThing==actor,"Mason selectable after Price finishes");
                if(completedAt<0){completedAt=Time.realtimeSinceStartup;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;return;}
                if(Time.realtimeSinceStartup-completedAt<.8f)return;
                var stealth=StealthMapComponent.For(c.Map);var archive=c.Objects["A03-host"].Position;
                Check(!stealth.WasExplored(archive)&&archive.Fogged(c.Map),"overview does not grant permanent room discovery");Shot("07-control-restored");
                if(runs==0){runs++;c.Retry();return;}
                if(GenCommandLine.CommandLineArgPassed("pmp-title-audit")){
                    Check(c.OpeningTitleAlpha==0,"first title fades out completely");testingSecond=true;c.Transition("mission-two");return;
                }
                Finish();
            }catch(Exception ex){File.AppendAllText(Path.Combine(Folder,"results.txt"),"FAIL "+ex);File.WriteAllText(Path.Combine(Folder,"complete.txt"),"FAIL");finished=true;Application.Quit();}
        }
        void Finish(){
                var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();File.WriteAllLines(Path.Combine(Folder,"errors.txt"),errors);
                Check(errors.Length==0,"zero native errors on fresh start and retry");File.WriteAllText(Path.Combine(Folder,"complete.txt"),"PASS");finished=true;Application.Quit();
        }
        void CheckTitle(CampaignController c,string node){
            Check(c.OpeningTitleText==RcText.T(c.Node.OpeningTitle.Title),node+" uses localized authored title");
            Check(c.OpeningTitleLines.Count==4&&c.OpeningTitleLines.All(s=>!s.Contains("{")),node+" four lines resolve date time weather and location");
            Check(c.OpeningTitleLines[2].Contains(c.Map.weatherManager.curWeather.LabelCap.ToString()),node+" entry weather is actual map weather");
            float elapsed=c.OpeningTitleElapsed;
            if(elapsed>.15f&&elapsed<.75f)Check(c.OpeningTitleAlpha>0&&c.OpeningTitleAlpha<1,node+" fades in");
            if(elapsed>1.2f&&elapsed<6){
                Check(c.OpeningTitleAlpha==1,node+" holds at full opacity");
                string shot=node+"-opening-title";
                if((node!="mission-two"||titleLoadPhase==0)&&screenshots.Add(shot))ScreenCapture.CaptureScreenshot(Path.Combine(Folder,shot+".png"));
            }
            if(elapsed>7.2f&&elapsed<8.3f)Check(c.OpeningTitleAlpha>0&&c.OpeningTitleAlpha<1,node+" fades out");
        }
        void CheckSecondTitle(CampaignController c){
            Check(c.NodeId=="mission-two","title follows transition inside same campaign");CheckTitle(c,"mission-two");
            Check(!c.Cinematic&&!c.Freeze,"second title does not lock playable opening");
            if(titleLoadPhase==0&&c.OpeningTitleElapsed>3){
                savedTitleElapsed=c.OpeningTitleElapsed;titleLoadPhase=1;
                GameDataSaveLoader.SaveGame("OpeningTitleRoundtrip");
                // The ordinary second opening is non-blocking, so saving must not be deferred.
                Check(File.Exists(GenFilePaths.FilePathForSavedGame("OpeningTitleRoundtrip")),"active title allows immediate save");
                titleLoading=true;GameDataSaveLoader.LoadGame("OpeningTitleRoundtrip");return;
            }
            if(titleLoadPhase==1){
                Check(c.OpeningTitleElapsed>=savedTitleElapsed&&c.OpeningTitleElapsed<savedTitleElapsed+.5f,"same-version load resumes title elapsed time");titleLoadPhase=2;
            }
            if(titleLoadPhase==2&&c.OpeningTitleElapsed>=8.5f){Check(c.OpeningTitleAlpha==0,"second title fades out completely");Finish();}
        }
    }
}
