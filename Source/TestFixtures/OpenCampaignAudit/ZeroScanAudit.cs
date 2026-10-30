using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using PMP_Persona.GestaltZero;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 [StaticConstructorOnStartup] static class ZeroScanBootstrap {
  static ZeroScanBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-zero-scan-audit"))new Harmony("pmp.zero.scan.audit").PatchAll();}
 }
 [HarmonyPatch(typeof(GenDraw),nameof(GenDraw.DrawMeshNowOrLater),new[]{typeof(Mesh),typeof(Matrix4x4),typeof(Material),typeof(bool),typeof(MaterialPropertyBlock)})]
 static class ZeroScanTrace {
  public static int Phase,Count;
  static void Prefix(Matrix4x4 __1,Material __2,MaterialPropertyBlock __4){
   var matrix=__1;var mat=__2;var propertyBlock=__4;
   if(!GenCommandLine.CommandLineArgPassed("pmp-zero-scan-audit")||mat?.shader?.name!="PM17N_ReconstructPrintCutout"||Count++>=12)return;
   string Field(string key)=>key+"="+propertyBlock.GetFloat(Shader.PropertyToID(key));
   File.AppendAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"ZeroScanAudit","uniforms.txt"),"stage="+Phase+" "+string.Join(" ",new[]{"_PM_ScanV","_PM_AboveAlpha","_PM_BelowAlpha","_PM_ScanIntensity","_PM_ScanSoftness"}.Select(Field))+" matrix="+matrix+"\n");
  }
 }
 public sealed class ZeroScanAudit:GameComponent {
  int frames,stage,age,exit;Pawn pawn;Thing beacon;bool done;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"ZeroScanAudit");
  public ZeroScanAudit(Game game){}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-zero-scan-audit"))return;
   if(done){if(++exit>50)Application.Quit();return;}
   if(Find.CurrentMap==null||++frames<80)return;
   if(Find.TickManager.TicksGame<300){Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;return;}
   Directory.CreateDirectory(Folder);
   try{
    var map=Find.CurrentMap;var zero=Current.Game.GetComponent<GameComponent_GestaltZeroReturn>();
    if(pawn==null){
     foreach(var thing in map.listerThings.AllThings.Where(t=>t.Spawned&&t.Position.DistanceTo(map.Center)<18&&!(t is Pawn)).ToList()){if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);else thing.DeSpawn();}
     foreach(var cell in CellRect.CenteredOn(map.Center,18)){map.roofGrid.SetRoof(cell,null);map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.fogGrid.Unfog(cell);}
     pawn=PawnGenerator.GeneratePawn(PawnKindDef.Named("PMPH_GestaltZero"),Faction.OfPlayer);GenSpawn.Spawn(pawn,map.Center,map);pawn.Kill(null);
     beacon=map.listerThings.ThingsOfDef(ThingDef.Named("PMPH_GestaltZeroBeacon")).First();
     Find.CameraDriver.JumpToCurrentMapLoc(beacon.Position);Find.CameraDriver.SetRootSize(4);
    }
    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
    if(age==0){
     float fraction=new[]{.25f,.5f,.8f,1f}[stage];
     AccessTools.Field(zero.GetType(),"finalSequenceActive").SetValue(zero,true);
     AccessTools.Field(zero.GetType(),"finalSequenceStartTick").SetValue(zero,Find.TickManager.TicksGame-(int)(fraction*GameComponent_GestaltZeroReturn.ReconstructionEpochTicks));
     ZeroScanTrace.Phase=stage;ZeroScanTrace.Count=0;
    }
    if(++age<40)return;
    int token=(int)AccessTools.Field(zero.GetType(),"lifecycleToken").GetValue(zero);
    File.AppendAllText(Path.Combine(Folder,"phases.txt"),"stage="+stage+" actualPhase="+zero.ReconstructionPhase(token)+" age="+pawn.health.hediffSet.GetFirstHediffOfDef(HediffDef.Named("Hediff_Reconstruct"))?.ageTicks+"\n");
    ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"phase-"+stage+".png"));age=0;
    if(++stage==4){File.WriteAllLines(Path.Combine(Folder,"messages.txt"),Log.Messages.Select(m=>m.text));done=true;}
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());done=true;}
  }
 }
}
