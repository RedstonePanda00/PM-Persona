using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GunSmithFramework;
using HarmonyLib;
using PMP_Persona.Prometheus;
using ProtossMech;
using ProtossMech.GunSmith;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 [StaticConstructorOnStartup] public static class ScattershotAuditBootstrap {
  static ScattershotAuditBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-scattershot-audit"))new Harmony("pmp.scattershot.audit").PatchAll();}
 }
 [HarmonyPatch] static class CaptureScattershot {
  public static readonly List<(int tick,Vector3 origin)> Shots=new List<(int,Vector3)>();
  static MethodBase TargetMethod()=>AccessTools.Method(typeof(Projectile),"Launch",new[]{typeof(Thing),typeof(Vector3),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(ProjectileHitFlags),typeof(bool),typeof(Thing),typeof(ThingDef)});
  static void Postfix(Thing equipment,Vector3 origin){if(equipment?.def.defName=="PMP_Scattershot")Shots.Add((Find.TickManager.TicksGame,origin));}
 }
 public sealed class ScattershotAudit : GameComponent {
  int stage,frames,exitFrames,pawnId,gunId,bounceId,started;bool done,loading;float realStart;IntVec3 bank;
  Pawn pawn;ThingWithComps gun;Thing target;CompModularGun comp;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"ScattershotAudit");
  ThingDef Pellet=>ThingDef.Named("PMP_ScattershotPellet");
  bool Bounced(Thing t)=>(bool)AccessTools.Field(typeof(Bullet_Scattershot),"ricocheted").GetValue(t);
  public ScattershotAudit(Game game){}
  public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref stage,"scattershotStage");Scribe_Values.Look(ref pawnId,"scattershotPawn");Scribe_Values.Look(ref gunId,"scattershotGun");Scribe_Values.Look(ref bounceId,"scattershotBounce");Scribe_Values.Look(ref bank,"scattershotBank");}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  void Bake(string name){var t=GunSmithTextureBaker.BakeComposite(comp.Platform,comp.GetPart,gun.def.graphicData.drawSize,out var size);Check(t!=null&&size.x>0,"GPU bake "+name);File.WriteAllBytes(Path.Combine(Folder,name+".png"),t.EncodeToPNG());}
  Thing Wall(IntVec3 cell,Map map,bool wood=false){var t=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDef.Named(wood?"WoodLog":"BlocksGranite"));return GenSpawn.Spawn(t,cell,map);}
  Bullet_Scattershot Probe(IntVec3 from,Thing to,Map map){var p=(Bullet_Scattershot)GenSpawn.Spawn(Pellet,from,map);p.Launch(pawn,from.ToVector3Shifted(),to,to,ProjectileHitFlags.All);return p;}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-scattershot-audit"))return;
   if(done){if(++exitFrames>60)Application.Quit();return;}
   if(loading||Find.CurrentMap==null||(stage==0&&++frames<90))return;
   Directory.CreateDirectory(Folder);if(realStart==0)realStart=Time.realtimeSinceStartup;
   try {
    if(Time.realtimeSinceStartup-realStart>180)throw new Exception("Timeout stage="+stage);
    var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
    if(stage==0){
     var def=ThingDef.Named("PMP_Scattershot");Check(!def.ConfigErrors().Any(),"native weapon Def validation: "+string.Join("; ",def.ConfigErrors()));var center=map.Center;
     foreach(var cell in CellRect.CenteredOn(center,15).ClipInsideMap(map)){
      foreach(var t in cell.GetThingList(map).ToList())if(!(t is Pawn)){if(t.def.destroyable)t.Destroy(DestroyMode.Vanish);else t.DeSpawn();}
      map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);map.fogGrid.Unfog(cell);
     }
     pawn=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_Prometheus"),Faction.OfPlayer);GenSpawn.Spawn(pawn,center,map);pawnId=pawn.thingIDNumber;
     pawn.equipment.DestroyAllEquipment();gun=(ThingWithComps)ThingMaker.MakeThing(def);pawn.equipment.AddEquipment(gun);gunId=gun.thingIDNumber;pawn.drafter.Drafted=true;
     comp=gun.GetComp<CompModularGun>();var verb=gun.GetComp<CompEquippable>().PrimaryVerb;
     Check(!comp.Platform.ConfigErrors().Any(),"platform valid");
     Check(GunSmithPartQuery.GetVisibleSlotIds(comp).SequenceEqual(new[]{"Grip","Optic"})&&comp.GetPart("Grip")==null&&comp.GetPart("Optic")==null,"native optional grip and optic initially empty");
     Check(GunSmithPlatformService.IsPlatformAvailable(comp.Platform)&&GunSmithCostUtility.BuildWarpCosts(comp.Platform,comp).Single().count==50,"available through gunsmith for 50 solarite");
     Check(verb.GetType()==typeof(Verb_ShootShotgun)&&verb.EffectiveRange==12,"existing shotgun verb with 12 cell base range");
     Check(Pellet.projectile.GetDamageAmount(gun)==10&&Pellet.projectile.GetArmorPenetration(gun)==2&&Pellet.projectile.damageDef.armorCategory.defName=="Heat","10 damage and 200 percent heat penetration");
     var tex=ContentFinder<Texture2D>.Get(comp.Platform.bodyTexPath);Check(tex.width==512&&tex.height==256,"512x256 body");Bake("bare");
     foreach(var optic in new[]{"PM_Common_ViperSight"}){
      Check(GunSmithPartQuery.GetPartsForPlatform(comp.Platform,"Optic").Any(p=>p.defName==optic),"shared optic "+optic);comp.SetPartDefName("Optic",optic);Bake(optic);comp.SetPartDefName("Optic",null);Check(verb.EffectiveRange==12,"optic removal restores range");
     }
     comp.SetPartDefName("Grip","PMP_Scattershot_GripDual");Check(comp.EnablesDualWield()&&Math.Abs(gun.GetStatValue(StatDefOf.Mass)-6)<.01f,"dual grip adds second gun mass");comp.SetPartDefName("Grip",null);
     Check(!comp.EnablesDualWield()&&Math.Abs(gun.GetStatValue(StatDefOf.Mass)-3)<.01f,"empty grip restores single gun");
     target=Wall(center+IntVec3.East*6,map);CaptureScattershot.Shots.Clear();Check(verb.TryStartCastOn(target),"single shotgun starts");started=Find.TickManager.TicksGame;stage=1;return;
    }
    if(stage==1){
     if(Find.TickManager.TicksGame-started<120)return;bool dual=comp.EnablesDualWield();
     Check(CaptureScattershot.Shots.Count==(dual?16:8)&&CaptureScattershot.Shots.Select(s=>s.tick).Distinct().Count()==1,dual?"16 simultaneous pellets, no extra Launch on ricochet":"8 simultaneous pellets");
     Check(target.HitPoints<target.MaxHitPoints,"pellets damage target");
     if(!dual){comp.SetPartDefName("Grip","PMP_Scattershot_GripDual");CaptureScattershot.Shots.Clear();Check(gun.GetComp<CompEquippable>().PrimaryVerb.TryStartCastOn(target),"dual shotgun starts");started=Find.TickManager.TicksGame;return;}
     Check(CaptureScattershot.Shots.Select(s=>s.origin).Distinct().Count()==2,"dual pellets use two muzzles");
     comp.SetPartDefName("Optic","PM_Common_ViperSight");Check(gun.GetComp<CompEquippable>().PrimaryVerb.EffectiveRange==12&&Math.Abs(gun.GetStatValue(StatDefOf.Mass)-6.05f)<.01f,"dual shared optic stats");
     bank=map.Center+IntVec3.West*9;var first=Wall(bank+IntVec3.North*5,map);foreach(var offset in new[]{IntVec3.West,IntVec3.Zero,IntVec3.East})Wall(bank+IntVec3.South*2+offset,map,true);Probe(bank,first,map);stage=2;return;
    }
    if(stage==2){
     var bounce=map.listerThings.ThingsOfDef(Pellet).OfType<Bullet_Scattershot>().FirstOrDefault(p=>Bounced(p));if(bounce==null)return;
     Check(map.listerThings.ThingsOfDef(Pellet).Count==1,"native collision produces exactly one continuation");
     Check(Vector3.Dot(bounce.ExactRotation*Vector3.forward,Vector3.back)>.9f,"north wall reflects pellet south without tracking");
     Check(bounce.DamageAmount==8&&bounce.ArmorPenetration==2,"ricochet keeps 75 percent rounded damage and 200 percent penetration");
     bounceId=bounce.thingIDNumber;stage=3;loading=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;GameDataSaveLoader.SaveGame("ScattershotAudit");LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("ScattershotAudit"),"LoadingLongEvent",false,null);return;
    }
    if(stage==3){
     pawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==pawnId);gun=pawn.equipment.Primary;comp=gun.GetComp<CompModularGun>();
     var bounce=map.listerThings.ThingsOfDef(Pellet).OfType<Bullet_Scattershot>().Single(p=>p.thingIDNumber==bounceId);
     Check(Bounced(bounce)&&bounce.DamageAmount==8,"live ricochet state and damage survive save/load");
     Check(comp.EnablesDualWield()&&comp.GetPart("Optic").defName=="PM_Common_ViperSight"&&gun.thingIDNumber==gunId,"dual gun and shared optic survive save/load");
     started=Find.TickManager.TicksGame;stage=4;return;
    }
    if(stage==4){
     if(Find.TickManager.TicksGame-started<50)return;
     var second=new[]{IntVec3.West,IntVec3.Zero,IntVec3.East}.Select(offset=>(bank+IntVec3.South*2+offset).GetEdifice(map));Check(second.Sum(w=>w.MaxHitPoints-w.HitPoints)==8,"reflected pellet really damages second wall: "+second.Sum(w=>w.MaxHitPoints-w.HitPoints));
     Check(map.listerThings.ThingsOfDef(Pellet).Count==0,"second wall consumes ricochet with no further bounce");
     var first=(bank+IntVec3.North*5).GetEdifice(map);var shielded=Probe(bank,first,map);
     AccessTools.Method(typeof(Bullet_Scattershot),"Impact").Invoke(shielded,new object[]{first,true});Check(map.listerThings.ThingsOfDef(Pellet).Count==0,"shield block does not ricochet");
     var victim=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);GenSpawn.Spawn(victim,bank+IntVec3.West*2,map);var body=Probe(bank,victim,map);
     AccessTools.Method(typeof(Bullet_Scattershot),"Impact").Invoke(body,new object[]{victim,false});Check(map.listerThings.ThingsOfDef(Pellet).Count==0,"pawn hit does not ricochet");
     var pylon=ThingMaker.MakeThing(ThingDef.Named("Pylon"));pylon.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(pylon,pawn.Position+IntVec3.South*5,map);
     var cabinet=ThingMaker.MakeThing(ThingDef.Named("PM_HyperspaceStorage"));cabinet.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(cabinet,pawn.Position+IntVec3.West*5,map);
     AccessTools.Method(typeof(CompHyperspaceStorage),"DepositInternal").Invoke(cabinet.TryGetComp<CompHyperspaceStorage>(),new object[]{ThingDef.Named("PM_Solarite"),130L});
     var loadout=new GunSmithLoadout(comp);Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,loadout).Single().count==130,"dual and shared optic costs 130 solarite");Check(GunSmithOperations.SpawnWeaponAt(comp.Platform,loadout,pawn.Position+IntVec3.South*2),"native gunsmith warp accepted");Check(cabinet.TryGetComp<CompHyperspaceStorage>().StoredCount==0,"warp deducts 130 solarite");stage=5;return;
    }
    if(stage==5){
     var delivered=map.listerThings.ThingsOfDef(ThingDef.Named("PMP_Scattershot"));if(delivered.Count==0)return;
     Check(delivered.Count==1&&delivered[0].TryGetComp<CompModularGun>().EnablesDualWield()&&delivered[0].TryGetComp<CompModularGun>().GetPart("Optic").defName=="PM_Common_ViperSight","one correctly configured dual weapon delivered");
     Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no game errors through firing, ricochet, save/load and warp");File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Scattershot native audit passed.");done=true;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());done=true;}
  }
 }
}
