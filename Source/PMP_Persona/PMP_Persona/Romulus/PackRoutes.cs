using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PMP_Persona.Romulus
{
    internal sealed class PackRoute
    {
        public IntVec3 Goal;
        public PathEndMode Mode;
        public int Until;
        public List<IntVec3> Nodes;
        public readonly Dictionary<IntVec3,int> Index = new();
        public readonly HashSet<IntVec3> Footprint = new();
    }
    public sealed partial class MapComponent_PackCoordinator
    {
        internal List<IntVec3> SharedPath(CompPackMember member,CompWallVault vault,LocalTargetInfo dest,PathEndMode mode)
        {
            if (!member.Managed || member.Target == null || dest.Thing != member.Target ||
                member.Pawn.Position.DistanceToSquared(dest.Cell) <= 64) return null;
            var group=member.Group;int now=Find.TickManager.TicksGame;
            if (!group.Routes.TryGetValue(member.Target,out var route) || route.Goal!=dest.Cell || route.Mode!=mode || now>=route.Until)
            {
                // The first member requesting a long approach leads this corridor. A guide already in melee
                // would produce a tiny suffix that distant followers cannot join.
                var guide=member;
                var guideVault=guide.Pawn.GetComp<CompWallVault>();if(guideVault==null)return null;
                var nodes=guideVault.FindPathRaw(dest,mode);
                route=new PackRoute{Goal=dest.Cell,Mode=mode,Until=now+90,Nodes=nodes};
                group.Routes[member.Target]=route;
                if(nodes==null)return null;
                for(int i=0;i<nodes.Count;i++)
                {
                    route.Index[nodes[i]]=i;route.Footprint.Add(nodes[i]);
                    if(i>0&&(nodes[i]-nodes[i-1]).LengthHorizontalSquared==4)
                        route.Footprint.Add(new IntVec3((nodes[i].x+nodes[i-1].x)/2,0,(nodes[i].z+nodes[i-1].z)/2));
                }
            }
            if(route.Nodes==null)return null;
            // A bounded local connection to an immutable shared suffix. Each pawn owns its copied path cursor.
            var start=member.Pawn.Position;
            var frontier=new Queue<IntVec3>();var parents=new Dictionary<IntVec3,IntVec3>();var seen=new HashSet<IntVec3>{start};
            frontier.Enqueue(start);int expanded=0;
            while(frontier.Count>0&&expanded++<192)
            {
                var at=frontier.Dequeue();if(PackMetrics.Enabled)PackMetrics.JoinNodes++;
                if(route.Index.TryGetValue(at,out var index))
                {
                    for(int i=index;i>0;i--)if(!vault.ValidEdge(route.Nodes[i],route.Nodes[i-1])){route.Until=0;return null;}
                    var result=new List<IntVec3>(index+16);for(int i=0;i<=index;i++)result.Add(route.Nodes[i]);
                    while(parents.TryGetValue(at,out var previous)){result.Add(previous);at=previous;}
                    if(PackMetrics.Enabled)PackMetrics.SharedHits++;return result;
                }
                foreach(var dir in CompWallVault.Directions)
                {
                    var next=at+dir;
                    if(!vault.Ground(next)){if(!vault.WallAt(next))continue;next+=dir;if(!vault.Ground(next))continue;}
                    if(next.DistanceToSquared(start)>144||!seen.Add(next))continue;
                    parents[next]=at;frontier.Enqueue(next);
                }
            }
            if(PackMetrics.Enabled)PackMetrics.JoinFailures++;return null;
        }
        internal void Invalidate(CellRect changed)
        {
            if(groups.Count==0)return;
            foreach(var group in groups)foreach(var route in group.Routes.Values)
                foreach(var cell in changed)if(route.Footprint.Contains(cell)){route.Until=0;break;}
        }
    }
    [HarmonyPatch(typeof(EdificeGrid),nameof(EdificeGrid.Register))]
    static class PackBuildingAdded
    { static void Postfix(Building ed,Map ___map)=>___map.GetComponent<MapComponent_PackCoordinator>()?.Invalidate(ed.OccupiedRect()); }
    [HarmonyPatch(typeof(EdificeGrid),nameof(EdificeGrid.DeRegister))]
    static class PackBuildingRemoved
    { static void Postfix(Building ed,Map ___map)=>___map.GetComponent<MapComponent_PackCoordinator>()?.Invalidate(ed.OccupiedRect()); }

    // Counters are enabled only by the isolated opt-in comparison audit. No clocks or per-event allocations in ordinary play.
    public static class PackMetrics
    {
        public static bool Enabled;
        public static bool LegacyMode=GenCommandLine.CommandLineArgPassed("pmp-pack-legacy")&&GenCommandLine.CommandLineArgPassed("pmp-m3-profile");
        public static long FullPaths, ExpandedNodes, SharedHits, JoinNodes, JoinFailures, GroupScans, CandidateChecks, VisibilityChecks, Assignments, IndexEntries,LocalHits;
        public static void Reset(){FullPaths=ExpandedNodes=SharedHits=JoinNodes=JoinFailures=GroupScans=CandidateChecks=VisibilityChecks=Assignments=IndexEntries=LocalHits=0;}
    }
}
