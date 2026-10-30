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
 [StaticConstructorOnStartup] public static class IncinerationBootstrap {
  static IncinerationBootstrap(){if(GenCommandLine.CommandLineArgPassed("pmp-incineration-cannon-audit"))new Harmony("pmp.incineration.audit").PatchAll();}
 }
 [HarmonyPatch] static class CaptureIncinerationShots {
  public static readonly List<string> Shots=new List<string>();
  static MethodBase TargetMethod()=>AccessTools.Method(typeof(Projectile),"Launch",new[]{typeof(Thing),typeof(Vector3),typeof(LocalTargetInfo),typeof(LocalTargetInfo),typeof(ProjectileHitFlags),typeof(bool),typeof(Thing),typeof(ThingDef)});
  static void Postfix(Projectile __instance){if(__instance.def.defName.StartsWith("PMP_Incineration"))Shots.Add(__instance.def.defName);}
 }
 [HarmonyPatch(typeof(Projectile_Explosive),"Explode")] static class CaptureIncinerationBlasts {
  public static readonly List<(string name,int tick,IntVec3 cell)> Blasts=new List<(string,int,IntVec3)>();
  static void Prefix(Projectile_Explosive __instance){if(__instance.def.defName.StartsWith("PMP_Incineration"))Blasts.Add((__instance.def.defName,Find.TickManager.TicksGame,__instance.Position));}
  public static int Main=>Blasts.Count(b=>b.name=="PMP_IncinerationRound");
  public static int Children=>Blasts.Count(b=>b.name=="PMP_IncinerationSubmunition");
 }
 public sealed class IncinerationAudit : GameComponent {
  int stage,frames,exitFrames,pawnId,gunId,started,wallId,wallHp,before;bool done,loading;float realStart;
  List<int> childIds=new List<int>(),fuses=new List<int>();IntVec3 bank;
  Pawn pawn;ThingWithComps gun;CompModularGun comp;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"IncinerationAudit");
  ThingDef Main=>ThingDef.Named("PMP_IncinerationRound");
  ThingDef Child=>ThingDef.Named("PMP_IncinerationSubmunition");
  List<Projectile_Explosive> Children(Map map)=>map.listerThings.ThingsOfDef(Child).Cast<Projectile_Explosive>().ToList();
  int Fuse(Projectile_Explosive p)=>(int)AccessTools.Field(typeof(Projectile_Explosive),"ticksToDetonation").GetValue(p);
  public IncinerationAudit(Game game){}
  public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref stage,"incStage");Scribe_Values.Look(ref pawnId,"incPawn");Scribe_Values.Look(ref gunId,"incGun");Scribe_Collections.Look(ref childIds,"incChildren",LookMode.Value);Scribe_Collections.Look(ref fuses,"incFuses",LookMode.Value);}
  void Check(bool ok,string message){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
  void Bake(string name){var tex=GunSmithTextureBaker.BakeComposite(comp.Platform,comp.GetPart,gun.def.graphicData.drawSize,out var size);Check(tex!=null&&size.x>0,"native GPU bake "+name);File.WriteAllBytes(Path.Combine(Folder,name+".png"),tex.EncodeToPNG());}
  Thing Wall(IntVec3 pos,Map map,bool wood=false){var wall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDef.Named(wood?"WoodLog":"BlocksGranite"));wall.SetFaction(Faction.OfPlayer);return GenSpawn.Spawn(wall,pos,map);}
  Projectile_Explosive ImpactAt(ThingDef def,IntVec3 pos,Map map,Thing target=null,bool shield=false){
   var p=(Projectile_Explosive)GenSpawn.Spawn(def,pos,map);LocalTargetInfo aim=target!=null?new LocalTargetInfo(target):new LocalTargetInfo(pos);
   p.Launch(pawn,pos.ToVector3Shifted(),aim,aim,ProjectileHitFlags.All,false,gun);
   AccessTools.Method(p.GetType(),"Impact").Invoke(p,new object[]{target,shield});return p;
  }
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-incineration-cannon-audit"))return;
   if(done){if(++exitFrames>60)Application.Quit();return;}
   if(loading||Find.CurrentMap==null||(stage==0&&++frames<90))return;
   Directory.CreateDirectory(Folder);if(realStart==0)realStart=Time.realtimeSinceStartup;
   try {
    if(Time.realtimeSinceStartup-realStart>180)throw new Exception("Timeout stage "+stage);
    var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
    if(stage==0){
     var def=ThingDef.Named("PMP_IncinerationCannon");foreach(var d in new[]{def,Main,Child})Check(!d.ConfigErrors().Any(),"valid native Def "+d.defName+": "+string.Join(";",d.ConfigErrors()));
     foreach(var cell in CellRect.CenteredOn(map.Center,60).ClipInsideMap(map)){
      map.roofGrid.SetRoof(cell,null);
      foreach(var thing in cell.GetThingList(map).ToList())if(!(thing is Pawn)){if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);else thing.DeSpawn();}
      map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);map.fogGrid.Unfog(cell);
     }
     pawn=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_Prometheus"),Faction.OfPlayer);GenSpawn.Spawn(pawn,map.Center,map);pawnId=pawn.thingIDNumber;pawn.equipment.DestroyAllEquipment();
     gun=(ThingWithComps)ThingMaker.MakeThing(def);pawn.equipment.AddEquipment(gun);gunId=gun.thingIDNumber;pawn.drafter.Drafted=true;comp=gun.GetComp<CompModularGun>();
     Check(!comp.Platform.ConfigErrors().Any()&&GunSmithPlatformService.IsPlatformAvailable(comp.Platform),"native launcher platform available");
     Check(comp.Platform.weaponCategory==GunSmithWeaponCategory.Launcher&&GunSmithPartQuery.GetVisibleSlotIds(comp).SequenceEqual(new[]{"UnderBarrel"})&&comp.IsOptionalSlot("UnderBarrel")&&!comp.EnablesDualWield(),"single-wield launcher with only an optional underbarrel slot");
     Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,comp).Single().count==150,"base cost 150 solarite");
     var verb=gun.GetComp<CompEquippable>().PrimaryVerb;
     Check(verb.GetType()==typeof(Verb_Shoot)&&verb.EffectiveRange==36&&!verb.CanHitTarget(pawn.Position+IntVec3.East*4)&&!verb.CanHitTarget(pawn.Position+IntVec3.East*42),"native single shot with 36 cell maximum and 5.9 cell minimum range");
     Check(def.Verbs[0].burstShotCount==1&&def.Verbs[0].warmupTime==1.7f&&gun.GetStatValue(StatDefOf.RangedWeapon_Cooldown)==3.8f,"single shot, 1.7 second aim and 3.8 second recovery");
     Check(Main.thingClass==typeof(Projectile_Incineration)&&Child.thingClass==typeof(Projectile_Explosive)&&Child.projectile.explosionDelay==18,"cluster main round and native delayed explosive children");
     foreach(var d in new[]{Main,Child})Check(d.projectile.damageDef==DamageDefOf.Burn&&d.projectile.damageDef.armorCategory.defName=="Heat"&&d.projectile.GetArmorPenetration(gun)==2,"200 percent heat penetration "+d.defName);
     Check(Main.projectile.GetDamageAmount(gun)==80&&Child.projectile.GetDamageAmount(gun)==28,"80 main and 28 secondary base damage");
     var tex=ContentFinder<Texture2D>.Get(comp.Platform.bodyTexPath);Check(tex.width==1024&&tex.height==512,"1024x512 body sizes shared attachments");Bake("bare");
     var rifle=DefDatabase<GunSmithPlatformDef>.GetNamed("PMP_GunSmith_HardlightRifle");
     var parts=GunSmithPartQuery.GetPartsForPlatform(comp.Platform,"UnderBarrel").ToArray();
     Check(parts.Length==5&&parts.Select(p=>p.defName).OrderBy(n=>n).SequenceEqual(GunSmithPartQuery.GetPartsForPlatform(rifle,"UnderBarrel").Select(p=>p.defName).OrderBy(n=>n)),"same five underbarrels as Z250");
     Check(comp.GetPart("UnderBarrelMount")==null,"bare cannon has no armor mount");
     foreach(var part in parts){
      comp.SetPartDefName("UnderBarrel",part.defName);
      Check(comp.GetPart("UnderBarrelMount")?.defName=="PMP_HardlightRifle_UnderbarrelMount","shared conditional armor with "+part.defName);
      Check(part.GetPreviewDrawSizeFor(comp.Platform)==part.previewDrawSize&&!(part.platformOverrides?.Any(o=>o.platform==comp.Platform)??false),"no per-weapon part override "+part.defName);
      Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,comp).Single().count==150+part.GetModExtension<GunSmithContentExtension>().costList.Single().count,"native attachment cost "+part.defName);
      // Enumerate native worker gizmos while the cannon is equipped.
      Check(comp.CompGetGizmosExtra().ToArray()!=null,"native equipped attachment gizmos "+part.defName);
      Bake(part.defName);comp.SetPartDefName("UnderBarrel",null);
      Check(comp.GetPart("UnderBarrelMount")==null,"removing underbarrel clears armor "+part.defName);
     }
     foreach(var name in new[]{"PMP_Suppressor","PMP_Scattershot"}){
      var other=(ThingWithComps)ThingMaker.MakeThing(ThingDef.Named(name));var oc=other.GetComp<CompModularGun>();
      Check(GunSmithPartQuery.GetPartsForPlatform(oc.Platform,"Optic").Select(p=>p.defName).SequenceEqual(new[]{"PM_Common_ViperSight"}),"Viper is the only optic for "+name);
      oc.SetPartDefName("Optic","PM_Common_ViperSight");Check(oc.GetPart("Optic")?.defName=="PM_Common_ViperSight","Viper fits "+name);oc.SetPartDefName("Optic",null);Check(oc.GetPart("Optic")==null,"optic may remain empty "+name);other.Destroy();
     }
     pawn.equipment.Remove(gun);
     var pistol=(ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("PMP_Boltshot"));pawn.equipment.AddEquipment(pistol);
     var pc=pistol.GetComp<CompModularGun>();pc.SetPartDefName("Ammo","PMP_Boltshot_Charged");pc.SetPartDefName("Optic",null);
     Check(pistol.GetComp<CompEquippable>().PrimaryVerb.EffectiveRange==14&&pc.GetPart("Ammo").rangeOffset==-10,"charged pistol effective range 14 with -10 penalty");
     Check(pistol.def.Verbs[0].aimingChargeMote==null,"pistol charge glow remains removed");
     pawn.equipment.DestroyAllEquipment();pawn.equipment.AddEquipment(gun);
     CaptureIncinerationShots.Shots.Clear();CaptureIncinerationBlasts.Blasts.Clear();
     Check(verb.TryStartCastOn(pawn.Position+IntVec3.East*25),"native live shot starts");started=Find.TickManager.TicksGame;stage=1;return;
    }
    if(stage==1){
     if(CaptureIncinerationBlasts.Main==0)return;
     Check(CaptureIncinerationShots.Shots.Count(n=>n==Main.defName)==1&&CaptureIncinerationBlasts.Main==1,"one shot creates exactly one main explosion");
     Check(CaptureIncinerationShots.Shots.Count(n=>n==Child.defName)==4&&Children(map).Count==4,"main explosion scatters exactly four live submunitions");
     Check(CaptureIncinerationBlasts.Children==0&&Find.TickManager.TicksGame-started>=85,"secondary explosions are delayed after actual aiming and main flight");
     Check(Children(map).All(p=>p.Launcher==pawn&&p.DamageAmount==28&&p.ArmorPenetration==2),"children preserve launcher attribution and heat damage");
     Find.Selector.ClearSelection();Find.Selector.Select(pawn);Find.CameraDriver.SetRootPosAndSize(pawn.DrawPos+Vector3.right*20,17);ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"main-blast.png"));
     comp.SetPartDefName("UnderBarrel","PM_Common_UB_FrontGrip01");stage=2;return;
    }
    if(stage==2){
     var children=Children(map);if(children.Count!=4||children.Any(p=>Fuse(p)<=0))return;
     Check(CaptureIncinerationBlasts.Children==0,"all four children land before first secondary detonation");
     childIds=children.Select(p=>p.thingIDNumber).ToList();fuses=children.Select(Fuse).ToList();
     Check(fuses.All(t=>t>0&&t<=18),"native fuses are counting down");
     stage=3;loading=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;GameDataSaveLoader.SaveGame("IncinerationAudit");LongEventHandler.QueueLongEvent(()=>GameDataSaveLoader.LoadGame("IncinerationAudit"),"LoadingLongEvent",false,null);return;
    }
    if(stage==3){
     pawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==pawnId);gun=pawn.equipment.Primary;comp=gun.GetComp<CompModularGun>();
     var children=Children(map);Check(children.Count==4&&childIds.All(id=>children.Any(p=>p.thingIDNumber==id)),"all landed children survive save/load");
     Check(children.All(p=>Fuse(p)>0&&Fuse(p)<=fuses[childIds.IndexOf(p.thingIDNumber)]),"native fuse remainder survives save/load without reset");
     Check(gun.thingIDNumber==gunId&&comp.GetPart("UnderBarrel").defName=="PM_Common_UB_FrontGrip01"&&comp.GetPart("UnderBarrelMount")?.defName=="PMP_HardlightRifle_UnderbarrelMount"&&gun.GetComp<CompEquippable>().PrimaryVerb.EffectiveRange==36&&!comp.EnablesDualWield(),"weapon, underbarrel and conditional armor survive save/load");
     started=Find.TickManager.TicksGame;stage=4;return;
    }
    if(stage==4){
     if(Find.TickManager.TicksGame-started<70)return;
     Check(CaptureIncinerationBlasts.Main==1&&CaptureIncinerationBlasts.Children==4&&Children(map).Count==0,"exactly four secondary explosions, no recursive clusters");
     Check(CaptureIncinerationBlasts.Blasts.Where(b=>b.name==Child.defName).All(b=>b.tick>CaptureIncinerationBlasts.Blasts[0].tick),"all secondary blasts occur after main blast");
     bank=map.Center+IntVec3.West*25;var wall=Wall(bank,map,true);wallId=wall.thingIDNumber;wallHp=wall.HitPoints;
     before=CaptureIncinerationBlasts.Children;ImpactAt(Child,bank,map,wall);Check(wall.HitPoints==wallHp,"landed submunition does not damage before fuse");started=Find.TickManager.TicksGame;stage=5;return;
    }
    if(stage==5){
     if(Find.TickManager.TicksGame-started<40)return;
     var wall=map.listerThings.ThingsOfDef(ThingDefOf.Wall).Single(t=>t.thingIDNumber==wallId);
     Check(wallHp-wall.HitPoints==28&&CaptureIncinerationBlasts.Children==before+1,"one child really deals 28 heat damage at blast center to a friendly wall");
     before=CaptureIncinerationShots.Shots.Count(n=>n==Child.defName);ImpactAt(Main,bank+IntVec3.North*12,map,null,true);
     Check(CaptureIncinerationShots.Shots.Count(n=>n==Child.defName)==before&&Children(map).Count==0,"intercepted main round does not release children");
     ImpactAt(Main,new IntVec3(0,0,0),map);Check(Children(map).Count==4&&Children(map).All(p=>p.usedTarget.Cell.InBounds(map)),"map-edge cluster keeps all four destinations in bounds");started=Find.TickManager.TicksGame;stage=6;return;
    }
    if(stage==6){
     if(Find.TickManager.TicksGame-started<90)return;
     Check(Children(map).Count==0,"edge cluster resolves without stranding projectiles");
     bank=map.Center+IntVec3.South*25;
     foreach(var cell in CellRect.CenteredOn(bank,1))if(cell!=bank)Wall(cell,map);
     ImpactAt(Main,bank,map);
     Check(Children(map).Count==4&&Children(map).All(p=>p.usedTarget.Thing is Building&&p.usedTarget.Cell.AdjacentTo8Way(bank)),"short child arcs target intervening walls instead of crossing them");
     before=CaptureIncinerationBlasts.Blasts.Count;started=Find.TickManager.TicksGame;stage=7;return;
    }
    if(stage==7){
     if(Find.TickManager.TicksGame-started<90)return;
     Check(Children(map).Count==0&&CaptureIncinerationBlasts.Blasts.Skip(before).Count()==4&&CaptureIncinerationBlasts.Blasts.Skip(before).All(b=>b.cell.AdjacentTo8Way(bank)),"four secondary blasts stop at the enclosing wall ring");
     var pylon=ThingMaker.MakeThing(ThingDef.Named("Pylon"));pylon.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(pylon,pawn.Position+IntVec3.South*5,map);
     var cabinet=ThingMaker.MakeThing(ThingDef.Named("PM_HyperspaceStorage"));cabinet.SetFaction(Faction.OfPlayer);GenSpawn.Spawn(cabinet,pawn.Position+IntVec3.West*5,map);
     AccessTools.Method(typeof(CompHyperspaceStorage),"DepositInternal").Invoke(cabinet.TryGetComp<CompHyperspaceStorage>(),new object[]{ThingDef.Named("PM_Solarite"),160L});
     var loadout=new GunSmithLoadout(comp);Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,loadout).Single().count==160,"foregrip configuration costs 160 solarite");
     Check(GunSmithOperations.SpawnWeaponAt(comp.Platform,loadout,pawn.Position+IntVec3.South*2),"native gunsmith warp accepted");Check(cabinet.TryGetComp<CompHyperspaceStorage>().StoredCount==0,"warp deducts exactly 160 solarite");stage=8;return;
    }
    if(stage==8){
     var delivered=map.listerThings.ThingsOfDef(ThingDef.Named("PMP_IncinerationCannon"));if(delivered.Count==0)return;
     Check(delivered.Count==1&&!delivered[0].TryGetComp<CompModularGun>().EnablesDualWield()&&delivered[0].TryGetComp<CompModularGun>().GetPart("UnderBarrel").defName=="PM_Common_UB_FrontGrip01","one correctly configured single-wield cannon delivered");
     Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no native errors through explosions, save/load, map edge, wall interception and paid warp");File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Incineration cannon native audit passed.");done=true;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());done=true;}
  }
 }
}
