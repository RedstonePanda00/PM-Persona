using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using Verse;

namespace PMP.Tests.Landscape {
 [StaticConstructorOnStartup] public static class Generate {
  public static readonly string Folder=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("-pmp-landscape-folder="))?.Substring("-pmp-landscape-folder=".Length);
  static void Report(string text)=>File.AppendAllText(Path.Combine(Folder,"generation.txt"),text+"\n");
  static Generate(){
   if(Folder==null||!GenCommandLine.CommandLineArgPassed("pmp-nature-build"))return;
   try {
    var project=Json.Load<Project>(Path.Combine(Folder,"input.project.json"));
    foreach(var map in project.Maps.Skip(1)){
     var authored=map.Objects.ToList();
     var before=Blocked(map,authored);
     TerrainInitializer.RegenerateUnderLayout(map);
     // Keep every authored record, even if an older natural object overlapped a floor.
     var generated=map.Objects.Where(TerrainInitializer.Generated).ToList();
     generated.AddRange(authored);map.Objects=generated;
     var targets=authored.Where(p=>p.Kind=="Marker"||p.Kind=="Pawn"||p.Kind=="PawnKind"||map.Interactions.Any(i=>i.Host==p.Id)).SelectMany(p=>Approach(map,p,before)).Distinct().ToList();
     var after=Blocked(map,map.Objects);int cleared=0,checkedTargets=0;
     var unassigned=new HashSet<int>(targets);
     while(unassigned.Count>0){
      int start=unassigned.First();var original=Flood(map,before,start);
      var component=targets.Where(t=>original.Contains(t)).ToList();foreach(int t in component)unassigned.Remove(t);
      var reachable=Flood(map,after,start);
      foreach(int target in component){
       if(!reachable.Contains(target)){
        var path=RepairPath(map,before,after,start,target);
        var cells=new HashSet<int>();
        foreach(int at in path)for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
         int x=at%map.Width+dx,z=at/map.Width+dz;if(x>=0&&z>=0&&x<map.Width&&z<map.Height&&!before[z*map.Width+x])cells.Add(z*map.Width+x);
        }
        // Stored clearings make a later editor regeneration preserve the same paths.
        foreach(var row in cells.GroupBy(i=>i/map.Width)){
         var xs=row.Select(i=>i%map.Width).OrderBy(x=>x).ToList();int left=xs[0],last=left;
         void Add(){map.NaturalClearings.Add(new RimCampaign.Core.Region{Id="nature-path-"+map.NaturalClearings.Count,Name="Preserved route",Min=new Cell{X=left,Z=row.Key},Max=new Cell{X=last,Z=row.Key}});}
         foreach(int x in xs.Skip(1)){if(x>last+1){Add();left=x;}last=x;}Add();
        }
        int count=map.Objects.Count;TerrainInitializer.ClearNatureAt(map,cells.Select(i=>new Cell{X=i%map.Width,Z=i/map.Width}));cleared+=count-map.Objects.Count;
        after=Blocked(map,map.Objects);reachable=Flood(map,after,start);
        if(!reachable.Contains(target))throw new Exception("Route repair failed "+map.Id+" "+target);
       }
       checkedTargets++;
      }
     }
     var retained=new HashSet<Placement>(map.Objects);
     if(authored.Any(p=>!retained.Contains(p)))throw new Exception("Authored object removed "+map.Id);
     int rocks=map.Objects.Count(p=>p.Layer==TerrainInitializer.NatureLayer&&p.Kind=="Thing"&&DefDatabase<ThingDef>.GetNamed(p.Def.Name).building?.isNaturalRock==true);
     int plants=map.Objects.Count(p=>p.Layer==TerrainInitializer.NatureLayer&&p.Kind=="Thing"&&DefDatabase<ThingDef>.GetNamed(p.Def.Name).category==ThingCategory.Plant);
     if(rocks==0||plants==0)throw new Exception("Missing nature "+map.Id);
     Report(map.Id+": plants="+plants+", rocks="+rocks+", reachable targets="+checkedTargets+", cleared for routes="+cleared+", authored="+authored.Count);
    }
    Json.Save(Path.Combine(Folder,"generated.project.json"),project);
    File.WriteAllText(Path.Combine(Folder,"GENERATED.txt"),"PASS");
   }catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),ex.ToString());Log.Error(ex.ToString());}
  }
  static IEnumerable<int> Approach(MapSpec map,Placement p,bool[] blocked){
   var footprint=TerrainInitializer.Footprint(p).ToList();
   foreach(var cell in footprint)for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++){
    int x=cell.X+dx,z=cell.Z+dz;if(x>=0&&z>=0&&x<map.Width&&z<map.Height&&!blocked[z*map.Width+x])yield return z*map.Width+x;
   }
  }
  static bool[] Blocked(MapSpec map,IEnumerable<Placement> objects){
   var cells=new bool[map.Width*map.Height];
   foreach(var p in objects.Where(p=>p.Kind=="Thing"&&p.SpawnAtStart)){
    var def=DefDatabase<ThingDef>.GetNamed(p.Def.Name);
    if(def.passability!=Traversability.Impassable||typeof(Building_Door).IsAssignableFrom(def.thingClass))continue;
    foreach(var c in TerrainInitializer.Footprint(p))if(c.X>=0&&c.Z>=0&&c.X<map.Width&&c.Z<map.Height)cells[c.Z*map.Width+c.X]=true;
   }return cells;
  }
  static IEnumerable<int> Neighbors(MapSpec map,int i){if(i%map.Width>0)yield return i-1;if(i%map.Width<map.Width-1)yield return i+1;if(i>=map.Width)yield return i-map.Width;if(i<map.Width*(map.Height-1))yield return i+map.Width;}
  static HashSet<int> Flood(MapSpec map,bool[] blocked,int start){
   var seen=new HashSet<int>();if(blocked[start])return seen;var queue=new Queue<int>();seen.Add(start);queue.Enqueue(start);
   while(queue.Count>0)foreach(int n in Neighbors(map,queue.Dequeue()))if(!blocked[n]&&seen.Add(n))queue.Enqueue(n);return seen;
  }
  static List<int> RepairPath(MapSpec map,bool[] before,bool[] after,int start,int target){
   var cost=Enumerable.Repeat(int.MaxValue,before.Length).ToArray();var parent=new int[before.Length];var queue=new PriorityQueue<int,int>();cost[start]=0;queue.Enqueue(start,0);
   while(queue.TryDequeue(out int at,out int priority)){
    if(priority!=cost[at])continue;if(at==target)break;
    foreach(int n in Neighbors(map,at)){if(before[n])continue;int next=priority+(after[n]?100:1);if(next>=cost[n])continue;cost[n]=next;parent[n]=at;queue.Enqueue(n,next);}
   }
   if(cost[target]==int.MaxValue)throw new Exception("Originally disconnected route");var path=new List<int>();for(int at=target;at!=start;at=parent[at])path.Add(at);path.Add(start);return path;
  }
 }
}
