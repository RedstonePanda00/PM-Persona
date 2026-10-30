using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GunSmithFramework;
using HarmonyLib;
using ProtossMech;
using ProtossMech.GunSmith;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 [StaticConstructorOnStartup] public static class SuppressorAuditBootstrap {
  static SuppressorAuditBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-suppressor-audit"))new Harmony("pmp.suppressor.audit").PatchAll();}
 }
 [HarmonyPatch] static class CaptureSuppressor {
  public static readonly List<int> Shots=new List<int>();
  public static readonly List<Vector3> Origins=new List<Vector3>();
  static MethodBase TargetMethod()=>AccessTools.Method(typeof(Projectile),"Launch",new[]{typeof(Thing),typeof(Vector3),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(ProjectileHitFlags),typeof(bool),typeof(Thing),typeof(ThingDef)});
  static void Postfix(Thing equipment,Vector3 origin){if(equipment?.def.defName=="PMP_Suppressor"){Shots.Add(Find.TickManager.TicksGame);Origins.Add(origin);}}
 }
 public sealed class SuppressorAudit : GameComponent {
  int stage,frames,exitFrames,pawnId,gunId,started;bool done,loading;float realStart;
  Pawn pawn;ThingWithComps gun;Thing target;CompModularGun comp;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"SuppressorAudit");
  public SuppressorAudit(Game game){}
  public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref stage,"suppressorStage");Scribe_Values.Look(ref pawnId,"suppressorPawn");Scribe_Values.Look(ref gunId,"suppressorGun");}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  void Bake(string name){var t=GunSmithTextureBaker.BakeComposite(comp.Platform,comp.GetPart,gun.def.graphicData.drawSize,out var size);Check(t!=null&&size.x>0,"GPU bake "+name);File.WriteAllBytes(Path.Combine(Folder,name+".png"),t.EncodeToPNG());}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-suppressor-audit"))return;
   if(done){if(++exitFrames>60)Application.Quit();return;}
   if(loading||Find.CurrentMap==null||++frames<90)return;
   Directory.CreateDirectory(Folder);if(realStart==0)realStart=Time.realtimeSinceStartup;
   try {
    if(Time.realtimeSinceStartup-realStart>150)throw new Exception("Timeout stage="+stage);
    var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
    if(stage==0){
     var def=ThingDef.Named("PMP_Suppressor");Check(!def.ConfigErrors().Any(),"native weapon Def validation");
     var center=map.Center;
     foreach(var cell in CellRect.CenteredOn(center,12).ClipInsideMap(map)){
      foreach(var thing in cell.GetThingList(map).ToList())if(!(thing is Pawn)){if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);else thing.DeSpawn();}
      map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);map.fogGrid.Unfog(cell);
     }
     pawn=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_Prometheus"),Faction.OfPlayer);GenSpawn.Spawn(pawn,center,map);pawnId=pawn.thingIDNumber;
     pawn.equipment.DestroyAllEquipment();gun=(ThingWithComps)ThingMaker.MakeThing(def);pawn.equipment.AddEquipment(gun);gunId=gun.thingIDNumber;pawn.drafter.Drafted=true;
     comp=gun.GetComp<CompModularGun>();var verb=gun.GetComp<CompEquippable>().PrimaryVerb;
     Check(!comp.Platform.ConfigErrors().Any(),"platform configuration valid");
     Check(GunSmithPartQuery.GetVisibleSlotIds(comp).SequenceEqual(new[]{"Grip","Optic"})&&comp.IsOptionalSlot("Grip")&&comp.IsOptionalSlot("Optic")&&comp.GetPart("Optic")==null&&comp.GetPart("Grip")==null,"optional grip and optic slots initially empty");
     Check(GunSmithPlatformService.EnumeratePlatforms().Contains(comp.Platform)&&GunSmithPlatformService.IsPlatformAvailable(comp.Platform),"available in native gunsmith menu");
     Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,comp).Single().count==40,"bare weapon costs 40 solarite");
     Check(verb.GetType()==typeof(Verb_Shoot)&&def.Verbs[0].defaultProjectile.thingClass==typeof(Bullet),"native shooting and unguided bullet classes");
     Check(verb.EffectiveRange==22&&!verb.CanHitTarget(center+IntVec3.North*25),"22 cell base range excludes distant target");
     Check(gun.GetStatValue(StatDefOf.AccuracyShort)>gun.GetStatValue(StatDefOf.AccuracyMedium),"accuracy falls at medium range");
     var projectile=def.Verbs[0].defaultProjectile.projectile;
     Check(projectile.GetDamageAmount(gun)==7&&Math.Abs(projectile.GetArmorPenetration(gun)-2f)<.001f&&projectile.damageDef==DamageDefOf.Burn,"7 heat damage and 200 percent penetration");
     foreach(var bulletName in new[]{"PMP_BoltshotBolt","PMP_BoltshotPellet","PMP_SuppressorBolt","PMP_HardlightBolt"}){
      var props=ThingDef.Named(bulletName).projectile;
      Check(props.damageDef.armorCategory.defName=="Heat"&&Math.Abs(props.GetArmorPenetration(gun)-2f)<.001f,"200 percent heat penetration: "+bulletName);
     }
     Check(GunSmithPartQuery.GetPartsForPlatform(comp.Platform,"Grip").Single().defName=="PMP_Suppressor_GripDual","only dual grip offered, no standard placeholder");
     comp.SetPartDefName("Grip","PMP_Suppressor_GripDual");Check(comp.EnablesDualWield()&&Math.Abs(gun.GetStatValue(StatDefOf.Mass)-4.8f)<.01f,"dual grip adds one full gun mass");
     comp.SetPartDefName("Grip",null);Check(!comp.EnablesDualWield()&&Math.Abs(gun.GetStatValue(StatDefOf.Mass)-2.4f)<.01f&&GunSmithCostUtility.BuildWarpCosts(comp.Platform,comp).Single().count==40,"removing dual grip restores single gun mass and cost");
     var texture=ContentFinder<Texture2D>.Get(comp.Platform.bodyTexPath);Check(texture.width==512&&texture.height==256,"512x256 source texture");Bake("bare");
     foreach(var optic in new[]{"PM_Common_ViperSight"}){
      Check(GunSmithPartQuery.GetPartsForPlatform(comp.Platform,"Optic").Any(p=>p.defName==optic),"shared optic available "+optic);comp.SetPartDefName("Optic",optic);Bake(optic);comp.SetPartDefName("Optic",null);
      Check(comp.GetPart("Optic")==null&&verb.EffectiveRange==22,"optic removal restores bare range");
     }
     target=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDef.Named("BlocksGranite"));GenSpawn.Spawn(target,center+IntVec3.East*6,map);
     CaptureSuppressor.Shots.Clear();CaptureSuppressor.Origins.Clear();Check(verb.TryStartCastOn(target),"native burst cast starts");started=Find.TickManager.TicksGame;stage=1;return;
    }
    if(stage==1){
     if(Find.TickManager.TicksGame-started<120)return;
     bool dual=comp.EnablesDualWield();
     Check(CaptureSuppressor.Shots.Count==(dual?18:9),dual?"dual burst launches eighteen projectiles":"single burst launches nine projectiles");
     var times=CaptureSuppressor.Shots.Distinct().ToArray();
     Check(times.Length==9&&CaptureSuppressor.Shots.GroupBy(t=>t).All(g=>g.Count()==(dual?2:1)),"nine launch times with correct simultaneous projectile count");
     if(dual)Check(CaptureSuppressor.Origins.Take(2).Distinct().Count()==2,"dual launch uses separate muzzles");
     var intervals=times.Zip(times.Skip(1),(a,b)=>b-a).ToArray();
     File.WriteAllText(Path.Combine(Folder,"shot-ticks.txt"),string.Join(",",CaptureSuppressor.Shots));
     // WarmupComplete can launch immediately before the same tick's VerbTick.
     Check((intervals[0]==3||intervals[0]==4)&&intervals.Skip(1).All(d=>d==4),"four tick sustained cadence; native warmup boundary: "+string.Join(",",intervals));
     Check(target.HitPoints<target.MaxHitPoints,"actual bullets damage target");
     if(!dual){
      comp.SetPartDefName("Grip","PMP_Suppressor_GripDual");CaptureSuppressor.Shots.Clear();CaptureSuppressor.Origins.Clear();
      Check(gun.GetComp<CompEquippable>().PrimaryVerb.TryStartCastOn(target),"dual burst starts");started=Find.TickManager.TicksGame;return;
     }
     comp.SetPartDefName("Optic","PM_Common_ViperSight");Check(gun.GetComp<CompEquippable>().PrimaryVerb.EffectiveRange==22&&Math.Abs(gun.GetStatValue(StatDefOf.Mass)-4.85f)<.01f,"dual guns and shared sight give range 22 and mass 4.85");
     Find.Selector.ClearSelection();Find.Selector.Select(pawn);Find.CameraDriver.SetRootPosAndSize(pawn.DrawPos,12);ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"equipped.png"));
     stage=2;loading=true;GameDataSaveLoader.SaveGame("SuppressorAudit");LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("SuppressorAudit"),"LoadingLongEvent",false,null);return;
    }
    if(stage==2){
     pawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==pawnId);gun=pawn.equipment.Primary;comp=gun.GetComp<CompModularGun>();
     Check(comp.EnablesDualWield()&&gun.thingIDNumber==gunId&&comp.GetPart("Optic").defName=="PM_Common_ViperSight"&&gun.GetComp<CompEquippable>().PrimaryVerb.EffectiveRange==22,"save/load preserves dual grip, weapon and optic stats");
     var pylon=ThingMaker.MakeThing(ThingDef.Named("Pylon"));pylon.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(pylon,pawn.Position+IntVec3.South*5,map);
     var cabinet=ThingMaker.MakeThing(ThingDef.Named("PM_HyperspaceStorage"));cabinet.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(cabinet,pawn.Position+IntVec3.West*5,map);
     AccessTools.Method(typeof(CompHyperspaceStorage),"DepositInternal").Invoke(cabinet.TryGetComp<CompHyperspaceStorage>(),new object[]{ThingDef.Named("PM_Solarite"),110L});
     var loadout=new GunSmithLoadout(comp);Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,loadout).Single().count==110,"shared-sight configuration costs 110 solarite");
     Check(GunSmithOperations.SpawnWeaponAt(comp.Platform,loadout,pawn.Position+IntVec3.South*2),"native gunsmith warp accepted");Check(cabinet.TryGetComp<CompHyperspaceStorage>().StoredCount==0,"warp deducts 110 solarite");stage=3;return;
    }
    if(stage==3){
     var delivered=map.listerThings.ThingsOfDef(ThingDef.Named("PMP_Suppressor"));if(delivered.Count==0)return;
     Check(delivered.Count==1&&delivered[0].TryGetComp<CompModularGun>().EnablesDualWield()&&delivered[0].TryGetComp<CompModularGun>().GetPart("Optic").defName=="PM_Common_ViperSight","one correctly configured weapon delivered");
     Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no game errors through rendering, firing, save/load and paid warp");File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Suppressor native audit passed.");done=true;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());done=true;}
  }
 }
}
