using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GunSmithFramework;
using HarmonyLib;
using PMP_Persona.Prometheus;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 [StaticConstructorOnStartup] static class ScattershotDamageBootstrap {
  static ScattershotDamageBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-scattershot-damage-audit"))new Harmony("pmp.scattershot.damage.audit").PatchAll();}
 }
 [HarmonyPatch(typeof(Bullet),"Impact")] static class TracePelletImpact {
  public static Projectile Current;
  public static int Hits,TargetId;
  static void Prefix(Bullet __instance,Thing hitThing){
   Current=(__instance.def.defName=="PM_Bullet_B12Vanguard" || (__instance is Bullet_Scattershot && !(bool)AccessTools.Field(typeof(Bullet_Scattershot),"ricocheted").GetValue(__instance)))?__instance:null;
   if(hitThing?.thingIDNumber!=TargetId)Current=null;
   if(Current!=null)Hits++;
  }
  static void Postfix(){Current=null;}
 }
 [HarmonyPatch(typeof(Thing),nameof(Thing.TakeDamage))] static class TracePelletDamage {
  public static readonly List<string> Lines=new List<string>();
  public static int Calls;public static float Total;
  static void Prefix(Thing __instance,out int __state){__state=__instance.HitPoints;}
  static void Postfix(Thing __instance,DamageInfo dinfo,int __state,DamageWorker.DamageResult __result){
   if(TracePelletImpact.Current==null||dinfo.Def!=TracePelletImpact.Current.def.projectile.damageDef)return;
   Calls++;Total+=__result.totalDamageDealt;
   Lines.Add($"tick={Find.TickManager.TicksGame} pellet={TracePelletImpact.Current.thingIDNumber} target={__instance.Label} raw={dinfo.Amount} dealt={__result.totalDamageDealt} hp={__state}->{__instance.HitPoints}");
  }
 }
 public sealed class ScattershotDamageAudit:GameComponent {
  int frames,caseIndex,started,hp,exitFrames;bool done,waiting;Pawn pawn;ThingWithComps gun;Thing wall;
  bool Vanguard=>GenCommandLine.CommandLineArgPassed("pmp-vanguard-damage-audit");
  string Weapon=>Vanguard?"PM_GunB12Vanguard":"PMP_Scattershot";
  string Pellet=>Vanguard?"PM_Bullet_B12Vanguard":"PMP_ScattershotPellet";
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"ScattershotDamageAudit");
  public ScattershotDamageAudit(Game game){}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-scattershot-damage-audit"))return;
   if(done){if(++exitFrames>60)Application.Quit();return;}
   if(Find.CurrentMap==null||++frames<60)return;
   Directory.CreateDirectory(Folder);
   try {
    var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
    if(pawn==null){
     foreach(var cell in CellRect.CenteredOn(map.Center,20).ClipInsideMap(map)){
      map.roofGrid.SetRoof(cell,null);
      foreach(var thing in cell.GetThingList(map).ToList())if(!(thing is Pawn)){if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);else thing.DeSpawn();}
      map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);map.fogGrid.Unfog(cell);
     }
     pawn=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_Prometheus"),Faction.OfPlayer);GenSpawn.Spawn(pawn,map.Center,map);pawn.equipment.DestroyAllEquipment();pawn.drafter.Drafted=true;pawn.drafter.FireAtWill=false;
     gun=(ThingWithComps)ThingMaker.MakeThing(ThingDef.Named(Weapon));pawn.equipment.AddEquipment(gun);
     if(!Vanguard)gun.GetComp<CompModularGun>().SetPartDefName("Grip",null);gun.GetComp<CompModularGun>().SetPartDefName("Optic",null);
     var method=AccessTools.Method(typeof(Projectile),"CheckForFreeInterceptBetween");var patches=Harmony.GetPatchInfo(method);
     File.WriteAllText(Path.Combine(Folder,"collision-patches.txt"),string.Join("\n",patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Select(p=>p.owner+" "+p.PatchMethod.DeclaringType.FullName+"."+p.PatchMethod.Name)));
     Check(ThingDef.Named(Pellet).projectile.damageDef.scaleDamageToBuildingsBasedOnFlammability,"pellets use "+ThingDef.Named(Pellet).projectile.damageDef.defName+" with building flammability scaling");
    }
    if(waiting){
     if(Find.TickManager.TicksGame-started<(Vanguard?240:110))return;
     string name=(caseIndex<6?(caseIndex%2==0?"direct-1":"direct-8"):(Vanguard?"live-verb-24":"live-verb-8"))+"-"+wall.Stuff.defName;
     File.AppendAllText(Path.Combine(Folder,"results.txt"),$"{name}: impacts={TracePelletImpact.Hits}, damageCalls={TracePelletDamage.Calls}, dealt={TracePelletDamage.Total}, hpLost={hp-wall.HitPoints}, flammability={wall.GetStatValue(StatDefOf.Flammability)}\n");
     File.WriteAllLines(Path.Combine(Folder,name+".txt"),TracePelletDamage.Lines);
     if(caseIndex<6)Check(TracePelletImpact.Hits==(caseIndex%2==0?1:8)&&TracePelletDamage.Calls==TracePelletImpact.Hits,"each aimed pellet has independent impact and damage call: "+name);
     if(caseIndex==1)Check(TracePelletDamage.Total==(Vanguard?56:80),"eight pellets deal full independent projectile damage to wood in the same volley");
     wall.Destroy();waiting=false;caseIndex++;
     if(caseIndex==9){Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no native errors during damage trace");File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Scattershot damage audit completed.");done=true;return;}
    }
    if(!waiting){
     foreach(var p in map.listerThings.ThingsOfDef(ThingDef.Named(Pellet)).ToList())p.Destroy();
     string stuff=new[]{"WoodLog","Steel","BlocksGranite"}[caseIndex<6?caseIndex/2:caseIndex-6];
     wall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDef.Named(stuff));GenSpawn.Spawn(wall,pawn.Position+IntVec3.East*6,map);hp=wall.HitPoints;TracePelletImpact.TargetId=wall.thingIDNumber;
     TracePelletImpact.Hits=0;TracePelletDamage.Calls=0;TracePelletDamage.Total=0;TracePelletDamage.Lines.Clear();
     if(caseIndex<6){
      for(int i=0;i<(caseIndex%2==0?1:8);i++){
       var pellet=(Projectile)GenSpawn.Spawn(ThingDef.Named(Pellet),pawn.Position,map);
       pellet.Launch(pawn,pawn.DrawPos,wall,wall,ProjectileHitFlags.All,false,gun);
      }
     }else Check(gun.GetComp<CompEquippable>().PrimaryVerb.TryStartCastOn(wall),"actual shotgun verb starts on "+stuff);
     started=Find.TickManager.TicksGame;waiting=true;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());done=true;}
  }
 }
}
