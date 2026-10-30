using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Editor;
using UnityEngine;
using Verse;
namespace PMP.Tests {
 [StaticConstructorOnStartup] public static class EditorMapRepairBoot {
  static EditorMapRepairBoot(){if(GenCommandLine.CommandLineArgPassed("pmp-editor-map-repair-audit")||GenCommandLine.CommandLineArgPassed("pmp-map-defense-audit"))new Harmony("pmp.editor-map-repair-audit").PatchAll();}
 }
 [HarmonyPatch(typeof(RimWorld.MainMenuDrawer),nameof(RimWorld.MainMenuDrawer.MainMenuOnGUI))]
 static class EditorMapRepairStart {
  static bool started;
  static void Postfix(){if(started||!GenCommandLine.CommandLineArgPassed("pmp-editor-map-repair-audit"))return;started=true;
   try{EditorMapRepairAudit.Start();}catch(Exception e){EditorMapRepairAudit.Fail(e);}
  }
 }
 [HarmonyPatch(typeof(Workspace),nameof(Workspace.DoWindowContents))]
 public static class EditorMapRepairAudit {
  static Workspace w;static Traverse t;static int frames,stage;static List<string> originals;
  public static string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"EditorMapRepairAudit");
  static void Call(string method,params object[] args)=>t.Method(method,args).GetValue();
  static void Check(bool ok,string msg){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+msg+"\n");if(!ok)throw new Exception(msg);}
  static void Select(IEnumerable<string> ids){var set=new HashSet<string>(ids);t.Field("selection").SetValue(set);t.Field("selectedId").SetValue(set.LastOrDefault()??"");}
  public static void Start(){
   Directory.CreateDirectory(Folder);var input=Environment.GetCommandLineArgs().First(a=>a.StartsWith("-rc-audit-project=")).Substring(18);
   w=new Workspace();t=Traverse.Create(w);t.Field("history").SetValue(new History<Project>(ProjectFiles.Load(input)));t.Field("mapId").SetValue("romulus-sanctuary");
   Json.Save(Path.Combine(Folder,"defs.json"),DefDatabase<ThingDef>.AllDefsListForReading.ToDictionary(d=>d.defName,d=>new DefInfo{X=d.size.x,Z=d.size.z,Category=d.category.ToString(),Passability=d.passability.ToString(),Package=d.modContentPack?.PackageId,Fill=d.fillPercent,Rotatable=d.rotatable}));
   originals=w.Map.Objects.Where(p=>p.Kind!="Terrain"&&p.Kind!="Roof"&&p.Position.X>=225&&p.Position.X<=248&&p.Position.Z>=144&&p.Position.Z<=151).Select(p=>p.Id).ToList();
   Select(originals);Call("Copy");Find.WindowStack.Add(w);File.WriteAllText(Path.Combine(Folder,"timings.tsv"),"operation\tms\tallocated_bytes\tobjects\n");
  }
  static void Postfix(Workspace __instance){
   if(w==null||__instance!=w||Event.current.type!=EventType.Repaint||stage==99)return;
   try{if(++frames<25)return;frames=0;
    if(stage<12){Select(originals);long bytes=GC.GetAllocatedBytesForCurrentThread();var timer=Stopwatch.StartNew();
     File.AppendAllText(Path.Combine(Folder,"trace.txt"),"before paste "+stage+"\n");Call("Paste");timer.Stop();
     File.AppendAllText(Path.Combine(Folder,"timings.tsv"),"paste"+stage+"\t"+timer.Elapsed.TotalMilliseconds+"\t"+(GC.GetAllocatedBytesForCurrentThread()-bytes)+"\t"+originals.Count+"\n");
     Check(t.Field("selection").GetValue<HashSet<string>>().Count==originals.Count,"group paste preserves member count "+stage);stage++;return;
    }
    if(stage==12){Call("UndoEdit",false);Call("UndoEdit",true);Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no native editor errors after paste/undo/redo");RunEditTests();ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"editor.png"));stage++;return;}
    Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no errors after regression cases and subsequent render");
    File.WriteAllText(Path.Combine(Folder,"complete.txt"),"PASS");stage=99;Application.Quit();
   }catch(Exception e){Fail(e);}
  }
  public static void Fail(Exception e){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"failure.txt"),e.ToString());stage=99;Application.Quit();}
  public class DefInfo { public int X{get;set;}public int Z{get;set;}public string Category{get;set;}public string Passability{get;set;}public string Package{get;set;}public float Fill{get;set;}public bool Rotatable{get;set;} }
  static CellRect Bounds(Placement p)=>GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),DefDatabase<ThingDef>.GetNamed(p.Def.Name).size);
  static Vector2 Center(Placement p){var b=Bounds(p);return new Vector2((b.minX+b.maxX)*.5f,(b.minZ+b.maxZ)*.5f);}
  static void RunEditTests(){
   var saved=t.Field("history").GetValue<History<Project>>();var savedMap=w.Map.Id;
   void Setup(List<Placement> objects){t.Field("history").SetValue(new History<Project>(new Project{Maps=new List<MapSpec>{new MapSpec{Id="audit",Width=100,Height=100,Objects=objects}}}));t.Field("mapId").SetValue("audit");t.Field("rotationSignature").SetValue(null);Select(objects.Select(p=>p.Id));}
   Placement P(string def,int x,int z,int rot=0)=>new Placement{Id=Guid.NewGuid().ToString("N"),Def=new DefRef{Name=def},Position=new Cell{X=x,Z=z},Rotation=rot};
   foreach(var size in new[]{new IntVec2(1,2),new IntVec2(2,2),new IntVec2(2,3),new IntVec2(3,3)}){
    var def=DefDatabase<ThingDef>.AllDefsListForReading.First(d=>d.category==ThingCategory.Building&&d.size==size);
    for(int initial=0;initial<4;initial++){
     Setup(new List<Placement>{P(def.defName,40,40,initial)});var before=Json.Write(w.Map.Objects);var center=Center(w.Map.Objects[0]);
     for(int i=0;i<4;i++){Call("RotateSelection",false);var now=Center(w.Map.Objects[0]);Check(Math.Abs(now.x-center.x)<=.5f&&Math.Abs(now.y-center.y)<=.5f,"single footprint center "+size+" initial="+initial+" step="+i);}
     Check(Json.Write(w.Map.Objects)==before,"four single rotations return exactly "+size+" initial="+initial);
    }
   }
   var group=new List<Placement>{P("Bed",30,40,1),P("Turret_AutoChargeBlaster",41,36),P("PMP_RomulusSpearTurret",35,45)};
   Setup(Json.Copy(group));var rects=group.Select(Bounds).ToList();var pivot=new Vector2((rects.Min(b=>b.minX)+rects.Max(b=>b.maxX))*.5f,(rects.Min(b=>b.minZ)+rects.Max(b=>b.maxZ))*.5f);
   Call("RotateSelection",false);var once=Json.Write(w.Map.Objects);
   foreach(var p in w.Map.Objects){var delta=Center(group.Single(o=>o.Id==p.Id))-pivot;var expected=pivot+new Vector2(delta.y,-delta.x);var actual=Center(p);Check(Math.Abs(actual.x-expected.x)<=.5&&Math.Abs(actual.y-expected.y)<=.5,"group rotates around whole footprint union center");}
   Call("UndoEdit",false);Check(Json.Write(w.Map.Objects)==Json.Write(group),"group rotation undo");Call("UndoEdit",true);Check(Json.Write(w.Map.Objects)==once,"group rotation redo");
   Setup(Json.Copy(group));Select(group.Select(p=>p.Id).Reverse());Call("RotateSelection",false);Check(Json.Write(w.Map.Objects)==once,"group pivot independent of last-selected object");
   for(int i=0;i<3;i++)Call("RotateSelection",false);Check(Json.Write(w.Map.Objects)==Json.Write(group),"four group turns return exactly");
   Setup(Json.Copy(group));for(int i=0;i<4;i++){Call("RotateSelection",true);foreach(var p in w.Map.Objects){var delta=Center(p)-Center(group.Single(o=>o.Id==p.Id));Check(Math.Abs(delta.x)<=.5&&Math.Abs(delta.y)<=.5,"Shift+R keeps each footprint center");}}
   Check(Json.Write(w.Map.Objects)==Json.Write(group),"four individual turns return exactly");
   Setup(new List<Placement>{P("Bed",0,0)});var edge=Json.Write(w.Map.Objects);Call("RotateSelection",false);Check(Json.Write(w.Map.Objects)==edge,"out-of-bounds group is rejected atomically");
   var pawn=P("PMP_M3Believer",50,50);pawn.Kind="PawnKind";pawn.Def.Kind="PawnKindDef";var marker=P("",55,55);marker.Kind="Marker";var ground=P("Soil",52,52);ground.Kind="Terrain";
   Setup(new List<Placement>{P("Wall",45,45),pawn,marker,ground});Call("SetSelectionFaction","Hostile");Check(w.Map.Objects.Take(2).All(p=>p.Faction=="Hostile")&&w.Map.Objects.Skip(2).All(p=>p.Faction=="Player"),"batch faction edits building/pawn and skips marker/terrain");
   Check(t.Field("history").GetValue<History<Project>>().UndoCount==1,"batch faction is one undo entry");Call("UndoEdit",false);Check(w.Map.Objects.All(p=>p.Faction=="Player"),"batch faction undo");Call("UndoEdit",true);Call("SetSelectionFaction","missing");Check(w.Map.Objects[0].Faction=="Hostile","unknown faction ignored");
   // A nature object removed by paste must be restored by scoped undo.
   var tree=P("Plant_TreeOak",11,11);tree.Layer="InitializedNature";Setup(new List<Placement>{P("Wall",10,10),tree});Select(new[]{w.Map.Objects[0].Id});Call("Copy");var natureBefore=Json.Write(w.Map.Objects);Call("Paste");Check(!w.Map.Objects.Any(p=>p.Id==tree.Id),"wall paste clears tree at destination");Call("UndoEdit",false);Check(Json.Write(w.Map.Objects)==natureBefore,"paste undo restores affected nature and object order");Call("UndoEdit",true);Check(!w.Map.Objects.Any(p=>p.Id==tree.Id),"paste redo clears nature again");
   Setup(new List<Placement>{P("Plant_TreeOak",20,20)});Call("Copy");Call("Paste");Check(w.Map.Objects.Count==2,"pasting nature does not erase the pasted object");Call("RotateSelection",false);Check(w.Map.Objects.Count==2,"rotating nature does not erase the selected object");
   t.Field("history").SetValue(saved);t.Field("mapId").SetValue(savedMap);Select(originals);Call("InvalidateScene",true);
  }
 }
}
