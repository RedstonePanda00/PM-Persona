using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using UnityEngine;

namespace PMP_Persona.Romulus
{
    // Explicit isolated native comparison only. LegacyMode disables only the new pack behavior.
    public sealed class PackAudit : GameComponent
    {
        static bool sampling;
        static long nativeScans, pathTime, pathRequests;
        static long meleeHits;
        static readonly HashSet<Thing> protectedTargets=new();
        static readonly Action<JobGiver_AIFightEnemy,Pawn> UpdateTarget=AccessTools.MethodDelegate<Action<JobGiver_AIFightEnemy,Pawn>>(AccessTools.Method(typeof(JobGiver_AIFightEnemy),"UpdateEnemyTarget"));
        readonly List<Pawn> wolves=new(), targets=new();
        readonly List<Thing> testWalls=new();
        readonly List<double> tickMs=new();
        readonly List<string> checks=new();
        Faction enemy; Map map; Lord lord;
        bool ready,done; int frame,run,ticks,reloadStage,savedPackId;long allocated; double totalMs;
        const int Duration=1800, Warmup=180;
        string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"PackAudit");
        public PackAudit(Game game){}
        public override void ExposeData(){Scribe_Values.Look(ref reloadStage,"packAuditReload");Scribe_Values.Look(ref savedPackId,"packAuditId");}
        void Check(bool condition,string message){if(!condition)throw new Exception(message);checks.Add("PASS "+message);File.WriteAllLines(Path.Combine(Folder,"checks.txt"),checks);}
        static void BeforePath(out long __state){__state=sampling?Stopwatch.GetTimestamp():0;}
        static void AfterPath(long __state){if(__state!=0){pathTime+=Stopwatch.GetTimestamp()-__state;pathRequests++;}}
        static void Scan(IAttackTargetSearcher searcher){if(sampling&&searcher.Thing is Pawn p&&p.def.defName=="PMP_Warhound")nativeScans++;}
        static void Protect(Thing __instance,ref DamageInfo dinfo)
        {
            if(!protectedTargets.Contains(__instance))return;
            if(sampling&&dinfo.Instigator is Pawn p&&p.def.defName=="PMP_Warhound")meleeHits++;
            dinfo.SetAmount(0);
        }
        Pawn Spawn(string kind,Faction faction,IntVec3 cell)
        {
            var p=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind),faction);
            GenSpawn.Spawn(p,cell,map);p.GetComp<CompCanBeDormant>()?.WakeUp();p.jobs.StopAll();return p;
        }
        void Clean()
        {
            sampling=false;PackMetrics.Enabled=false;protectedTargets.Clear();
            foreach(var p in wolves.Concat(targets).ToArray())if(!p.Destroyed)p.Destroy(DestroyMode.Vanish);
            wolves.Clear();targets.Clear();
            foreach(var wall in testWalls)if(!wall.Destroyed)wall.Destroy(DestroyMode.Vanish);testWalls.Clear();
            if(lord!=null&&map.lordManager.lords.Contains(lord))map.lordManager.RemoveLord(lord);lord=null;
        }
        void Setup(int count,bool legacy,int seed)
        {
            Clean();PackMetrics.LegacyMode=legacy;Rand.PushState(seed);
            try
            {
                for(int i=0;i<count;i++){var p=Spawn("PMP_Warhound",enemy,new IntVec3(80+i%8,0,85+i/8));wolves.Add(p);protectedTargets.Add(p);}
                for(int i=0;i<5;i++)
                {
                    var p=Spawn("Colonist",Faction.OfPlayer,new IntVec3(120,0,85+i*5));
                    protectedTargets.Add(p);
                    p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait,999999),JobCondition.InterruptForced);targets.Add(p);
                }
                lord=LordMaker.MakeNewLord(enemy,new LordJob_DefendPoint(new IntVec3(100,0,95),wanderRadius:5,defendRadius:90,addFleeToil:false),map,wolves);
                map.GetComponent<MapComponent_PackCoordinator>().MapComponentTick();
            }
            finally{Rand.PopState();}
        }
        void Move(Pawn p,IntVec3 cell){p.jobs.StopAll();p.Position=cell;p.Notify_Teleported();}
        void Step(int count){for(int i=0;i<count;i++)Find.TickManager.DoSingleTick();}
        void Functional()
        {
            Setup(20,false,771);Step(35);
            var mc=map.GetComponent<MapComponent_PackCoordinator>();
            var members=wolves.Select(p=>p.GetComp<CompPackMember>()).ToArray();
            Check(members.All(m=>m.Target!=null),"all twenty members receive targets: "+string.Join("; ",members.Take(3).Select(m=>$"eligible={m.Eligible} managed={m.Managed} pack={m.PackId} target={m.Target} duty={m.Pawn.mindState.duty} awake={m.Pawn.GetComp<CompCanBeDormant>()?.Awake} pos={m.Pawn.Position}"))+" candidates="+map.attackTargetsCache.GetPotentialTargetsFor(wolves[0]).Count+" firstValid="+MapComponent_PackCoordinator.Valid(wolves[0],targets[0]));
            Check(members.Select(m=>m.PackId).Distinct().Count()==1,"one original duty produces one twenty-member pack");
            Check(members.GroupBy(m=>m.Target).Count()==5&&members.GroupBy(m=>m.Target).All(g=>g.Count()==4),"five visible enemies split twenty hounds into five groups of four");
            var first=members[0];int pack=first.PackId;var former=first.Leader;
            former.Destroy(DestroyMode.Vanish);Step(65);
            Check(members.Where(m=>m.Pawn.Spawned).All(m=>m.PackId==pack&&m.Leader!=former&&m.Leader.Spawned),"dead leader replaced without rebuilding surviving pack");
            var removed=targets[0];removed.Destroy(DestroyMode.Vanish);Step(35);
            Check(members.Where(m=>m.Pawn.Spawned).All(m=>m.Target!=removed),"dead target locally released and members reassigned");
            var forced=members.First(m=>m.Pawn.Spawned);var job=JobMaker.MakeJob(JobDefOf.Wait,120);job.playerForced=true;forced.Pawn.jobs.StartJob(job,JobCondition.InterruptForced);
            Check(!forced.Managed,"explicit player-forced job excluded");forced.Pawn.jobs.StopAll();
            var oldFaction=forced.Pawn.Faction;forced.Pawn.SetFaction(Faction.OfPlayer);Check(!forced.Managed,"player-controlled hound excluded");forced.Pawn.SetFaction(oldFaction);
            if(forced.Pawn.GetLord()!=lord)lord.AddPawn(forced.Pawn);
            var originalDuty=forced.Pawn.mindState.duty;forced.Pawn.mindState.duty=new PawnDuty(DutyDefOf.Defend,new IntVec3(190,0,190),5);
            Check(!forced.Managed,"changed duty immediately invalidates old pack before membership cycle");forced.Pawn.mindState.duty=originalDuty;
            // Exercise cached suffix invalidation with a new thick wall placed directly on it.
            var member=members.First(m=>m.Managed&&m.Target!=null);Move(member.Pawn,new IntVec3(80,0,90));
            var vault=member.Pawn.GetComp<CompWallVault>();var path=vault.FindPath(member.Target,PathEndMode.Touch,false);
            Check(path!=null&&path.Count>10,"shared long route reaches assigned target");
            var point=path[path.Count/2];
            foreach(var delta in new[]{IntVec3.Zero,IntVec3.East})
            {var wall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);GenSpawn.Spawn(wall,point+delta,map);testWalls.Add(wall);}
            var revised=vault.FindPath(member.Target,PathEndMode.Touch,false);
            Check(revised!=null&&!revised.Contains(point),"new obstacle invalidates shared suffix and recomputes traversable route");
            for(int i=1;i<revised.Count;i++)Check(vault.ValidEdge(revised[i],revised[i-1]),"replacement route edge remains walkable or a legal single-wall vault");
            foreach(var wall in testWalls)if(!wall.Destroyed)wall.Destroy(DestroyMode.Vanish);testWalls.Clear();
            var goal=member.Target;
            foreach(var cell in CellRect.CenteredOn(goal.Position,2).EdgeCells)
            {var wall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);GenSpawn.Spawn(wall,cell,map);testWalls.Add(wall);}
            var jump=vault.FindPath(goal,PathEndMode.Touch,false);
            Check(jump!=null&&jump.Zip(jump.Skip(1),(a,b)=>(a-b).LengthHorizontalSquared).Contains(4),"shared route retains passive single-wall vault edges");
            foreach(var cell in CellRect.CenteredOn(goal.Position,3).EdgeCells)
            {var wall=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);GenSpawn.Spawn(wall,cell,map);testWalls.Add(wall);}
            Check(vault.FindPath(goal,PathEndMode.Touch,false)==null,"shared route cannot cross a two-cell-thick sealed wall");
            Check(member.Blocked(goal)&&member.Target==null,"unreachable member suspends that target instead of retrying every job");
            Step(35);Check(member.Target!=goal,"unreachable target remains excluded during reassignment cooldown");
            foreach(var wall in testWalls)if(!wall.Destroyed)wall.Destroy(DestroyMode.Vanish);testWalls.Clear();
            var relocated=member.Target;var oldPosition=relocated.Position;Move((Pawn)relocated,oldPosition+IntVec3.East*4);
            var moved=vault.FindPath(relocated,PathEndMode.Touch,false);
            Check(moved!=null&&ReachabilityImmediate.CanReachImmediate(moved[0],relocated,map,PathEndMode.Touch,member.Pawn),"moving target rebuilds shared destination correctly");
            var detached=members.First(m=>m.Pawn.Spawned&&m!=member);lord.RemovePawn(detached.Pawn);
            var separate=LordMaker.MakeNewLord(enemy,new LordJob_DefendPoint(new IntVec3(100,0,95),wanderRadius:5,defendRadius:90,addFleeToil:false),map,new[]{detached.Pawn});
            Step(65);Check(detached.PackId!=member.PackId,"different original Lords never merge even at the same guard point");
            var follower=members.First(m=>m.Managed&&m.Pawn!=m.Leader);var leader=follower.Leader;
            Move(leader,new IntVec3(90,0,130));Move(follower.Pawn,new IntVec3(80,0,130));follower.LastFollowTick=-9999;
            Check(mc.Follow(follower)==null,"idle leader fidget does not trigger follower pathfinding");
            leader.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto,new IntVec3(130,0,130)),JobCondition.InterruptForced);
            Check(mc.Follow(follower)?.def==JobDefOf.Goto,"purposeful long march triggers follower rendezvous");
            Check(mc.Follow(follower)==null,"rendezvous refresh is throttled rather than moving-target spam");
            Clean();
            if(map.lordManager.lords.Contains(separate))map.lordManager.RemoveLord(separate);
        }
        void Controlled(int count,bool legacy)
        {
            Setup(count,legacy,919+count);
            var mc=map.GetComponent<MapComponent_PackCoordinator>();
            // Identical stationary target field and one decision/path request per member.
            // New mode shares acquisition/assignment and suffixes; legacy uses native target selection and original A*.
            PackMetrics.Reset();PackMetrics.Enabled=true;nativeScans=pathTime=pathRequests=meleeHits=0;sampling=true;
            long start=Stopwatch.GetTimestamp(),bytes=GC.GetAllocatedBytesForCurrentThread();
            if(!legacy) { mc.DebugRefreshForAudit(); }
            foreach(var pawn in wolves)
            {
                UpdateTarget(new JobGiver_AIFightEnemies(),pawn);
                var target=pawn.mindState.enemyTarget;
                if(target!=null)pawn.GetComp<CompWallVault>().FindPath(target,PathEndMode.Touch,false);
            }
            double ms=(Stopwatch.GetTimestamp()-start)*1000d/Stopwatch.Frequency;long alloc=GC.GetAllocatedBytesForCurrentThread()-bytes;
            sampling=false;PackMetrics.Enabled=false;
            Append("controlled",count,legacy,0,ms,ms,ms,alloc,mc.GroupCount);
        }
        void Append(string scenario,int count,bool legacy,int repeat,double ms,double p95,double p99,long bytes,int groups)
        {
            string mode=legacy?"legacy":"pack";
            File.AppendAllText(Path.Combine(Folder,"measurements.csv"),string.Join(",",new object[]{scenario,count,mode,repeat,legacy?0:groups,ms.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),p95.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),p99.ToString("F4",System.Globalization.CultureInfo.InvariantCulture),bytes,nativeScans,pathRequests,(pathTime*1000d/Stopwatch.Frequency).ToString("F4",System.Globalization.CultureInfo.InvariantCulture),PackMetrics.FullPaths,PackMetrics.ExpandedNodes,PackMetrics.SharedHits,PackMetrics.JoinNodes,PackMetrics.JoinFailures,PackMetrics.GroupScans,PackMetrics.CandidateChecks,PackMetrics.IndexEntries,PackMetrics.Assignments,PackMetrics.LocalHits,meleeHits})+"\n");
        }
        public override void GameComponentUpdate()
        {
            if(!GenCommandLine.CommandLineArgPassed("pmp-pack-audit")||done||Find.CurrentMap==null||++frame<90)return;
            try
            {
                Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                if(reloadStage==1)
                {
                    map=Find.CurrentMap;checks.AddRange(File.ReadAllLines(Path.Combine(Folder,"checks.txt")));
                    wolves.AddRange(map.mapPawns.AllPawnsSpawned.Where(p=>p.def.defName=="PMP_Warhound"));foreach(var p in map.mapPawns.AllPawnsSpawned.Where(p=>p.Faction==Faction.OfPlayer))protectedTargets.Add(p);Step(65);
                    Check(wolves.Count==20&&wolves.All(p=>p.GetComp<CompPackMember>().PackId==savedPackId&&p.GetComp<CompPackMember>().Managed),"same-version save/load rebuilds one coordinated pack with persistent membership");
                    Check(wolves.All(p=>p.GetComp<CompPackMember>().Target!=null),"save/load reacquires targets without serialized path caches");
                    Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"native run contains no Verse errors");
                    done=true;PackMetrics.LegacyMode=false;File.WriteAllText(Path.Combine(Folder,"complete.txt"),checks.Count+" checks passed; 6 controlled cases and 12 native 1800-tick runs");Application.Quit();return;
                }
                if(!ready)
                {
                    ready=true;Directory.CreateDirectory(Folder);map=Find.CurrentMap;
                    foreach(var pawn in map.mapPawns.AllPawnsSpawned.ToArray())pawn.Destroy(DestroyMode.Vanish);
                    foreach(var cell in new CellRect(35,35,165,165))
                    {
                        foreach(var thing in cell.GetThingList(map).ToArray())if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);
                        map.terrainGrid.SetTerrain(cell,TerrainDefOf.Concrete);map.roofGrid.SetRoof(cell,null);
                    }
                    enemy=Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion"));
                    if(enemy==null){enemy=FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion")));Find.FactionManager.Add(enemy);}
                    Check(enemy.HostileTo(Faction.OfPlayer),"comparison factions hostile");
                    var h=new Harmony("pmp.pack.optin-audit");
                    h.Patch(AccessTools.Method(typeof(CompWallVault),nameof(CompWallVault.FindPath)),new HarmonyMethod(typeof(PackAudit),nameof(BeforePath)),new HarmonyMethod(typeof(PackAudit),nameof(AfterPath)));
                    h.Patch(AccessTools.Method(typeof(AttackTargetFinder),nameof(AttackTargetFinder.BestAttackTarget)),new HarmonyMethod(typeof(PackAudit),nameof(Scan)));
                    h.Patch(AccessTools.Method(typeof(Thing),nameof(Thing.TakeDamage)),new HarmonyMethod(typeof(PackAudit),nameof(Protect)));
                    File.WriteAllText(Path.Combine(Folder,"measurements.csv"),"scenario,count,mode,repeat,groups,total_ms,p95_ms,p99_ms,allocated_bytes,native_scans,path_requests,path_ms,full_paths,astar_nodes,shared_hits,join_nodes,join_failures,group_scans,candidate_checks,index_entries,assignments,local_hits,melee_hits\n");
                    Functional();
                    foreach(int n in new[]{40,80,160}){Controlled(n,true);Controlled(n,false);}
                    Clean();return;
                }
                if(run>=12)
                {
                    Setup(20,false,717);Step(35);savedPackId=wolves[0].GetComp<CompPackMember>().PackId;reloadStage=1;
                    GameDataSaveLoader.SaveGame("Pack-roundtrip");GameDataSaveLoader.LoadGame("Pack-roundtrip");return;
                }
                // Paired repeats alternate execution order to reduce warm-JIT/order bias.
                int size=new[]{40,80,160}[run/4],repeat=(run%4)/2;bool legacy=(run%2==0)==(repeat==0);
                if(ticks==0)
                {
                    Setup(size,legacy,1729+size+repeat);Step(Warmup);
                    tickMs.Clear();totalMs=0;allocated=0;nativeScans=pathTime=pathRequests=meleeHits=0;PackMetrics.Reset();PackMetrics.Enabled=true;sampling=true;
                }
                Rand.PushState(10000+size+repeat*100+ticks);
                try
                {
                    for(int i=0;i<20&&ticks<Duration;i++,ticks++)
                    {
                        // Keep the same five durable targets stationary; attack damage and animations still run.
                        foreach(var target in targets)if(target.Spawned&&target.CurJobDef!=JobDefOf.Wait)target.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Wait,999999),JobCondition.InterruptForced);
                        long before=Stopwatch.GetTimestamp(),bytes=GC.GetAllocatedBytesForCurrentThread();Find.TickManager.DoSingleTick();
                        double ms=(Stopwatch.GetTimestamp()-before)*1000d/Stopwatch.Frequency;totalMs+=ms;tickMs.Add(ms);allocated+=GC.GetAllocatedBytesForCurrentThread()-bytes;
                    }
                }
                finally{Rand.PopState();}
                if(ticks>=Duration)
                {
                    sampling=false;PackMetrics.Enabled=false;tickMs.Sort();
                    Append("native",size,legacy,repeat,totalMs,tickMs[(int)(tickMs.Count*.95)],tickMs[(int)(tickMs.Count*.99)],allocated,map.GetComponent<MapComponent_PackCoordinator>().GroupCount);
                    Check(wolves.Count(p=>p.Spawned&&!p.Dead)==size,"native "+run+" preserves all combatants");
                    Check(targets.All(p=>p.Spawned&&!p.Dead&&!p.Downed),"native "+run+" preserves exactly five active targets");
                    Clean();ticks=0;run++;File.WriteAllText(Path.Combine(Folder,"progress.txt"),"completed native run "+run+"/12");
                }
            }
            catch(Exception ex){done=true;sampling=false;PackMetrics.Enabled=false;PackMetrics.LegacyMode=false;Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"failure.txt"),ex.ToString());Log.Error("[PackAudit] "+ex);Application.Quit();}
        }
    }
}
