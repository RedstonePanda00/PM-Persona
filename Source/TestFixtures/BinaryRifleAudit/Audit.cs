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
 [StaticConstructorOnStartup] public static class BinaryRifleAuditBootstrap {
  static BinaryRifleAuditBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-binary-rifle-audit"))new Harmony("pmp.binary.rifle.audit").PatchAll();}
 }
 [HarmonyPatch] static class CaptureBinaryBeam {
  public static readonly List<int> Shots=new List<int>();
  public static bool Record=true;
  static MethodBase TargetMethod()=>AccessTools.Method(typeof(Projectile),"Launch",new[]{typeof(Thing),typeof(Vector3),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(ProjectileHitFlags),typeof(bool),typeof(Thing),typeof(ThingDef)});
  static void Postfix(Thing equipment){if(Record&&equipment?.def.defName=="PMP_BinaryRifle")Shots.Add(Find.TickManager.TicksGame);}
 }
 [HarmonyPatch(typeof(Mote),nameof(Mote.SpawnSetup))] static class CaptureBinaryMotes {
  public static readonly List<Mote> Motes=new List<Mote>();
  static void Postfix(Mote __instance){if(__instance.def.defName.StartsWith("PMP_Mote_Binary"))Motes.Add(__instance);}
  public static bool Alive(string name)=>Motes.Any(m=>m.def.defName==name&&m.Spawned&&!m.Destroyed);
 }
 public sealed class BinaryRifleAudit : GameComponent {
  int stage,frames,exitFrames,pawnId,gunId,started,hp;bool done,loading;float realStart;
  Pawn pawn;ThingWithComps gun;Thing target;CompModularGun comp;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"BinaryRifleAudit");
  public BinaryRifleAudit(Game game){}
  public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref stage,"binaryStage");Scribe_Values.Look(ref pawnId,"binaryPawn");Scribe_Values.Look(ref gunId,"binaryGun");}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  void Bake(string name){var t=GunSmithTextureBaker.BakeComposite(comp.Platform,comp.GetPart,gun.def.graphicData.drawSize,out var size);Check(t!=null&&size.x>0,"GPU bake "+name);File.WriteAllBytes(Path.Combine(Folder,name+".png"),t.EncodeToPNG());}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-binary-rifle-audit"))return;
   if(done){if(++exitFrames>60)Application.Quit();return;}
   if(loading||Find.CurrentMap==null||++frames<90)return;
   Directory.CreateDirectory(Folder);if(realStart==0)realStart=Time.realtimeSinceStartup;
   try {
    if(Time.realtimeSinceStartup-realStart>150)throw new Exception("Timeout stage="+stage);
    var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
    if(stage==0){
     var def=ThingDef.Named("PMP_BinaryRifle");Check(!def.ConfigErrors().Any(),"native weapon Def validation");
     foreach(var name in new[]{"PMP_BinaryBeam","PMP_Mote_BinaryAim","PMP_Mote_BinaryShot"})Check(!ThingDef.Named(name).ConfigErrors().Any(),"Def validation "+name);
     var center=map.Center;
     foreach(var cell in CellRect.CenteredOn(center,65).ClipInsideMap(map)){
      map.roofGrid.SetRoof(cell,null);
      foreach(var thing in cell.GetThingList(map).ToList())if(!(thing is Pawn)){if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);else thing.DeSpawn();}
      map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);map.fogGrid.Unfog(cell);
     }
     pawn=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_Prometheus"),Faction.OfPlayer);GenSpawn.Spawn(pawn,center,map);pawnId=pawn.thingIDNumber;
     pawn.equipment.DestroyAllEquipment();gun=(ThingWithComps)ThingMaker.MakeThing(def);pawn.equipment.AddEquipment(gun);gunId=gun.thingIDNumber;pawn.drafter.Drafted=true;
     comp=gun.GetComp<CompModularGun>();var verb=gun.GetComp<CompEquippable>().PrimaryVerb;
     Check(!comp.Platform.ConfigErrors().Any(),"platform configuration valid");
     Check(GunSmithPartQuery.GetVisibleSlotIds(comp).SequenceEqual(new[]{"Optic"})&&comp.IsOptionalSlot("Optic")&&comp.GetPart("Optic")==null,"only optional shared optic slot; no grip or dual-wield option");
     Check(!comp.EnablesDualWield(),"single wield");
     Check(GunSmithPlatformService.EnumeratePlatforms().Contains(comp.Platform)&&GunSmithPlatformService.IsPlatformAvailable(comp.Platform),"available in native gunsmith menu");
     Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,comp).Single().count==100,"bare weapon costs 100 solarite");
     Check(verb.GetType()==typeof(Verb_Shoot)&&def.Verbs[0].defaultProjectile.thingClass==typeof(Beam),"native single-shot instant beam classes");
     Check(verb.EffectiveRange==55&&!verb.CanHitTarget(center+IntVec3.North*60),"55 cell base range excludes distant target");
     Check(def.Verbs[0].warmupTime==2.4f&&def.Verbs[0].burstShotCount==1&&gun.GetStatValue(StatDefOf.RangedWeapon_Cooldown)==2.5f,"long aim, one shot, long recovery");
     var projectile=def.Verbs[0].defaultProjectile.projectile;
     Check(projectile.GetDamageAmount(gun)==90&&Math.Abs(projectile.GetArmorPenetration(gun)-2f)<.001f&&projectile.damageDef==DamageDefOf.Burn&&projectile.damageDef.armorCategory.defName=="Heat","90 heat damage with 200 percent heat penetration");
     var texture=ContentFinder<Texture2D>.Get(comp.Platform.bodyTexPath);Check(texture.width==1024&&texture.height==512,"1024x512 body source sizes shared optics through native texture ratios");Bake("bare");
     foreach(var optic in new[]{"PMP_HardlightRifle_Optic","PM_Common_MarksmanOptic","PM_Common_ViperSight"}){
      Check(GunSmithPartQuery.GetPartsForPlatform(comp.Platform,"Optic").Any(p=>p.defName==optic),"shared optic available "+optic);comp.SetPartDefName("Optic",optic);
      var part=comp.GetPart("Optic");var placement=GunSmithCompositeLayout.ResolvePartAttachmentOffset(comp.Platform,part,"Optic",comp.GetPart);
      Check((part.platformOverrides==null||!part.platformOverrides.Any(p=>p.platform==comp.Platform))&&Math.Abs(part.GetPreviewDrawSizeFor(comp.Platform)-part.previewDrawSize)<.001f&&Math.Abs(placement.x-.2f)<.001f&&Math.Abs(placement.y+.095f)<.001f,"unmodified shared optic scale, no Z750 part override; placement belongs to weapon slot: "+optic);
      Bake(optic);comp.SetPartDefName("Optic",null);
      Check(comp.GetPart("Optic")==null&&verb.EffectiveRange==55,"optic removal restores bare range");
     }
     target=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.WoodLog);GenSpawn.Spawn(target,center+IntVec3.East*30,map);hp=target.HitPoints;
     var blocker=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDef.Named("BlocksGranite"));GenSpawn.Spawn(blocker,center+IntVec3.East*15,map);
     Check(!verb.CanHitTarget(target),"opaque wall blocks line of sight");blocker.Destroy(DestroyMode.Vanish);
     CaptureBinaryBeam.Shots.Clear();Check(verb.TryStartCastOn(target),"native long aim begins");started=Find.TickManager.TicksGame;stage=1;return;
    }
    if(stage==1){
     if(Find.TickManager.TicksGame-started<40)return;
     Check(pawn.stances.curStance is Stance_Warmup&&CaptureBinaryBeam.Shots.Count==0&&target.HitPoints==hp,"no damage before warmup completes");
     Check(CaptureBinaryMotes.Alive("PMP_Mote_BinaryAim"),"native aiming line appears during warmup");
     Find.Selector.ClearSelection();Find.Selector.Select(pawn);Find.CameraDriver.SetRootPosAndSize(pawn.DrawPos+Vector3.right*15,22);ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"aiming.png"));
     started=Find.TickManager.TicksGame;stage=10;return;
    }
    if(stage==10){
     if(Find.TickManager.TicksGame-started<5)return;
     pawn.stances.CancelBusyStanceHard();started=Find.TickManager.TicksGame;stage=2;return;
    }
    if(stage==2){
     if(Find.TickManager.TicksGame-started<30)return;
     Check(CaptureBinaryBeam.Shots.Count==0&&target.HitPoints==hp,"interrupting aim cancels shot");
     File.WriteAllText(Path.Combine(Folder,"mote-state.txt"),"tick="+Find.TickManager.TicksGame+" stance="+pawn.stances.curStance+"\n"+string.Join("\n",CaptureBinaryMotes.Motes.Select(m=>m.def.defName+" spawned="+m.Spawned+" destroyed="+m.Destroyed+" age="+m.AgeSecs+" maintain="+m.def.mote.needsMaintenance+" fade="+m.def.mote.fadeOutTime+" ticker="+m.def.tickerType+" realtime="+m.def.mote.realTime+" last="+AccessTools.Field(typeof(Mote),"lastMaintainTick").GetValue(m))));
     Check(!CaptureBinaryMotes.Alive("PMP_Mote_BinaryAim"),"interrupted aim line expires without custom cleanup");
     Check(gun.GetComp<CompEquippable>().PrimaryVerb.TryStartCastOn(target),"fresh aim starts after interruption");started=Find.TickManager.TicksGame;stage=3;return;
    }
    if(stage==3){
     if(CaptureBinaryBeam.Shots.Count==0)return;
     Check(CaptureBinaryBeam.Shots.Count==1,"exactly one beam fired");
     Check(CaptureBinaryBeam.Shots[0]-started>=120,"long warmup respected with Prometheus aiming modifier");
     Check(target.HitPoints==hp-90||target.HitPoints==hp,"native aimed shot uses ordinary accuracy: damage="+(hp-target.HitPoints));
     // Keep normal shooting accuracy intact. Test exact damage separately with a known hit.
     hp=target.HitPoints;
     var impact=(Beam)GenSpawn.Spawn(ThingDef.Named("PMP_BinaryBeam"),pawn.Position,map);
     CaptureBinaryBeam.Record=false;
     try{impact.Launch(pawn,pawn.DrawPos,target,target,ProjectileHitFlags.IntendedTarget,false,gun);}
     finally{CaptureBinaryBeam.Record=true;}
     Check(target.HitPoints==hp-90,"one native beam impact deals exactly 90 damage to wooden wall");
     Check(map.listerThings.ThingsOfDef(ThingDef.Named("PMP_BinaryBeam")).Count==0,"beam resolves instantly with no travelling projectile");
     Check(CaptureBinaryMotes.Alive("PMP_Mote_BinaryShot"),"brief discharge beam appears");
     ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"firing.png"));started=Find.TickManager.TicksGame;stage=4;return;
    }
    if(stage==4){
     if(Find.TickManager.TicksGame-started<165)return;
     Check(CaptureBinaryBeam.Shots.Count==1&&target.HitPoints==hp-90,"no sustained beam or follow-up shots");
     Check(!CaptureBinaryMotes.Alive("PMP_Mote_BinaryShot"),"discharge visual expires");
     comp.SetPartDefName("Optic","PMP_HardlightRifle_Optic");Check(gun.GetComp<CompEquippable>().PrimaryVerb.EffectiveRange==59&&Math.Abs(gun.GetStatValue(StatDefOf.Mass)-4.7f)<.01f,"shared sight gives range 59 and mass 4.7");
     stage=5;loading=true;GameDataSaveLoader.SaveGame("BinaryRifleAudit");LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("BinaryRifleAudit"),"LoadingLongEvent",false,null);return;
    }
    if(stage==5){
     pawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==pawnId);gun=pawn.equipment.Primary;comp=gun.GetComp<CompModularGun>();
     Check(!comp.EnablesDualWield()&&gun.thingIDNumber==gunId&&comp.GetPart("Optic").defName=="PMP_HardlightRifle_Optic"&&gun.GetComp<CompEquippable>().PrimaryVerb.EffectiveRange==59,"save/load preserves single weapon and shared optic stats");
     var pylon=ThingMaker.MakeThing(ThingDef.Named("Pylon"));pylon.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(pylon,pawn.Position+IntVec3.South*5,map);
     var cabinet=ThingMaker.MakeThing(ThingDef.Named("PM_HyperspaceStorage"));cabinet.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(cabinet,pawn.Position+IntVec3.West*5,map);
     AccessTools.Method(typeof(CompHyperspaceStorage),"DepositInternal").Invoke(cabinet.TryGetComp<CompHyperspaceStorage>(),new object[]{ThingDef.Named("PM_Solarite"),105L});
     var loadout=new GunSmithLoadout(comp);Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,loadout).Single().count==105,"shared-sight configuration costs 105 solarite");
     Check(GunSmithOperations.SpawnWeaponAt(comp.Platform,loadout,pawn.Position+IntVec3.South*2),"native gunsmith warp accepted");Check(cabinet.TryGetComp<CompHyperspaceStorage>().StoredCount==0,"warp deducts 105 solarite");stage=6;return;
    }
    if(stage==6){
     var delivered=map.listerThings.ThingsOfDef(ThingDef.Named("PMP_BinaryRifle"));if(delivered.Count==0)return;
     Check(delivered.Count==1&&!delivered[0].TryGetComp<CompModularGun>().EnablesDualWield()&&delivered[0].TryGetComp<CompModularGun>().GetPart("Optic").defName=="PMP_HardlightRifle_Optic","one correctly configured weapon delivered");
     Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no game errors through rendering, firing, save/load and paid warp");File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Binary rifle native audit passed.");done=true;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());done=true;}
  }
 }
}
