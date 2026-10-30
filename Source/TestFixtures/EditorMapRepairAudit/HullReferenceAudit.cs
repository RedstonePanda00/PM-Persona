using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimCampaign.Runtime;
using RimCampaign.Core;
using RimCampaign.Editor;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 public sealed partial class MapDefenseAudit {
  Workspace hullWorkspace;
  void HullReference(CampaignController c){
   if(stage==0){
    Check(ModsConfig.OdysseyActive,"Odyssey enabled for native hull reference");
    var romulus=DefDatabase<ThingDef>.GetNamed("PMP_RomulusRedWall");
    var test=new IntVec3(390,0,350);var directions=new[]{IntVec3.North,IntVec3.East,IntVec3.South,IntVec3.West,new IntVec3(-1,0,1),new IntVec3(1,0,1),new IntVec3(1,0,-1),new IntVec3(-1,0,-1)};
    foreach(var cell in new CellRect(388,348,5,5))foreach(var t in cell.GetThingList(c.Map).ToList())if(t.def.category==ThingCategory.Building)t.Destroy(DestroyMode.Vanish);
    for(int mask=0;mask<256;mask++){
     var walls=new List<Thing>();for(int i=0;i<8;i++)if((mask&(1<<i))!=0)walls.Add(GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.GravshipHull,ThingDefOf.Steel),test+directions[i],c.Map));
     bool native=SectionLayer_GravshipHull.ShouldDrawCornerPiece(test,c.Map,c.Map.terrainGrid,out var nativeType,out var ignored);
     bool ours=HullConnections.TryCorner(test,p=>p.GetEdifice(c.Map)?.def==ThingDefOf.GravshipHull?romulus:p.GetEdifice(c.Map)?.def,p=>false,out var corner);
     Check(native==ours&&(!native||nativeType.ToString()==(corner.Full?"Corner_":"Diagonal_")+(corner.Z>0?"N":"S")+(corner.X>0?"E":"W")),"native topology parity, neighbour mask "+mask);
     foreach(var wall in walls)wall.Destroy(DestroyMode.Vanish);
    }
    c.State.Fibers.Clear();c.RestoreControl();c.Communications.Messages.Clear();c.Map.fogGrid.ClearAllFog();
    var presentation=AccessTools.Property(AccessTools.TypeByName("PMP.Campaign.MissionEffectsMapComponent"),"CurrentPresentation")?.GetValue(null);
    if(presentation!=null)AccessTools.Method(presentation.GetType(),"End").Invoke(presentation,null);
    foreach(var cell in new CellRect(340,325,40,35))foreach(var t in cell.GetThingList(c.Map).ToList())if(t.def.category==ThingCategory.Building||t.def.category==ThingCategory.Plant)t.Destroy(DestroyMode.Vanish);
    var shape=new HashSet<IntVec3>();
    for(int i=0;i<5;i++){shape.Add(new IntVec3(i,0,4));shape.Add(new IntVec3(4,0,i));shape.Add(new IntVec3(8+i,0,i));shape.Add(new IntVec3(16+i,0,0));}
    foreach(var row in new[]{(ThingDefOf.GravshipHull,348),(DefDatabase<ThingDef>.GetNamed("PMP_RomulusRedWall"),338)})
     foreach(var off in shape){var at=new IntVec3(345,0,row.Item2)+off;c.Map.terrainGrid.SetTerrain(at,TerrainDefOf.Concrete);GenSpawn.Spawn(ThingMaker.MakeThing(row.Item1,row.Item1.MadeFromStuff?ThingDefOf.Steel:null),at,c.Map);}
    foreach(var name in new[]{"GravshipHull_Atlas","AngledGravshipHull_northwest","AngledGravshipHull_northeast","AngledGravshipHull_southwest","AngledGravshipHull_southeast","AngledGravshipHull_Partial_northwest","AngledGravshipHull_Partial_northeast","AngledGravshipHull_Partial_southwest","AngledGravshipHull_Partial_southeast"}){
     var tex=ContentFinder<Texture2D>.Get("Things/Building/Linked/GravshipHull/"+name);File.WriteAllBytes(Path.Combine(Folder,name+".png"),tex.EncodeToPNG());
    }
    foreach(var cell in new CellRect(344,347,23,7))if(SectionLayer_GravshipHull.ShouldDrawCornerPiece(cell,c.Map,c.Map.terrainGrid,out var type,out var color))File.AppendAllText(Path.Combine(Folder,"native-corners.txt"),cell+" "+type+"\n");
    c.Map.mapDrawer.RegenerateEverythingNow();Find.CameraDriver.config.sizeRange=new FloatRange(3,60);Find.CameraDriver.SetRootPosAndSize(new Vector3(355,0,345),13);stage=1;frames=0;return;
   }
   if(stage==1&&++frames==40)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"odyssey-reference-vs-current.png"));
   if(stage==1&&frames>=65){
    var removed=new IntVec3(355,0,340).GetEdifice(c.Map);Check(removed!=null,"diagonal middle wall exists");removed.Destroy(DestroyMode.Vanish);
    Check(!HullConnections.TryCorner(c.Map,new IntVec3(354,0,340),out _)&&!HullConnections.TryCorner(c.Map,new IntVec3(355,0,339),out _),"removing diagonal middle wall removes its adjacent joins");stage=2;frames=0;
   }
   if(stage==2&&++frames==30)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"diagonal-wall-removed.png"));
   if(stage==2&&frames>=45){GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_RomulusRedWall")),new IntVec3(355,0,340),c.Map);stage=3;frames=0;}
   if(stage==3&&++frames==30)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"diagonal-wall-restored.png"));
   if(stage==3&&frames>=45){
    Check(HullConnections.TryCorner(c.Map,new IntVec3(354,0,340),out _)&&HullConnections.TryCorner(c.Map,new IntVec3(355,0,339),out _),"rebuilding middle wall restores adjacent joins");
    foreach(var cell in new CellRect(410,330,30,30))foreach(var t in cell.GetThingList(c.Map).ToList())if(t.def.category==ThingCategory.Building||t.def.category==ThingCategory.Plant)t.Destroy(DestroyMode.Vanish);
    var defs=new[]{ThingDefOf.GravshipHull,DefDatabase<ThingDef>.GetNamed("PMP_RomulusRedWall")};
    for(int row=0;row<2;row++)for(int i=0;i<4;i++){
     int x=i%2==0?1:-1,z=i/2==0?1:-1;var center=new IntVec3(413+i*6,0,350-row*10);
     foreach(var d in new[]{new IntVec3(x,0,0),new IntVec3(0,0,z),new IntVec3(x,0,z)})GenSpawn.Spawn(ThingMaker.MakeThing(defs[row],row==0?ThingDefOf.Steel:null),center+d,c.Map);
    }
    c.Map.mapDrawer.RegenerateEverythingNow();Find.CameraDriver.SetRootPosAndSize(new Vector3(422,0,345),12);stage=4;frames=0;
   }
   if(stage==4&&++frames==30)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"four-direction-corners.png"));
   if(stage==4&&frames>=45){
    hullWorkspace=new Workspace();hullWorkspace.Map.Width=32;hullWorkspace.Map.Height=20;hullWorkspace.Map.Name="墙体斜接预览验证";
    void Wall(int x,int z)=>hullWorkspace.Map.Objects.Add(new Placement{Kind="Thing",Def=new DefRef{Name="PMP_RomulusRedWall"},Position=new Cell{X=x,Z=z}});
    for(int i=0;i<4;i++){int x=i%2==0?1:-1,z=i/2==0?1:-1;int cx=5+i*6,cz=14;Wall(cx+x,cz);Wall(cx,cz+z);Wall(cx+x,cz+z);}
    for(int i=0;i<6;i++){Wall(5+i,4+i);Wall(18+i,9-i);}
    Traverse.Create(hullWorkspace).Field("leftOpen").SetValue(false);Traverse.Create(hullWorkspace).Field("rightOpen").SetValue(false);Traverse.Create(hullWorkspace).Field("showAux").SetValue(false);
    Find.WindowStack.Add(hullWorkspace);stage=5;frames=0;
   }
   if(stage==5&&++frames==5)Traverse.Create(Traverse.Create(hullWorkspace).Field("mapView").GetValue()).Method("Fit").GetValue();
   if(stage==5&&frames==30){Check(Traverse.Create(hullWorkspace).Field<List<HullCorner>>("hullPreview").Value.Count==24,"editor previews four full corners and both diagonal directions");ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"editor-hull-preview.png"));}
   if(stage==5&&frames>=45){hullWorkspace.Close();Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native errors during hull rendering, editor preview and rebuild");Finish();}
  }
 }
}
