using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PMP_Persona.Romulus
{
    public sealed class CompProperties_PackMember : CompProperties
    {
        public int maxPackSize = 40, scanInterval = 30, candidateLimit = 16;
        public float acquireRadius = 56, keepRadius = 65, joinRadius = 28;
        public CompProperties_PackMember() { compClass = typeof(CompPackMember); }
    }

    public sealed class CompPackMember : ThingComp
    {
        public CompProperties_PackMember Settings => (CompProperties_PackMember)props;
        public Pawn Pawn => (Pawn)parent;
        internal PackGroup Group;
        public Thing Target { get; internal set; }
        public Pawn Leader => Group?.Leader?.Pawn;
        public int PackId;
        internal MapComponent_PackCoordinator Owner;
        internal int LastFollowTick = -9999;
        internal Thing UnreachableTarget;
        internal int UnreachableUntil;
        internal bool Blocked(Thing target)=>target!=null&&target==UnreachableTarget&&Find.TickManager.TicksGame<UnreachableUntil;
        internal void PathFailed(Thing target){UnreachableTarget=target;UnreachableUntil=Find.TickManager.TicksGame+120;if(Target==target)Target=null;}
        public bool Eligible => !PackMetrics.LegacyMode && Pawn.Spawned && !Pawn.Dead && !Pawn.Downed &&
            Pawn.Faction != null && !Pawn.Faction.IsPlayer && !Pawn.Drafted && !Pawn.InMentalState &&
            Pawn.CurJob?.playerForced != true && Pawn.GetComp<CompCanBeDormant>()?.Awake != false &&
            (Pawn.mindState.duty == null || Pawn.mindState.duty.def == DutyDefOf.Defend || Pawn.mindState.duty.def == DutyDefOf.AssaultColony);
        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            Owner = Pawn.Map.GetComponent<MapComponent_PackCoordinator>(); Owner.Register(this);
        }
        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            Owner?.Unregister(this); Owner = null; base.PostDeSpawn(map, mode);
        }
        public override void PostExposeData() { base.PostExposeData(); Scribe_Values.Look(ref PackId, "packId"); }
        internal bool Managed => Eligible && Group != null && Owner != null && Owner.Matches(this, Group);
    }

    internal readonly record struct PackKey(Faction Faction, Lord Lord, DutyDef Duty, IntVec3 Focus);
    internal sealed class PackGroup
    {
        public int Id, NextScan, Observer;
        public PackKey Key;
        public CompPackMember Leader;
        public readonly List<CompPackMember> Members = new();
        public readonly List<Thing> Targets = new();
        public readonly Dictionary<Thing, PackRoute> Routes = new();
    }

    public sealed partial class MapComponent_PackCoordinator : MapComponent
    {
        const int BucketSize = 20, GroupBudget = 2;
        readonly List<CompPackMember> members = new();
        readonly List<PackGroup> groups = new();
        readonly Dictionary<Faction, Dictionary<IntVec2, List<Thing>>> targetBuckets = new();
        readonly List<Thing> candidates = new();
        int nextId = 1, nextMembership, nextIndex, cursor;
        public int GroupCount => groups.Count;
        internal void DebugRefreshForAudit(){Reconcile();RebuildIndex();foreach(var group in groups){group.Targets.Clear();group.Routes.Clear();foreach(var m in group.Members)m.Target=null;Refresh(group);}}
        public MapComponent_PackCoordinator(Map map) : base(map) { }
        internal void Register(CompPackMember member)
        {
            if (!members.Contains(member)) members.Add(member);
            nextId = Math.Max(nextId, member.PackId + 1); nextMembership = 0;
        }
        internal void Unregister(CompPackMember member)
        {
            members.Remove(member); member.Group?.Members.Remove(member);
            member.Group = null; member.Target = null; nextMembership = 0;
        }
        static PackKey Key(CompPackMember member)
        {
            var p = member.Pawn; var duty = p.mindState.duty;
            return new PackKey(p.Faction, p.GetLord(), duty?.def,
                duty?.def == DutyDefOf.Defend ? duty.focus.Cell : IntVec3.Invalid);
        }
        internal bool Matches(CompPackMember member, PackGroup group) => Key(member) == group.Key;
        public override void MapComponentTick()
        {
            if (PackMetrics.LegacyMode || members.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            if (now >= nextMembership) { Reconcile(); nextMembership = now + 60; }
            if (groups.Count == 0) return;
            if (now >= nextIndex) { RebuildIndex(); nextIndex = now + 30; }
            int processed = 0;
            for (int tried = 0; tried < groups.Count && processed < GroupBudget; tried++)
            {
                if (cursor >= groups.Count) cursor = 0;
                var group = groups[cursor++];
                if (now < group.NextScan) continue;
                Refresh(group); group.NextScan = now + Math.Max(6, group.Leader.Settings.scanInterval); processed++;
            }
        }
        void Reconcile()
        {
            nextIndex=0; // Membership/faction changes must not query an index built for an old faction set.
            foreach (var g in groups)
            {
                if(!g.Leader.Eligible)g.Leader=g.Members.FirstOrDefault(m=>m.Eligible&&Matches(m,g))??g.Leader;
                g.Members.Clear();
            }
            foreach (var member in members)
            {
                if (!member.Eligible) { member.Group = null; member.Target = null; continue; }
                var key = Key(member); var group = member.Group;
                if (group == null || group.Key != key || !group.Leader.Eligible ||
                    member.Pawn.Position.DistanceToSquared(group.Leader.Pawn.Position) > member.Settings.keepRadius * member.Settings.keepRadius)
                {
                    group = groups.FirstOrDefault(g => g.Key == key && g.Members.Count < member.Settings.maxPackSize &&
                        g.Leader.Eligible && (g.Id == member.PackId || g.Leader.Pawn.Position.DistanceToSquared(member.Pawn.Position) <= member.Settings.joinRadius * member.Settings.joinRadius));
                    if (group == null)
                    {
                        group = new PackGroup { Id = member.PackId > 0 && !groups.Any(g => g.Id == member.PackId) ? member.PackId : nextId++, Key = key, Leader = member };
                        groups.Add(group);
                    }
                    member.Target = null;
                }
                group.Members.Add(member); member.Group = group; member.PackId = group.Id;
            }
            groups.RemoveAll(g => g.Members.Count == 0);
            foreach (var g in groups) if (!g.Members.Contains(g.Leader) || !g.Leader.Eligible) g.Leader = g.Members[0];
        }
        void RebuildIndex()
        {
            targetBuckets.Clear();
            foreach (var g in groups)
            {
                if (targetBuckets.ContainsKey(g.Key.Faction)) continue;
                var buckets = new Dictionary<IntVec2, List<Thing>>(); targetBuckets.Add(g.Key.Faction, buckets);
                // The native cache includes hostile pawns/buildings and aggressive factionless pawns.
                foreach (var t in map.attackTargetsCache.GetPotentialTargetsFor(g.Leader.Pawn))
                {
                    var thing = t.Thing; if (!thing.Spawned || thing.Map != map) continue;
                    var cell = new IntVec2(thing.Position.x / BucketSize, thing.Position.z / BucketSize);
                    if (!buckets.TryGetValue(cell, out var list)) buckets[cell] = list = new List<Thing>();
                    list.Add(thing); if (PackMetrics.Enabled) PackMetrics.IndexEntries++;
                }
            }
        }
        internal static bool Valid(Pawn pawn, Thing target) => target?.Spawned == true && target.Map == pawn.Map &&
            pawn.HostileTo(target) && target is IAttackTarget attack && !attack.ThreatDisabled(pawn) && AttackTargetFinder.IsAutoTargetable(attack) &&
            (!(target is Pawn victim) || !victim.Dead && !victim.Downed && !victim.IsPsychologicallyInvisible());
        internal static bool InDuty(Pawn pawn, Thing target)
        {
            var duty = pawn.mindState.duty;
            return duty?.def != DutyDefOf.Defend || duty.radius < 0 ||
                target.Position.DistanceToSquared(duty.focus.Cell) <= duty.radius * duty.radius;
        }
        void Refresh(PackGroup group)
        {
            if (!group.Leader.Eligible)
            {
                var replacement = group.Members.FirstOrDefault(m => m.Eligible && Matches(m, group));
                if (replacement == null) return;
                group.Leader = replacement;
            }
            if (PackMetrics.Enabled) PackMetrics.GroupScans++;
            var leader = group.Leader.Pawn; var settings = group.Leader.Settings;
            var observer = group.Members[group.Observer++ % group.Members.Count].Pawn;
            candidates.Clear();
            var buckets = targetBuckets[group.Key.Faction];
            int radius = (int)Math.Ceiling(settings.acquireRadius / BucketSize) + 1;
            var center = new IntVec2(leader.Position.x / BucketSize, leader.Position.z / BucketSize);
            for (int x = center.x - radius; x <= center.x + radius; x++)
                for (int z = center.z - radius; z <= center.z + radius; z++)
                    if (buckets.TryGetValue(new IntVec2(x, z), out var nearby))
                        foreach (var target in nearby)
                        {
                            if (PackMetrics.Enabled) PackMetrics.CandidateChecks++;
                            if (Valid(leader, target) && InDuty(leader, target) && leader.Position.DistanceToSquared(target.Position) <= settings.acquireRadius * settings.acquireRadius)
                                candidates.Add(target);
                        }
            candidates.Sort((a,b) => { int d = leader.Position.DistanceToSquared(a.Position).CompareTo(leader.Position.DistanceToSquared(b.Position)); return d != 0 ? d : a.thingIDNumber.CompareTo(b.thingIDNumber); });
            var oldTargets = group.Targets.ToArray(); group.Targets.Clear();
            // Retain known live targets within the leash; occasional occlusion does not reshuffle a fight.
            foreach (var target in oldTargets)
                if (Valid(leader, target) && InDuty(leader, target) && leader.Position.DistanceToSquared(target.Position) <= settings.keepRadius * settings.keepRadius)
                    group.Targets.Add(target);
            int checkedCount = 0;
            foreach (var target in candidates)
            {
                if (group.Targets.Count >= Math.Min(settings.candidateLimit, group.Members.Count) || checkedCount++ >= settings.candidateLimit) break;
                if (group.Targets.Contains(target)) continue;
                if (PackMetrics.Enabled) PackMetrics.VisibilityChecks++;
                if (!GenSight.LineOfSightToThing(leader.Position,target,map) &&
                    (!observer.Spawned || !GenSight.LineOfSightToThing(observer.Position,target,map))) continue;
                if (!leader.CanReach(target,PathEndMode.Touch,Danger.Deadly)) continue;
                group.Targets.Add(target);
            }
            var counts = new Dictionary<Thing, int>(); foreach (var t in group.Targets) counts[t] = 0;
            int quota = counts.Count == 0 ? 0 : (group.Members.Count + counts.Count - 1) / counts.Count;
            foreach (var member in group.Members)
            {
                if (!member.Eligible || !Matches(member,group)) { member.Target = null; continue; }
                if (member.Target != null && !member.Blocked(member.Target) && counts.TryGetValue(member.Target,out var count) && count < quota) counts[member.Target]++;
                else member.Target = null;
            }
            foreach (var member in group.Members)
            {
                if (!member.Eligible || member.Target != null || counts.Count == 0) continue;
                Thing best = null; float score = float.MaxValue;
                foreach (var pair in counts)
                {
                    if(member.Blocked(pair.Key))continue;
                    // Load first, distance second: equally accessible five targets receive five subgroups.
                    float value = pair.Value * 100000 + member.Pawn.Position.DistanceToSquared(pair.Key.Position);
                    if (value < score) { score = value; best = pair.Key; }
                }
                if(best!=null){member.Target = best; counts[best]++; if (PackMetrics.Enabled) PackMetrics.Assignments++;}
            }
            foreach (var target in group.Routes.Keys.Where(t => !counts.ContainsKey(t)).ToArray()) group.Routes.Remove(target);
        }
        internal Job Follow(CompPackMember member)
        {
            var pawn = member.Pawn; var leader = member.Leader; int now = Find.TickManager.TicksGame;
            if (leader == null || leader == pawn || !leader.Spawned || now - member.LastFollowTick < 90 ||
                pawn.Position.DistanceToSquared(leader.Position) <= 36) return null;
            bool marching=leader.pather.Moving&&leader.CurJob?.def==JobDefOf.Goto&&
                leader.Position.DistanceToSquared(leader.CurJob.targetA.Cell)>144;
            if(!marching&&pawn.Position.DistanceToSquared(leader.Position)<=member.Settings.joinRadius*member.Settings.joinRadius)return null;
            member.LastFollowTick = now;
            // Fixed rendezvous snapshot; never request a fresh path for every movement of the leader.
            var job = JobMaker.MakeJob(JobDefOf.Goto, leader.Position); job.expiryInterval = 180; job.checkOverrideOnExpire = true;
            return job;
        }
    }

    [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "UpdateEnemyTarget")]
    static class PackEnemyTarget
    {
        static bool Prefix(Pawn pawn)
        {
            var member = pawn.GetComp<CompPackMember>(); if (member?.Managed != true) return true;
            var target = MapComponent_PackCoordinator.Valid(pawn,member.Target) ? member.Target : null;
            if(target!=null&&!MapComponent_PackCoordinator.InDuty(pawn,target))target=null;
            // Group assignment must not make a hound walk past an enemy already biting distance away.
            // Constant-size local contact check, not another map-wide acquisition scan.
            if(target==null||!pawn.CanReachImmediate(target,PathEndMode.Touch))
                foreach(var dir in GenAdj.AdjacentCellsAndInside)
                {
                    var cell=pawn.Position+dir;if(!cell.InBounds(pawn.Map))continue;
                    foreach(var thing in cell.GetThingList(pawn.Map))
                        if(MapComponent_PackCoordinator.Valid(pawn,thing)&&MapComponent_PackCoordinator.InDuty(pawn,thing)&&pawn.CanReachImmediate(thing,PathEndMode.Touch))
                        {target=thing;goto foundContact;}
                }
            foundContact:
            if (target != pawn.mindState.enemyTarget && target != null)
            { pawn.mindState.lastEngageTargetTick=Find.TickManager.TicksGame; pawn.GetLord()?.Notify_PawnAcquiredTarget(pawn,target); }
            pawn.mindState.enemyTarget = target; return false;
        }
    }
    [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
    static class PackIdleFollow
    {
        static void Postfix(Pawn pawn, ref Job __result)
        { var member = pawn.GetComp<CompPackMember>(); if (__result == null && member?.Managed == true && member.Target == null) __result = member.Owner.Follow(member); }
    }
    [HarmonyPatch(typeof(JobGiver_AIGotoNearestHostile), "TryGiveJob")]
    static class PackAvoidDuplicateScan
    {
        static bool Prefix(Pawn pawn,ref Job __result)
        {
            var member=pawn.GetComp<CompPackMember>();if(member?.Managed!=true)return true;
            if(member.Target==null&&member.Leader==pawn)return true; // Native long-range assault destination for the one leader.
            // FightEnemy handles assigned enemies. Keep idle groups together instead of a second global scan.
            __result=member.Target==null?member.Owner.Follow(member):null;return false;
        }
    }
}
