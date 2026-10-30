using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona.Romulus
{
    public sealed class CompProperties_WallVault : CompProperties
    {
        public int vaultTicks = 22;
        public float arcHeight = .4f;
        public CompProperties_WallVault() { compClass = typeof(CompWallVault); }
    }
    // Private graph: ground edges plus cardinal two-cell edges over a single wall. Never edit shared path grids.
    public sealed class CompWallVault : ThingComp
    {
        public CompProperties_WallVault Settings => (CompProperties_WallVault)props;
        public Pawn Pawn => (Pawn)parent;
        public bool Vaulting => Pawn.Spawned && Pawn.pather.Moving &&
            (Pawn.pather.nextCell-Pawn.Position).LengthHorizontalSquared == 4;
        internal static readonly IntVec3[] Directions = { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West };
        private IntVec3 cachedStart, cachedTarget;
        private PathEndMode cachedMode;
        private int cacheUntil;
        private List<IntVec3> cachedPath;
        public bool WallAt(IntVec3 cell)
        {
            if (!cell.InBounds(Pawn.Map)) return false;
            var b = cell.GetEdifice(Pawn.Map);
            return b != null && !(b is Building_Door) && b.def.size == IntVec2.One &&
                b.def.passability == Traversability.Impassable && b.def.fillPercent >= .99f;
        }
        internal bool Ground(IntVec3 cell)
        {
            if (!cell.InBounds(Pawn.Map) || !cell.WalkableBy(Pawn.Map, Pawn)) return false;
            var door = cell.GetDoor(Pawn.Map);
            return door == null || door.FreePassage || door.PawnCanOpen(Pawn);
        }
        public List<IntVec3> FindPath(LocalTargetInfo target, PathEndMode mode, bool useCache = true)
        {
            if (!Pawn.Spawned || !target.IsValid || target.HasThing && target.Thing.Map != Pawn.Map) return null;
            int now = Find.TickManager.TicksGame;
            var member=Pawn.GetComp<CompPackMember>();
            if (now < cacheUntil && cachedStart == Pawn.Position && cachedTarget == target.Cell && cachedMode == mode)
            {
                if(useCache)return cachedPath;
                if(member?.Managed==true&&cachedPath!=null&&cachedPath.Count<=32)
                {
                    bool valid=true;for(int i=1;i<cachedPath.Count;i++)if(!ValidEdge(cachedPath[i],cachedPath[i-1])){valid=false;break;}
                    if(valid){if(PackMetrics.Enabled)PackMetrics.LocalHits++;return cachedPath;}
                }
            }
            cachedStart = Pawn.Position; cachedTarget = target.Cell; cachedMode = mode; cacheUntil = now+30; cachedPath = null;
            if(member?.Managed==true)cachedPath=member.Owner.SharedPath(member,this,target,mode);
            cachedPath ??= FindPathRaw(target,mode);
            if(cachedPath==null&&member?.Managed==true&&target.Thing!=null)member.PathFailed(target.Thing);
            return cachedPath;
        }
        internal bool ValidEdge(IntVec3 from,IntVec3 to)
        {
            if(!Ground(to))return false;
            int distance=(from-to).LengthHorizontalSquared;
            return distance==1 || distance==4 && WallAt(new IntVec3((from.x+to.x)/2,0,(from.z+to.z)/2));
        }
        internal List<IntVec3> FindPathRaw(LocalTargetInfo target,PathEndMode mode)
        {
            if(PackMetrics.Enabled)PackMetrics.FullPaths++;
            var frontier = new PriorityQueue<IntVec3, float>();
            var costs = new Dictionary<IntVec3,float> { [Pawn.Position] = 0 };
            var parents = new Dictionary<IntVec3,IntVec3>();
            var closed = new HashSet<IntVec3>();
            float cellTicks = Mathf.Max(1, Pawn.TicksPerMoveCardinal);
            frontier.Enqueue(Pawn.Position, 0);
            while (frontier.Count > 0 && closed.Count < 30000)
            {
                var at = frontier.Dequeue(); if (!closed.Add(at)) continue;
                if(PackMetrics.Enabled)PackMetrics.ExpandedNodes++;
                if (ReachabilityImmediate.CanReachImmediate(at, target, Pawn.Map, mode, Pawn))
                {
                    var path = new List<IntVec3> { at };
                    while (parents.TryGetValue(at, out var prev)) { path.Add(prev); at = prev; }
                    return path;
                }
                foreach (var dir in Directions)
                {
                    IntVec3 to = at+dir; bool vault = false;
                    if (!Ground(to))
                    {
                        if (!WallAt(to)) continue;
                        to += dir; vault = true;
                        if (!Ground(to)) continue;
                    }
                    if (closed.Contains(to)) continue;
                    float cost = costs[at] + (vault ? Settings.vaultTicks : cellTicks + Pawn.Map.pathing.For(Pawn).pathGrid.CalculatedCostAt(to, false, IntVec3.Invalid));
                    if (costs.TryGetValue(to, out float old) && cost >= old) continue;
                    costs[to] = cost; parents[to] = at;
                    // Lower bound remains admissible even when vault is faster than two ground cells.
                    float h = (Math.Abs(to.x-target.Cell.x)+Math.Abs(to.z-target.Cell.z))*Math.Min(cellTicks,Settings.vaultTicks*.5f);
                    frontier.Enqueue(to, cost+h);
                }
            }
            return null;
        }
        public void BeginStep()
        {
            if (!Vaulting) return;
            Pawn.pather.nextCellCostTotal = Settings.vaultTicks;
            Pawn.pather.nextCellCostLeft = Settings.vaultTicks;
            var delta = Pawn.pather.nextCell-Pawn.Position;
            Pawn.Rotation = Rot4.FromIntVec3(new IntVec3(Math.Sign(delta.x),0,Math.Sign(delta.z)));
        }
    }
    internal static class VaultPathAccess
    {
        internal static readonly AccessTools.FieldRef<Pawn_PathFollower,Pawn> Pawn = AccessTools.FieldRefAccess<Pawn_PathFollower,Pawn>("pawn");
        internal static readonly AccessTools.FieldRef<Pawn_PathFollower,bool> Moving = AccessTools.FieldRefAccess<Pawn_PathFollower,bool>("moving");
        internal static readonly AccessTools.FieldRef<Pawn_PathFollower,LocalTargetInfo> Destination = AccessTools.FieldRefAccess<Pawn_PathFollower,LocalTargetInfo>("destination");
        internal static readonly AccessTools.FieldRef<Pawn_PathFollower,PathEndMode> Mode = AccessTools.FieldRefAccess<Pawn_PathFollower,PathEndMode>("peMode");
        private static readonly AccessTools.FieldRef<PawnPath,int> Index = AccessTools.FieldRefAccess<PawnPath,int>("curNodeIndex");
        internal static readonly Action<Pawn_PathFollower> Arrived = AccessTools.MethodDelegate<Action<Pawn_PathFollower>>(AccessTools.Method(typeof(Pawn_PathFollower),"PatherArrived"));
        internal static readonly Action<Pawn_PathFollower> Failed = AccessTools.MethodDelegate<Action<Pawn_PathFollower>>(AccessTools.Method(typeof(Pawn_PathFollower),"PatherFailed"));
        private static readonly Action<Pawn_PathFollower> Setup = AccessTools.MethodDelegate<Action<Pawn_PathFollower>>(AccessTools.Method(typeof(Pawn_PathFollower),"SetupMoveIntoNextCell"));
        internal static void Request(Pawn_PathFollower pather, CompWallVault comp)
        {
            var nodes = comp.FindPath(Destination(pather), Mode(pather), false);
            if (nodes == null) { Failed(pather); return; }
            if (nodes.Count <= 1) { Arrived(pather); return; }
            pather.curPath?.Dispose();
            var path = new PawnPath(); foreach (var node in nodes) path.AddNode(node);
            Index(path) = nodes.Count-1;
            pather.curPath = path;
            pather.lastPathedTargetPosition = Destination(pather).Cell;
            pather.nextCell = comp.Pawn.Position;
            pather.nextCellCostLeft = 0;
            Setup(pather);
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath))]
    static class VaultStartPath
    {
        static bool Prefix(Pawn_PathFollower __instance, LocalTargetInfo dest, PathEndMode peMode)
        {
            var pawn = VaultPathAccess.Pawn(__instance); var comp = pawn.GetComp<CompWallVault>();
            if (comp == null || !pawn.Spawned) return true;
            if (pawn.Downed || pawn.Dead || !dest.IsValid || dest.HasThing && dest.ThingDestroyed) { VaultPathAccess.Failed(__instance); return false; }
            dest = (LocalTargetInfo)GenPath.ResolvePathMode(pawn, dest.ToTargetInfo(pawn.Map), ref peMode);
            if (__instance.Moving && VaultPathAccess.Destination(__instance)==dest && VaultPathAccess.Mode(__instance)==peMode) return false;
            __instance.StopDead();
            VaultPathAccess.Destination(__instance)=dest; VaultPathAccess.Mode(__instance)=peMode;
            VaultPathAccess.Moving(__instance)=true; pawn.jobs.posture=PawnPosture.Standing;
            VaultPathAccess.Request(__instance,comp); return false;
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "SetNewPathRequest")]
    static class VaultRequestPath
    {
        static bool Prefix(Pawn_PathFollower __instance)
        {
            var comp=VaultPathAccess.Pawn(__instance).GetComp<CompWallVault>();
            if(comp==null)return true;
            VaultPathAccess.Request(__instance,comp); return false;
        }
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "SetupMoveIntoNextCell")]
    static class VaultStep { static void Postfix(Pawn ___pawn) => ___pawn.GetComp<CompWallVault>()?.BeginStep(); }
    [HarmonyPatch(typeof(Reachability),nameof(Reachability.CanReach),typeof(IntVec3),typeof(LocalTargetInfo),typeof(PathEndMode),typeof(TraverseParms))]
    static class VaultReachability
    {
        static void Postfix(IntVec3 start,LocalTargetInfo dest,PathEndMode peMode,TraverseParms traverseParams,ref bool __result)
        {
            if(__result)return;
            var comp=traverseParams.pawn?.GetComp<CompWallVault>();
            if(comp!=null&&start==comp.Pawn.Position) __result=comp.FindPath(dest,peMode)!=null;
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.DrawPos),MethodType.Getter)]
    static class VaultDraw
    {
        static void Postfix(Pawn __instance,ref Vector3 __result)
        {
            var comp=__instance.GetComp<CompWallVault>();
            if(comp?.Vaulting!=true)return;
            float t=Mathf.Clamp01(1-__instance.pather.nextCellCostLeft/__instance.pather.nextCellCostTotal);
            var at=Vector3.Lerp(__instance.Position.ToVector3Shifted(),__instance.pather.nextCell.ToVector3Shifted(),t);
            __result.x=at.x;__result.z=at.z+Mathf.Sin(t*Mathf.PI)*comp.Settings.arcHeight;
        }
    }
}
