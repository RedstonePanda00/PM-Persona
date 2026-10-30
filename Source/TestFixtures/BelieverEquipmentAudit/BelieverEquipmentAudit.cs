using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PMP_Persona.Romulus;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Tests {
 public sealed class BelieverEquipmentAudit:GameComponent {
  int frames,stage,tickStart,hpBefore; bool done; Pawn shooter; Thing target;
  readonly HashSet<int> projectiles=new();
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"BelieverAudit");
  public BelieverEquipmentAudit(Game game){}
  void Check(bool ok,string label){if(!ok)throw new Exception(label);File.AppendAllText(Path.Combine(Folder,"results.txt"),"PASS "+label+"\n");}
  void Hold(Pawn p){p.jobs.StopAll();var job=JobMaker.MakeJob(JobDefOf.Wait_Combat);job.expiryInterval=60000;p.jobs.StartJob(job,JobCondition.InterruptForced);}
  Pawn Spawn(string kind,IntVec3 cell){var p=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind),Faction.OfPlayer);GenSpawn.Spawn(p,cell,Find.CurrentMap);p.drafter.Drafted=true;Hold(p);return p;}
  public override void GameComponentTick(){
   if(stage!=2||done)return;
   foreach(var p in Find.CurrentMap.listerThings.AllThings.OfType<Projectile>())if(p.def.defName=="PMP_BelieverSpearShot")projectiles.Add(p.thingIDNumber);
  }
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-believer-audit")||done||Find.CurrentMap==null)return;
   Directory.CreateDirectory(Folder);
   try {
    if(stage==0&&++frames>=90){
     Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
     foreach(var name in new[]{"PMP_BelieverArmor","PMP_BelieverHelmet","PMP_BelieverSpearRifle","PMP_BelieverSpearShot"}){
      var d=DefDatabase<ThingDef>.GetNamed(name);var errors=d.ConfigErrors().ToList();Check(errors.Count==0,name+" ConfigErrors: "+string.Join(";",errors));
      Check(d.techLevel==TechLevel.Archotech||name.EndsWith("Shot"),name+" archotech tier");
      if(name.EndsWith("Shot"))continue;
      Check(d.recipeMaker==null,name+" no unintended crafting recipe");
      var thing=ThingMaker.MakeThing(d);Check(thing.Graphic.MatSingle.mainTexture!=null,name+" inventory graphic");
     }
     var map=Find.CurrentMap;
     foreach(var cell in CellRect.CenteredOn(new IntVec3(50,0,50),24)){
      if(!cell.InBounds(map))continue;
      foreach(var t in cell.GetThingList(map).ToList())if(!(t is Pawn)){if(t.def.destroyable)t.Destroy(DestroyMode.Vanish);else t.DeSpawn();}
      map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
     }
     foreach(var kind in new[]{"PMP_OutpostGuard","PMP_M3Believer"}){
      var pawn=Spawn(kind,new IntVec3(35,0,45));
      Check(pawn.equipment.Primary?.def.defName=="PMP_BelieverSpearRifle",kind+" generated with spear rifle");
      foreach(var name in new[]{"PMP_BelieverArmor","PMP_BelieverHelmet"})Check(pawn.apparel.WornApparel.Count(a=>a.def.defName==name)==1,kind+" wears "+name+"; actual="+string.Join(",",pawn.apparel.WornApparel.Select(a=>a.def.defName)));
      Check(!pawn.apparel.WornApparel.Any(a=>a.def.defName=="Apparel_FlakVest"||a.def.defName=="Apparel_SimpleHelmet"),kind+" placeholder apparel removed");
      Check(pawn.apparel.WornApparel.Count==2,kind+" integrated suit has no protruding undershirt or pants");
      Check(LegionDoctrine.Record(pawn)?.Advanced==true,kind+" existing merit eligibility");
      pawn.DeSpawn();
     }
     var bodyNames=new[]{"Male","Female","Thin","Fat","Hulk"};
     Graphic sharedArmorGraphic=null;
     var rots=new[]{Rot4.South,Rot4.East,Rot4.North,Rot4.West};
     for(int col=0;col<bodyNames.Length;col++)for(int row=0;row<rots.Length;row++){
      var p=Spawn("PMP_M3Believer",new IntVec3(42+col*4,0,58-row*4));
      p.story.bodyType=DefDatabase<BodyTypeDef>.GetNamed(bodyNames[col]);p.Rotation=rots[row];p.Drawer.renderer.SetAllGraphicsDirty();
      var armor=p.apparel.WornApparel.Single(a=>a.def.defName=="PMP_BelieverArmor");
      var head=p.apparel.WornApparel.Single(a=>a.def.defName=="PMP_BelieverHelmet");
      foreach(var a in new[]{armor,head}){
       Check(ApparelGraphicRecordGetter.TryGetGraphicApparel(a,p.story.bodyType,false,out var record),bodyNames[col]+" native apparel graphic "+a.def.defName);
       Check(record.graphic.MatAt(rots[row]).mainTexture!=null,bodyNames[col]+" "+rots[row]+" rendered material "+a.def.defName);
       if(a==armor){
        Check(record.graphic.path.EndsWith("BelieverArmor_Male"),bodyNames[col]+" uses canonical shared armor texture path");
        sharedArmorGraphic??=record.graphic;
        Check(ReferenceEquals(sharedArmorGraphic,record.graphic),bodyNames[col]+" reuses same cached armor graphic");
        Check(p.story.bodyType.defName==bodyNames[col],bodyNames[col]+" wearer's actual body type unchanged");
       }
       Check(Math.Abs(a.GetStatValue(StatDefOf.ArmorRating_Sharp)-1.35f)<.001f,a.def.defName+" Normal sharp armor 135 percent");
      }
     }
     var gunDef=DefDatabase<ThingDef>.GetNamed("PMP_BelieverSpearRifle");var shot=gunDef.Verbs[0].defaultProjectile.projectile;
     var vanillaArmor=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_PowerArmor"));
     Check(ApparelGraphicRecordGetter.TryGetGraphicApparel(vanillaArmor,BodyTypeDefOf.Fat,false,out var vanillaRecord)&&vanillaRecord.graphic.path.EndsWith("_Fat"),"ordinary apparel retains vanilla body-specific texture selection");
     Check(shot.damageDef==DamageDefOf.Bullet&&Math.Abs(shot.GetArmorPenetration(ThingMaker.MakeThing(gunDef))-1.30f)<.001f,"standard bullet damage with 130 percent AP");
     Check(gunDef.tools.Single().armorPenetration==1f,"melee point 100 percent AP");
     Check(gunDef.Verbs[0].burstShotCount==3,"three-shot native burst");
     Check(DefDatabase<ThingDef>.GetNamed("PMP_BelieverArmor").apparel.renderSkipFlags?.Any(f=>f.defName=="Body")!=true,"armor does not hide its own render branch");
     Check(DefDatabase<ThingDef>.GetNamed("PMP_BelieverArmor").label=="信徒动力甲","Chinese localization loaded");
     Find.CameraDriver.SetRootPosAndSize(new Vector3(50,0,52),9);
     stage=1;frames=0;
    } else if(stage==1){
     if(++frames==20)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"01-body-types-directions.png"));
     if(frames<40)return;
     shooter=Spawn("PMP_M3Believer",new IntVec3(42,0,37));
     target=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);GenSpawn.Spawn(target,new IntVec3(54,0,37),Find.CurrentMap);hpBefore=target.HitPoints;
     var verb=shooter.equipment.Primary.GetComp<CompEquippable>().PrimaryVerb;
     Check(verb.TryStartCastOn(target),"native spear shooting starts");
     Find.CameraDriver.SetRootPosAndSize(new Vector3(48,0,37),5);
     tickStart=Find.TickManager.TicksGame;Find.TickManager.CurTimeSpeed=TimeSpeed.Fast;stage=2;frames=0;
    } else if(stage==2&&Find.TickManager.TicksGame-tickStart>240){
     Check(projectiles.Count>=3,"native burst launched at least three projectiles: "+projectiles.Count);
     Check(target.Destroyed||target.HitPoints<hpBefore,"real projectile impact damaged target");
     Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;stage=3;frames=0;
     File.WriteAllText(Path.Combine(Folder,"complete.txt"),"Believer equipment generation, all body types and directions, stats, localization, and native shooting passed.");
    } else if(stage==3){
     if(++frames==10)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"02-native-shooting.png"));
     if(frames>=25){done=true;Application.Quit();}
    }
   }catch(Exception e){done=true;File.WriteAllText(Path.Combine(Folder,"failure.txt"),e.ToString());Log.Error(e.ToString());Application.Quit();}
  }
 }
}
