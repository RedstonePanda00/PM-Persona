using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimCampaign.Runtime;
using Verse;
using PMP_Persona.VanguardSupport;
using UnityEngine;
using PMP_Persona.Romulus;
namespace PMP.Campaign {
 // Opt-in native profiler; never patches or samples during ordinary play.
 public sealed class MissionThreePerformanceAudit:GameComponent {
  static readonly Dictionary<MethodBase,(long ticks,int calls,long worst)> totals=new();
  static bool sampling;
  bool installed,finished;float began;int firstTick,firstFrame;
  public MissionThreePerformanceAudit(Game game){}
  static void Before(out long __state){__state=sampling?Stopwatch.GetTimestamp():0;}
  static void After(MethodBase __originalMethod,long __state){if(__state==0)return;long elapsed=Stopwatch.GetTimestamp()-__state;totals.TryGetValue(__originalMethod,out var v);totals[__originalMethod]=(v.ticks+elapsed,v.calls+1,Math.Max(v.worst,elapsed));}
  public override void GameComponentUpdate(){
   if(finished||!GenCommandLine.CommandLineArgPassed("pmp-m3-profile"))return;
   var c=Current.Game.GetComponent<CampaignController>();
   if(c?.Map?.GetComponent<OrbitalSupportSession>()?.FireControlReady!=true)return;
   if(!installed){
    var h=new Harmony("pmp.m3.optin-profiler");
    foreach(var pair in new[]{("PMP_Persona.Romulus.CompWallVault","FindPath"),("RimCampaign.Runtime.CampaignController","ResolveTriggerPawn"),("RimCampaign.Runtime.TaskEngage","Execute"),("Verse.TickManager","DoSingleTick"),("RimCampaign.Runtime.CampaignController","GameComponentUpdate"),("PMP_Persona.VanguardSupport.VanguardPresentation","Draw"),("Verse.AI.Pawn_PathFollower","PatherTick")}){
     var t=AccessTools.TypeByName(pair.Item1);var method=t==null?null:AccessTools.Method(t,pair.Item2);if(method!=null)h.Patch(method,new HarmonyMethod(typeof(MissionThreePerformanceAudit),nameof(Before)),new HarmonyMethod(typeof(MissionThreePerformanceAudit),nameof(After)));
    }
    PackMetrics.Reset();PackMetrics.Enabled=true;installed=true;sampling=true;began=Time.realtimeSinceStartup;firstTick=Find.TickManager.TicksGame;firstFrame=Time.frameCount;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;return;
   }
   if(Time.realtimeSinceStartup-began<45)return;
   sampling=false;finished=true;var folder=Path.Combine(GenFilePaths.SaveDataFolderPath,"Performance");Directory.CreateDirectory(folder);
   double elapsedSeconds=Time.realtimeSinceStartup-began;
   File.WriteAllLines(Path.Combine(folder,"profile.txt"),new[]{ $"seconds={elapsedSeconds:F2} TPS={(Find.TickManager.TicksGame-firstTick)/elapsedSeconds:F2} FPS={(Time.frameCount-firstFrame)/elapsedSeconds:F2} pawns={c.Map.mapPawns.AllPawnsSpawned.Count}"}.Concat(totals.OrderByDescending(x=>x.Value.ticks).Select(x=>$"{x.Key.DeclaringType.Name}.{x.Key.Name}: calls={x.Value.calls}, total_ms={x.Value.ticks*1000d/Stopwatch.Frequency:F2}, max_ms={x.Value.worst*1000d/Stopwatch.Frequency:F2}")));
   File.AppendAllText(Path.Combine(folder,"profile.txt"),$"packMode={!PackMetrics.LegacyMode} fullPaths={PackMetrics.FullPaths} expandedNodes={PackMetrics.ExpandedNodes} sharedHits={PackMetrics.SharedHits} localHits={PackMetrics.LocalHits} groupScans={PackMetrics.GroupScans} candidateChecks={PackMetrics.CandidateChecks}\n");PackMetrics.Enabled=false;
   Application.Quit();
  }
 }
}
