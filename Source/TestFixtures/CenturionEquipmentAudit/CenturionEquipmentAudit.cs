using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests
{
 public sealed class CenturionEquipmentAudit:GameComponent
 {
  int frames,stage;bool done;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"CenturionEquipmentAudit");
  public CenturionEquipmentAudit(Game game){}
  void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Path.Combine(Folder,"results.txt"),"PASS "+message+"\n");}
  IEnumerable<PawnRenderNode> Nodes(PawnRenderNode n){if(n==null)yield break;yield return n;foreach(var c in n.children??Array.Empty<PawnRenderNode>())foreach(var d in Nodes(c))yield return d;}
  Pawn Spawn(Faction faction,IntVec3 cell){var p=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_LegionCenturion"),faction);GenSpawn.Spawn(p,cell,Find.CurrentMap);p.jobs.StopAll();return p;}
  void MarineGear(Pawn p){foreach(var a in p.apparel.WornApparel.ToList()){p.apparel.Remove(a);a.Destroy(DestroyMode.Vanish);}foreach(var name in new[]{"Apparel_PowerArmor","Apparel_PowerArmorHelmet"}){var a=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name));a.TryGetComp<CompQuality>().SetQuality(QualityCategory.Excellent,ArtGenerationContext.Outsider);a.HitPoints=a.MaxHitPoints/2;p.apparel.Wear(a);}}
  void CheckRender(Pawn p,Rot4 rot,BodyTypeDef body){
   p.story.bodyType=body;p.Rotation=rot;p.Drawer.renderer.SetAllGraphicsDirty();
   var tree=p.Drawer.renderer.renderTree;tree.EnsureInitialized(PawnRenderFlags.None);
   var nodes=Nodes(tree.rootNode).ToList();var armor=nodes.Single(n=>n.apparel?.def.defName=="PMP_CenturionArmor");var helmet=nodes.Single(n=>n.apparel?.def.defName=="PMP_CenturionHelmet");
   var parms=new PawnDrawParms{pawn=p,facing=rot};
   Check(armor.Worker.LayerFor(armor,parms)<helmet.Worker.LayerFor(helmet,parms),body.defName+" helmet above cape and armor "+rot);
   Check(ApparelGraphicRecordGetter.TryGetGraphicApparel(armor.apparel,body,false,out var g)&&g.graphic.path.EndsWith("CenturionArmor_Male"),body.defName+" shared body texture "+rot);
   Check(g.graphic.MatAt(rot).mainTexture.width==512&&helmet.PrimaryGraphic.MatAt(rot).mainTexture.width==512,body.defName+" directional textures loaded "+rot);
  }
  public override void GameComponentUpdate(){
   if(done||!GenCommandLine.CommandLineArgPassed("pmp-centurion-equipment-audit")||Find.CurrentMap==null)return;
   Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Directory.CreateDirectory(Folder);
   try{
    if(stage==0&&++frames>=90){
     var map=Find.CurrentMap;
     foreach(var cell in CellRect.CenteredOn(new IntVec3(50,0,55),20))if(cell.InBounds(map)){foreach(var t in cell.GetThingList(map).ToList())if(!(t is Pawn)){if(t.def.destroyable)t.Destroy(DestroyMode.Vanish);else t.DeSpawn();}map.roofGrid.SetRoof(cell,null);map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);}
     foreach(var name in new[]{"PMP_CenturionArmor","PMP_CenturionHelmet"}){var d=DefDatabase<ThingDef>.GetNamed(name);Check(!d.ConfigErrors().Any(),name+" native config valid");Check(ThingMaker.MakeThing(d).Graphic.MatSingle.mainTexture.width==512,name+" inventory icon loaded");Check(!DefDatabase<RecipeDef>.AllDefs.Any(r=>r.products?.Any(p=>p.thingDef==d)==true),name+" unconfirmed recipes remain disabled");}
     var faction=FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion")));Find.FactionManager.Add(faction);
     var rotations=new[]{Rot4.South,Rot4.East,Rot4.North,Rot4.West};
     for(int col=0;col<4;col++){
      var p=Spawn(faction,new IntVec3(44+col*3,0,60));
      Check(p.apparel.WornApparel.Count==2&&p.apparel.WornApparel.Any(a=>a.def.defName=="PMP_CenturionArmor")&&p.apparel.WornApparel.Any(a=>a.def.defName=="PMP_CenturionHelmet"),"new centurion wears complete set "+col);
      foreach(var body in new[]{BodyTypeDefOf.Male,BodyTypeDefOf.Female,BodyTypeDefOf.Thin,BodyTypeDefOf.Fat,BodyTypeDefOf.Hulk})CheckRender(p,rotations[col],body);
      CheckRender(p,rotations[col],BodyTypeDefOf.Male);
     }
     var legacy=Spawn(faction,new IntVec3(35,0,40));legacy.DeSpawn();legacy.gender=Gender.Female;MarineGear(legacy);var old=legacy.apparel.WornApparel.ToList();
     GenSpawn.Spawn(legacy,new IntVec3(35,0,40),map,Rot4.South,WipeMode.Vanish,true);
     Check(legacy.gender==Gender.Female&&legacy.apparel.WornApparel.All(a=>a.def.defName.StartsWith("PMP_Centurion")),"legacy NPC migrates equipment without changing gender");
     Check(old.All(a=>a.Destroyed)&&legacy.apparel.WornApparel.All(a=>a.TryGetQuality(out var q)&&q==QualityCategory.Excellent&&Math.Abs(a.HitPoints/(float)a.MaxHitPoints-.5f)<.02),"legacy quality and durability retained without loose equipment");
     var migrated=legacy.apparel.WornApparel.ToList();legacy.DeSpawn();GenSpawn.Spawn(legacy,new IntVec3(35,0,40),map,Rot4.South,WipeMode.Vanish,true);Check(migrated.All(a=>legacy.apparel.WornApparel.Contains(a)),"migration is idempotent");
     var fragment=DefDatabase<ThingDef>.GetNamed("PMP_ArchotechArmorFragment");int before=map.listerThings.ThingsOfDef(fragment).Sum(t=>t.stackCount);legacy.Kill(null);
     Check(legacy.Dead&&(legacy.Corpse==null||legacy.Corpse.Destroyed)&&migrated.All(a=>a.Destroyed)&&map.listerThings.ThingsOfDef(fragment).Sum(t=>t.stackCount)-before==2,"legion centurion true death removes new gear and drops exactly two fragments");
     var player=Spawn(Faction.OfPlayer,new IntVec3(35,0,43));player.DeSpawn();MarineGear(player);var playerGear=player.apparel.WornApparel.ToList();GenSpawn.Spawn(player,new IntVec3(35,0,43),map,Rot4.South,WipeMode.Vanish,true);Check(playerGear.All(a=>player.apparel.WornApparel.Contains(a)),"player-owned equipment excluded from legacy migration");
     Find.CameraDriver.config.sizeRange=new FloatRange(3,60);Find.CameraDriver.SetRootPosAndSize(new Vector3(49,0,60),3.5f);
     File.WriteAllText(Path.Combine(Folder,"complete.txt"),"Centurion equipment generation, migration, shared textures, four-direction render layers and salvage passed.");stage=1;frames=0;
    }else if(stage==1){if(++frames==20)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"centurion-four-directions.png"));if(frames>=40){done=true;Application.Quit();}}
   }catch(Exception e){done=true;File.WriteAllText(Path.Combine(Folder,"failure.txt"),e.ToString());Log.Error(e.ToString());Application.Quit();}
  }
 }
}
