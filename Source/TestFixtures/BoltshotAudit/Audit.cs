using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using GunSmithFramework;
using ProtossMech;
using ProtossMech.GunSmith;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Tests {
 [StaticConstructorOnStartup] public static class BoltshotAuditBootstrap {
  static BoltshotAuditBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-boltshot-audit"))new Harmony("pmp.boltshot.audit").PatchAll();}
 }
 [HarmonyPatch] static class CaptureBoltshot {
  public static readonly List<(int tick,Vector3 origin,string projectile)> Shots=new List<(int,Vector3,string)>();
  static MethodBase TargetMethod()=>AccessTools.Method(typeof(Projectile),"Launch",new[]{typeof(Thing),typeof(Vector3),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(ProjectileHitFlags),typeof(bool),typeof(Thing),typeof(ThingDef)});
  static void Postfix(Projectile __instance,Vector3 origin,Thing equipment){if(equipment?.def.defName=="PMP_Boltshot")Shots.Add((Find.TickManager.TicksGame,origin,__instance.def.defName));}
 }
 public sealed class BoltshotAudit : GameComponent {
  int stage,frames,exitFrames,pawnId,gunId,started,caseIndex;bool done,loading,shooting;float realStart;
  Pawn pawn;ThingWithComps gun;Thing target;CompModularGun comp;Verb verb;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"BoltshotAudit");
  public BoltshotAudit(Game game){}
  public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref stage,"boltshotStage");Scribe_Values.Look(ref pawnId,"boltshotPawn");Scribe_Values.Look(ref gunId,"boltshotGun");}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  void Bake(string name){var t=GunSmithTextureBaker.BakeComposite(comp.Platform,comp.GetPart,gun.def.graphicData.drawSize,out var size);Check(t!=null&&size.x>0,"GPU bake "+name);File.WriteAllBytes(Path.Combine(Folder,name+".png"),t.EncodeToPNG());}
  void Configure(bool charge,bool dual){comp.SetPartDefName("Ammo",charge?"PMP_Boltshot_Charged":"PMP_Boltshot_Single");comp.SetPartDefName("Grip",dual?"PMP_Boltshot_GripDual":null);}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-boltshot-audit"))return;
   if(done){if(++exitFrames>60)Application.Quit();return;}
   if(loading||Find.CurrentMap==null||++frames<90)return;
   Directory.CreateDirectory(Folder);if(realStart==0)realStart=Time.realtimeSinceStartup;
   try {
    CheckTimeout();var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
    if(stage==0){
     var def=ThingDef.Named("PMP_Boltshot");Check(!def.ConfigErrors().Any(),"weapon Def passes native validation");
     var center=map.Center;
     foreach(var cell in CellRect.CenteredOn(center,18).ClipInsideMap(map)){
      foreach(var thing in cell.GetThingList(map).ToList())if(!(thing is Pawn)){if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);else thing.DeSpawn();}
      map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);map.fogGrid.Unfog(cell);
     }
     pawn=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_Prometheus"),Faction.OfPlayer);GenSpawn.Spawn(pawn,center,map);pawnId=pawn.thingIDNumber;
     pawn.equipment.DestroyAllEquipment();gun=(ThingWithComps)ThingMaker.MakeThing(def);pawn.equipment.AddEquipment(gun);gunId=gun.thingIDNumber;pawn.drafter.Drafted=true;
     comp=gun.GetComp<CompModularGun>();verb=gun.GetComp<CompEquippable>().PrimaryVerb;
     target=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDef.Named("BlocksGranite"));GenSpawn.Spawn(target,center+IntVec3.East*6,map);
     Check(!comp.Platform.ConfigErrors().Any(),"platform slot graph is valid");
     Check(GunSmithPartQuery.GetVisibleSlotIds(comp).SequenceEqual(new[]{"Ammo","Grip","Optic"}),"ammo, grip and optic are the three visible selectors");
     Check(comp.GetPart("Ammo").defName=="PMP_Boltshot_Single"&&comp.GetPart("Grip")==null&&comp.GetPart("Optic")==null&&comp.GetPart("OpticArmor")==null,"defaults are single-shot ammo and no grip, optic or armor");
     Check(GunSmithPlatformService.EnumeratePlatforms().Contains(comp.Platform)&&GunSmithPlatformService.IsPlatformAvailable(comp.Platform),"pistol is available through native gunsmith menu");
     Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,comp).Single().count==20,"base warp price is 20 solarite");Bake("bare");
     foreach(string optic in new[]{"PMP_HardlightRifle_Optic","PM_Common_MarksmanOptic","PM_Common_ViperSight"}){
      Check(GunSmithPartQuery.GetPartsForPlatform(comp.Platform,"Optic").Any(p=>p.defName==optic),"compatible optic "+optic);
      comp.SetPartDefName("Optic",optic);Check(comp.GetPart("OpticArmor")?.defName=="PMP_Boltshot_OpticArmor","armor appears with "+optic);Bake(optic);
      comp.SetPartDefName("Optic",null);Check(comp.GetPart("OpticArmor")==null,"optic removal removes armor");
     }
     Check(comp.IsOptionalSlot("Grip") && GunSmithPartQuery.GetPartsForPlatform(comp.Platform,"Grip").Count()==1,"grip offers only empty or dual-wield");
     Check(DefDatabase<GunSmithPartDef>.GetNamedSilentFail("PMP_Boltshot_GripSingle")==null && DefDatabase<GunSmithPartDef>.GetNamedSilentFail("PMP_Boltshot_Optic")==null && DefDatabase<GunSmithSlotDef>.GetNamedSilentFail("PMP_BoltshotFireMode")==null,"redundant grip, optic and custom ammo slot removed");
     Check(DefDatabase<GunSmithPartDef>.GetNamed("PMP_Boltshot_GripDual").iconPath=="Things/Weapon/Boltshot/Boltshot_DualGrip_Icon","dual grip uses crossed-pistol icon");
     CheckRifleArt();
     var meta=ModLister.GetActiveModWithIdentifier("redstone.pmpersona");Check(meta.ModIconPath=="UI/Campaign/RomulusInsignia"&&meta.Icon==ContentFinder<Texture2D>.Get("UI/Campaign/RomulusInsignia"),"native mod icon loads Romulus faction insignia");
     Find.Selector.ClearSelection();Find.Selector.Select(pawn);Find.CameraDriver.SetRootPosAndSize(pawn.DrawPos,12);stage=1;
    }
    if(stage==1){
     if(!shooting){
      if(pawn.stances.curStance is Stance_Busy)return;
      bool charge=caseIndex%2==1,dual=caseIndex>=2;Configure(charge,dual);
      Check(Math.Abs(verb.EffectiveRange-(charge?14f:24f))<.01f&&Math.Abs(verb.WarmupTime-(charge?1.2f:.25f))<.01f,"firing circuit controls range and warmup case "+caseIndex);
      Check(!charge||!verb.CanHitTarget(pawn.Position+IntVec3.North*16),"charged circuit rejects distant target");
      CaptureBoltshot.Shots.Clear();started=Find.TickManager.TicksGame;
      Check(verb.TryStartCastOn(target),"native cast starts case "+caseIndex);shooting=true;return;
     }
     if(CaptureBoltshot.Shots.Count==0)return;
     var shots=CaptureBoltshot.Shots;int expected=new[]{1,5,2,10}[caseIndex];
     Check(shots.Count==expected&&shots.Select(s=>s.tick).Distinct().Count()==1,"simultaneous projectile count "+expected);
     Check(shots.All(s=>s.projectile==(caseIndex%2==1?"PMP_BoltshotPellet":"PMP_BoltshotBolt")),"circuit selects correct ammunition");
     if(caseIndex%2==1)Check(shots[0].tick-started>=50,"charged shot really waits for warmup");
     if(caseIndex>=2)Check(shots.Select(s=>s.origin).Distinct().Count()==2,"dual grip launches from two separate muzzles");
     Check(((VerbProperties_Shotgun)gun.def.Verbs[0]).pelletCount==1,"shared Def is unchanged by this gun's mode");
     ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"firing-"+caseIndex+".png"));shooting=false;
     if(++caseIndex==4){started=Find.TickManager.TicksGame;stage=2;}return;
    }
    if(stage==2){
     if(pawn.stances.curStance is Stance_Busy)return;
     Check(target.HitPoints<target.MaxHitPoints,"actual projectiles damage the target");Configure(true,false);CaptureBoltshot.Shots.Clear();
     Check(verb.TryStartCastOn(target),"interruptible charged shot starts");started=Find.TickManager.TicksGame;stage=3;return;
    }
    if(stage==3){
     if(Find.TickManager.TicksGame-started<10)return;
     Check(pawn.stances.curStance is Stance_Warmup,"charge enters native warmup stance");
     pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto,pawn.Position+IntVec3.North*3));stage=4;return;
    }
    if(stage==4){
     if(Find.TickManager.TicksGame-started<100)return;
     Check(CaptureBoltshot.Shots.Count==0,"moving during charge cancels without firing");pawn.jobs.StopAll();Configure(true,true);comp.SetPartDefName("Optic","PMP_HardlightRifle_Optic");
     Check(Math.Abs(gun.GetStatValue(StatDefOf.Mass)-2.6f)<.01f,"dual pistols and optic add their proper mass");
     stage=5;loading=true;GameDataSaveLoader.SaveGame("BoltshotAudit");LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("BoltshotAudit"),"LoadingLongEvent",false,null);return;
    }
    if(stage==5){
     pawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==pawnId);gun=pawn.equipment.Primary;comp=gun.GetComp<CompModularGun>();
     Check(gun.thingIDNumber==gunId&&comp.GetPart("Ammo").defName=="PMP_Boltshot_Charged"&&comp.EnablesDualWield()&&comp.GetPart("OpticArmor")!=null,"save/load preserves weapon identity, mode, dual grip, optic and conditional armor");
     var platform=comp.Platform;var loadout=new GunSmithLoadout(comp);var costs=GunSmithCostUtility.BuildWarpCosts(platform,loadout);Check(costs.Single().count==55,"charged dual pair with sight costs 55 solarite");
     var pylon=ThingMaker.MakeThing(ThingDef.Named("Pylon"));pylon.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(pylon,pawn.Position+IntVec3.South*5,map);
     var cabinet=ThingMaker.MakeThing(ThingDef.Named("PM_HyperspaceStorage"));cabinet.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(cabinet,pawn.Position+IntVec3.West*5,map);
     AccessTools.Method(typeof(CompHyperspaceStorage),"DepositInternal").Invoke(cabinet.TryGetComp<CompHyperspaceStorage>(),new object[]{ThingDef.Named("PM_Solarite"),55L});
     Check(GunSmithOperations.SpawnWeaponAt(platform,loadout,pawn.Position+IntVec3.South*2),"native gunsmith weapon warp accepted");
     Check(cabinet.TryGetComp<CompHyperspaceStorage>().StoredCount==0,"native warp deducts all 55 solarite");stage=6;return;
    }
    if(stage==6){
     var delivered=map.listerThings.ThingsOfDef(ThingDef.Named("PMP_Boltshot"));if(delivered.Count==0)return;
     Check(delivered.Count==1&&delivered[0].TryGetComp<CompModularGun>().EnablesDualWield()&&delivered[0].TryGetComp<CompModularGun>().GetPart("Ammo").defName=="PMP_Boltshot_Charged","native warp creates exactly one correctly configured dual-pistol item");
     Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no game errors through creation, baking, combat, save/load and paid warp");
     File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Boltshot native audit passed.");done=true;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());done=true;}
  }
  void CheckRifleArt(){
   foreach(var path in new[]{"Boltshot/Boltshot","HardlightRifle/Hardlight_UnderbarrelMount"})
    Check(ContentFinder<Texture2D>.Get("Things/Weapon/"+path).width==512,"512 pixel layer "+path);
   foreach(var path in new[]{"HardlightRifle/Hardlight_Body","HardlightRifle/Hardlight_UnderbarrelMount_Rifle"})
    Check(ContentFinder<Texture2D>.Get("Things/Weapon/"+path).width==1024,"1024 pixel rifle layer "+path);
   var rifle=(ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("PMP_HardlightRifle"));var rc=rifle.GetComp<CompModularGun>();
   Check(rifle.def.graphicData.texPath==rc.Platform.bodyTexPath,"rifle uses body texture and native composition, no preview texture");
   Check(rc.GetPart("Optic").defName=="PMP_HardlightRifle_Optic","rifle keeps existing shared sight by default");
   foreach(var slot in new[]{"Optic","UnderBarrel"}){
    foreach(var part in GunSmithPartQuery.GetPartsForPlatform(rc.Platform,slot)){
     rc.SetPartDefName(slot,part.defName);
     Check(slot!="UnderBarrel"||rc.GetPart("UnderBarrelMount")!=null,"rifle mount appears with "+part.defName);
     var tex=GunSmithTextureBaker.BakeComposite(rc.Platform,rc.GetPart,rifle.def.graphicData.drawSize,out var size);
     Check(tex!=null&&size.x>0,"rifle GPU bake "+part.defName);File.WriteAllBytes(Path.Combine(Folder,"rifle-"+part.defName+".png"),tex.EncodeToPNG());
     rc.SetPartDefName(slot,null);
     Check(slot!="UnderBarrel"||rc.GetPart("UnderBarrelMount")==null,"rifle mount removed with "+part.defName);
    }
   }
   rifle.Destroy(DestroyMode.Vanish);
  }
  void CheckTimeout(){if(Time.realtimeSinceStartup-realStart>180)throw new Exception("Timeout stage="+stage+" case="+caseIndex);}
 }
}
