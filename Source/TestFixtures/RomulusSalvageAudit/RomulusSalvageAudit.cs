using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using PMP_Persona.Romulus;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests
{
 public sealed class RomulusSalvageAudit:GameComponent
 {
  int frames,stage; bool done;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"RomulusSalvageAudit");
  public RomulusSalvageAudit(Game game){}
  void Check(bool ok,string text){if(!ok)throw new Exception(text);File.AppendAllText(Path.Combine(Folder,"results.txt"),"PASS "+text+"\n");}
  ThingDef Fragment=>DefDatabase<ThingDef>.GetNamed("PMP_ArchotechArmorFragment");
  IEnumerable<PawnRenderNode> Nodes(PawnRenderNode node){if(node==null)yield break;yield return node;foreach(var child in node.children??Array.Empty<PawnRenderNode>())foreach(var descendant in Nodes(child))yield return descendant;}
  void CheckChampionRendering(Pawn pawn,Rot4 facing){
   var tree=pawn.Drawer.renderer.renderTree;tree.EnsureInitialized(PawnRenderFlags.None);
   var nodes=Nodes(tree.rootNode).ToList();
   var armor=nodes.Single(n=>n.apparel?.def.defName=="PMP_ChampionArmor");
   var helmet=nodes.Single(n=>n.apparel?.def.defName=="PMP_ChampionHelmet");
   var parms=new PawnDrawParms{pawn=pawn,facing=facing};
   var size=armor.Worker.ScaleFor(armor,parms);var headSize=helmet.Worker.ScaleFor(helmet,parms);
   Check(Mathf.Approximately(size.x,1.5f)&&Mathf.Approximately(size.z,1.5f),"champion worn armor scale 1.5 x 1.5 "+facing);
   Check(Mathf.Approximately(headSize.x,1f)&&Mathf.Approximately(headSize.z,1f),"champion helmet retains original scale "+facing);
   var offset=armor.Worker.OffsetFor(armor,parms,out _);
   Check(Mathf.Approximately(offset.z,-.15f),"champion armor downward offset 0.15 "+facing+" "+offset);
   Check(facing==Rot4.East?offset.x<-.13f:facing==Rot4.West?offset.x>.13f:Mathf.Approximately(offset.x,0),"champion side armor moves backward symmetrically "+facing);
   float bodyLayer=armor.Worker.LayerFor(armor,parms),headLayer=helmet.Worker.LayerFor(helmet,parms);
   Check(bodyLayer<headLayer,"helmet above armor "+facing+" (armor="+bodyLayer+", helmet="+headLayer+")");
  }
  int Fragments()=>Find.CurrentMap.listerThings.ThingsOfDef(Fragment).Sum(t=>t.stackCount);
  Pawn Spawn(string kind,Faction faction,IntVec3 cell){var p=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind),faction);GenSpawn.Spawn(p,cell,Find.CurrentMap);p.jobs.StopAll();return p;}
  List<Thing> Gear(Pawn p)=> (p.apparel?.WornApparel.Cast<Thing>()??Enumerable.Empty<Thing>()).Concat(p.equipment?.AllEquipmentListForReading.Cast<Thing>()??Enumerable.Empty<Thing>()).Concat(p.inventory?.innerContainer.Cast<Thing>()??Enumerable.Empty<Thing>()).ToList();
  void Kill(Pawn p){var r=BossMapComponent.For(p);if(r!=null){r.Template.ReviveAfterSeconds=0;r.Health=1;p.Map.GetComponent<BossMapComponent>().ReceiveHit(r,new DamageInfo(DamageDefOf.Blunt,2));}else p.Kill(null);}
  void CheckDeath(string kind,Faction faction,int count,IntVec3 cell){
   var pawn=Spawn(kind,faction,cell);var allGear=Gear(pawn);int before=Fragments();
   var bystanderLoot=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_Autopistol"));GenSpawn.Spawn(bystanderLoot,cell,Find.CurrentMap);
   Kill(pawn);
   Check(pawn.Dead,kind+" true death");Check(pawn.Corpse==null||pawn.Corpse.Destroyed,kind+" no corpse");
   Check(allGear.All(g=>g.Destroyed),kind+" own equipment and inventory vanish");
   Check(!bystanderLoot.Destroyed,kind+" unrelated loot in death cell preserved");
   Check(Fragments()-before==count,kind+" exact fragment drop "+count);
  }
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-romulus-salvage-audit")||done||Find.CurrentMap==null)return;
   Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;Directory.CreateDirectory(Folder);
   try{
    if(stage==0&&++frames>=90){
     var map=Find.CurrentMap;
     foreach(var cell in CellRect.CenteredOn(new IntVec3(50,0,50),30))if(cell.InBounds(map)){
      foreach(var t in cell.GetThingList(map).ToList())if(!(t is Pawn)){if(t.def.destroyable)t.Destroy(DestroyMode.Vanish);else t.DeSpawn();}
      map.roofGrid.SetRoof(cell,null);map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
     }
     foreach(var name in new[]{"PMP_ChampionArmor","PMP_ChampionHelmet","PMP_ArchotechArmorFragment","PMP_LegionChosen"}){
      var d=DefDatabase<ThingDef>.GetNamed(name);Check(!d.ConfigErrors().Any(),name+" native config valid");
      if(name!="PMP_LegionChosen")Check(ThingMaker.MakeThing(d).Graphic.MatSingle.mainTexture!=null,name+" inventory graphic loads");
     }
     Check(Fragment.label=="超凡科技护甲片","Chinese fragment name");
     var faction=FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion")));Find.FactionManager.Add(faction);
     Check(LegionDoctrine.IsLegionFaction(faction),"legion faction explicitly recognized");
     foreach(var kind in new[]{"PMP_ChampionWarrior","PMP_SanctuaryChampion"}){
      var p=Spawn(kind,faction,new IntVec3(38,0,38));
      Check(p.gender==Gender.Male,kind+" male only");
      Check(p.apparel.WornApparel.Any(a=>a.def.defName=="PMP_ChampionArmor")&&p.apparel.WornApparel.Any(a=>a.def.defName=="PMP_ChampionHelmet"),kind+" wears approved armor and helmet");
      var r=BossMapComponent.For(p);Check(r!=null&&r.Template.ReviveAfterSeconds==0,kind+" always regular boss with no infinite resurrection");
      int before=Fragments();var gear=Gear(p);p.Kill(null);
      Check(!p.Dead&&Fragments()==before&&gear.All(g=>!g.Destroyed),kind+" blocked direct kill does not destroy gear or create drops");
      r.Template.ReviveAfterSeconds=1;r.Health=1;map.GetComponent<BossMapComponent>().ReceiveHit(r,new DamageInfo(DamageDefOf.Blunt,2));
      Check(!p.Dead&&r.FallenUntil>=0&&Fragments()==before&&gear.All(g=>!g.Destroyed),kind+" temporary defeat does not create loot");
      r.FallenUntil=Find.TickManager.TicksGame;map.GetComponent<BossMapComponent>().MapComponentTick();
      Check(!p.Dead&&r.FallenUntil<0&&Fragments()==before,kind+" revival creates no fragments");
      Kill(p);Check(p.Dead&&Fragments()-before==8,kind+" eventual true death drops eight once");
     }
     foreach(var entry in new[]{("PMP_OutpostGuard",1),("PMP_M3Believer",1),("PMP_LegionCenturion",2),("PMP_Warhound",0),("PMP_LegionGuard",3),("PMP_LegionChosen",5),("PMP_ChampionWarrior",8)})
      CheckDeath(entry.Item1,faction,entry.Item2,new IntVec3(32,0,32));
     var pirates=Find.FactionManager.FirstFactionOfDef(FactionDefOf.Pirate);
     var outsider=Spawn("PMP_M3Believer",pirates,new IntVec3(30,0,34));int outsideBefore=Fragments();var outsiderGear=Gear(outsider);outsider.Kill(null);
     Check(outsider.Corpse!=null&&!outsider.Corpse.Destroyed&&Fragments()==outsideBefore&&outsiderGear.Any(g=>!g.Destroyed),"unregistered pirate faction keeps normal loot even with legion gear");
     var campaignFaction=FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(FactionDefOf.Pirate));Find.FactionManager.Add(campaignFaction);
     Current.Game.GetComponent<LegionFactionRegistry>().Register(campaignFaction);
     CheckDeath("PMP_M3Believer",campaignFaction,1,new IntVec3(30,0,36));
     foreach(var kind in new[]{"PMP_LegionGuard","PMP_LegionChosen"}){
      var player=Spawn(kind,Faction.OfPlayer,new IntVec3(30,0,40));var gear=Gear(player);int before=Fragments();player.Kill(null);
      Check(player.Dead&&player.Corpse!=null&&!player.Corpse.Destroyed&&Fragments()==before,kind+" player mech leaves normal corpse and no fragment refund");
      Check(gear.All(g=>!g.Destroyed),kind+" player equipment preserved");
     }
     var wearer=Spawn("PMP_M3Believer",Faction.OfPlayer,new IntVec3(30,0,43));wearer.gender=Gender.Female;
     foreach(var a in wearer.apparel.WornApparel.ToList()){wearer.apparel.Remove(a);a.Destroy(DestroyMode.Vanish);}
     foreach(var name in new[]{"PMP_ChampionArmor","PMP_ChampionHelmet"})wearer.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name)));
     Check(wearer.gender==Gender.Female&&BossMapComponent.For(wearer)==null,"wearing champion armor does not change gender or create a boss");
     var playerGear=Gear(wearer);int playerBefore=Fragments();wearer.Kill(null);
     Check(wearer.Corpse!=null&&!wearer.Corpse.Destroyed&&Fragments()==playerBefore&&playerGear.All(g=>!g.Destroyed),"ordinary colonist with champion gear keeps normal death behavior");
     var legacy=Spawn("PMP_ChampionWarrior",faction,new IntVec3(35,0,43));var legacyRecord=BossMapComponent.For(legacy);legacyRecord.Health=12345;
     legacy.DeSpawn();legacy.gender=Gender.Female;
     foreach(var a in legacy.apparel.WornApparel.ToList()){legacy.apparel.Remove(a);a.Destroy(DestroyMode.Vanish);}
     foreach(var name in new[]{"Apparel_PowerArmor","Apparel_PowerArmorHelmet"}){var a=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name));a.TryGetComp<CompQuality>().SetQuality(QualityCategory.Excellent,ArtGenerationContext.Outsider);a.HitPoints=a.MaxHitPoints/2;legacy.apparel.Wear(a);}
     GenSpawn.Spawn(legacy,new IntVec3(35,0,43),map,Rot4.South,WipeMode.Vanish,true);
     Check(legacy.gender==Gender.Male&&legacy.apparel.WornApparel.All(a=>a.def.defName.StartsWith("PMP_Champion")),"legacy champion migrates male identity and old marine gear on load");
     Check(legacy.apparel.WornApparel.All(a=>a.TryGetQuality(out var q)&&q==QualityCategory.Excellent&&Math.Abs(a.HitPoints/(float)a.MaxHitPoints-.5f)<.02),"legacy gear quality and damage retained");
     Check(BossMapComponent.For(legacy)==legacyRecord&&legacyRecord.Health==12345,"legacy migration preserves boss encounter state");
     Check(!DefDatabase<RecipeDef>.AllDefs.Any(r=>r.products?.Any(p=>p.thingDef.defName=="PMP_ChampionArmor"||p.thingDef.defName=="PMP_ChampionHelmet")==true),"unconfirmed champion crafting recipes remain disabled");
     Check(DefDatabase<PawnKindDef>.GetNamed("PMP_ChampionWarrior").race==ThingDefOf.Human,"champion is a human boss, not a manufacturable mech");
     var visualKinds=new[]{"PMP_ChampionWarrior","PMP_LegionChosen"};var rotations=new[]{Rot4.South,Rot4.East,Rot4.North,Rot4.West};
     for(int row=0;row<2;row++)for(int col=0;col<4;col++){
      var p=Spawn(visualKinds[row],faction,new IntVec3(44+col*3,0,60-row*6));p.Rotation=rotations[col];
      if(row==0){var armor=p.apparel.WornApparel.Single(a=>a.def.defName=="PMP_ChampionArmor");
       foreach(var body in new[]{BodyTypeDefOf.Male,BodyTypeDefOf.Female,BodyTypeDefOf.Thin,BodyTypeDefOf.Fat,BodyTypeDefOf.Hulk})
        Check(ApparelGraphicRecordGetter.TryGetGraphicApparel(armor,body,false,out var graphic)&&graphic.graphic.path.EndsWith("ChampionArmor_Male"),"champion armor shared texture "+body.defName+" "+rotations[col]);
      }
      p.Drawer.renderer.SetAllGraphicsDirty();
      if(row==0)CheckChampionRendering(p,rotations[col]);
     }
     // Presentation only: hide stacked test-boss HUDs after all behavioral checks.
     foreach(var boss in map.GetComponent<BossMapComponent>().Records)boss.Active=false;
     Find.CameraDriver.config.sizeRange=new FloatRange(3,60);
     Find.CameraDriver.SetRootPosAndSize(new Vector3(49,0,60),3.5f);stage=1;frames=0;
     File.WriteAllText(Path.Combine(Folder,"complete.txt"),"Native equipment, male bosses, legacy migration, scoped true-death salvage, player exclusions and shared graphics passed.");
    }else if(stage==1){
     if(++frames==20)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"champion-and-chosen.png"));
     if(frames>=40){done=true;Application.Quit();}
    }
   }catch(Exception e){done=true;File.WriteAllText(Path.Combine(Folder,"failure.txt"),e.ToString());Log.Error(e.ToString());Application.Quit();}
  }
 }
}
