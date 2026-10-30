using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using PMP.Campaign.BossPlugins;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PMP.Tests
{
    public sealed class CenturionBossAudit : GameComponent
    {
        int frames,stage,pawnId;bool loaded,done;
        string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"CenturionBossAudit");
        public CenturionBossAudit(Game game){}
        public override void ExposeData(){Scribe_Values.Look(ref stage,"centurionAuditStage");Scribe_Values.Look(ref pawnId,"centurionAuditPawn");}
        public override void LoadedGame(){loaded=true;}
        void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Path.Combine(Folder,"checks.txt"),"PASS "+message+"\n");}
        void Think(Pawn pawn,bool native)
        {
            object[] args={null,false};
            AccessTools.Method(typeof(Pawn_JobTracker),"DetermineNextJob").Invoke(pawn.jobs,args);
            Check(native?args[0]!=null:args[0]==null,native?"raid centurion executes native think trees":"fixed centurion retains Boss behavior control");
        }
        void Save(string name,int next){stage=next;loaded=false;GameDataSaveLoader.SaveGame(name);LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame(name));}
        public override void GameComponentUpdate()
        {
            if(done||!GenCommandLine.CommandLineArgPassed("pmp-centurion-boss-audit")||Find.CurrentMap==null||++frames<100)return;
            Directory.CreateDirectory(Folder);
            try
            {
                var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                var faction=Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion"));
                var kind=DefDatabase<PawnKindDef>.GetNamed("PMP_LegionCenturion");var owner=map.GetComponent<BossMapComponent>();
                if(stage==0)
                {
                    var pawn=PawnGenerator.GeneratePawn(kind,faction);pawnId=pawn.thingIDNumber;
                    var far=map.AllCells.First(c=>c.Standable(map)&&!c.Fogged(map)&&map.mapPawns.FreeColonistsSpawned.All(p=>p.Position.DistanceToSquared(c)>225));
                    GenSpawn.Spawn(pawn,far,map);pawn.jobs.StopAll();
                    var record=BossMapComponent.For(pawn);
                    Check(record!=null&&record.Health==5000,"normal PawnKind spawn attaches independent 5000-HP Boss");
                    Check(record.Template.RegenerationPerSecond==0&&record.Template.ReviveAfterSeconds==0,"same no-regeneration and no-revival settings as mission two");
                    Check(!record.Active&&!CenturionBehavior.IsRaid(pawn),"fixed default centurion starts dormant outside activation radius");
                    Check(pawn.equipment.Primary?.def.defName=="PMP_CenturionMinigun","default Boss retains Absolution");
                    var actions=record.Template.Phases[0].Behaviors;
                    Check(actions.Any(a=>a.Module=="boss.approach"&&a.Range==24&&a.CooldownTicks==60)&&actions.Any(a=>a.Module=="boss.weapon"&&a.Range==26&&a.CooldownTicks==30),"fixed approach and shoot actions match mission two");
                    Think(pawn,false);
                    var worker=map.mapPawns.FreeColonistsSpawned.First();
                    worker.jobs.StopAll();worker.Position=CellFinder.StandableCellNear(far,map,9);worker.Notify_Teleported();
                    Find.TickManager.DebugSetTicksGame(((Find.TickManager.TicksGame/30)+1)*30);
                    map.GetComponent<CenturionBehaviorMapComponent>().MapComponentTick();
                    Check(record.Active,"player approach within ten cells activates default fixed Boss");
                    int wounds=pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Count();
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.Bullet,100,100));
                    Check(record.Health==4900&&pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Count()==wounds,"incoming damage uses independent Boss HP without adding body wounds (HP="+record.Health+")");
                    Save("CenturionFixedAudit",1);return;
                }
                if(stage==1&&loaded)
                {
                    var pawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==pawnId);var record=BossMapComponent.For(pawn);
                    Check(record!=null&&record.Health==4900&&record.Active&&!CenturionBehavior.IsRaid(pawn),"fixed activation mode and HP persist through actual save/load");
                    Check(owner.Records.Count(r=>r.Proxy==pawn)==1,"fixed Boss is not registered twice after load");
                    var fragment=DefDatabase<ThingDef>.GetNamed("PMP_ArchotechArmorFragment");int before=map.listerThings.ThingsOfDef(fragment).Sum(t=>t.stackCount);
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.Bullet,4900,100));
                    Check(pawn.Dead&&record.Ended&&record.FallenUntil<0,"fixed Boss dies permanently when independent HP reaches zero");
                    Check(map.listerThings.ThingsOfDef(fragment).Sum(t=>t.stackCount)==before+2,"Boss death retains exactly two armor-fragment salvage");
                    var authored=Json.Read<BossTemplate>(File.ReadAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"mission-two-centurion.json")));
                    var original=owner.Spawn(authored,CellFinder.RandomClosewalkCellNear(map.Center,map,12),faction,"authored-centurion");
                    var scenario=BossMapComponent.For(original);
                    Check(scenario.Id=="authored-centurion"&&!scenario.Active&&scenario.Template.Id=="m2-centurion-boss"&&scenario.Template.TargetRole=="hero","authored mission-two Boss keeps its own dormant encounter template");
                    Check(owner.Records.Count(r=>r.Proxy==original)==1,"automatic preset does not duplicate or overwrite authored Boss");
                    map.GetComponent<CenturionBehaviorMapComponent>().MapComponentTick();
                    Check(!scenario.Active,"default activation scanner leaves authored trigger control intact");
                    LordMaker.MakeNewLord(faction,new LordJob_AssaultColony(faction),map,new[]{original});
                    Check(!CenturionBehavior.IsRaid(original)&&!scenario.Active&&scenario.Template.Phases[0].Behaviors.Count==2,"assault Lord cannot overwrite authored encounter activation or behaviors");
                    for(int attempt=0;attempt<8&&!map.mapPawns.AllPawnsSpawned.Any(p=>p.kindDef==kind&&CenturionBehavior.IsRaid(p));attempt++)
                    {
                        var parms=new IncidentParms{target=map,faction=faction,points=3000,forced=true,raidStrategy=RaidStrategyDefOf.ImmediateAttack,raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn};
                        Rand.PushState(417+attempt);try{Check(IncidentDefOf.RaidEnemy.Worker.TryExecute(parms),"actual native raid executes with Boss centurions");}finally{Rand.PopState();}
                    }
                    var raiders=map.mapPawns.AllPawnsSpawned.Where(p=>p.kindDef==kind&&CenturionBehavior.IsRaid(p)).ToList();
                    File.WriteAllLines(Path.Combine(Folder,"raid-diagnostics.txt"),map.mapPawns.AllPawnsSpawned.Where(p=>p.Faction==faction).Select(p=>p.kindDef.defName+" boss="+(BossMapComponent.For(p)?.Id??"none")+" orders="+string.Join(",",p.health.hediffSet.hediffs.Select(h=>h.def.defName))));
                    File.AppendAllText(Path.Combine(Folder,"raid-diagnostics.txt"),"\nPostfixes="+string.Join(",",Harmony.GetPatchInfo(AccessTools.Method(typeof(IncidentWorker_Raid),nameof(IncidentWorker_Raid.TryGenerateRaidInfo))).Postfixes.Select(p=>p.owner+":"+p.PatchMethod.DeclaringType.FullName)));
                    Check(raiders.Count>0,"raid centurions receive persistent raid orders");
                    Check(raiders.All(p=>p.GetLord()!=null&&BossMapComponent.For(p)?.Health==5000&&BossMapComponent.For(p).Active),"raid centurions join raid Lord with active 5000-HP Boss records");
                    Check(raiders.All(p=>BossMapComponent.For(p).Template.Phases.All(ph=>ph.Behaviors.Count==0)),"raid mode disables fixed approach and shoot loop");
                    pawn=raiders[0];Think(pawn,true);pawnId=pawn.thingIDNumber;
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.Bullet,100,100));Check(BossMapComponent.For(pawn).Health==4900,"raid mode keeps Boss damage routing");
                    var delayed=PawnGenerator.GeneratePawn(kind,faction);CenturionBehavior.UseRaidLogic(delayed);
                    Check(CenturionBehavior.IsRaid(delayed)&&BossMapComponent.For(delayed)==null,"raid orders can be recorded before a delayed arrival spawns");
                    GenSpawn.Spawn(delayed,CellFinder.RandomClosewalkCellNear(map.Center,map,10),map);
                    Check(BossMapComponent.For(delayed).Active&&BossMapComponent.For(delayed).Template.Phases.All(ph=>ph.Behaviors.Count==0),"delayed arrival applies raid logic on spawn");
                    var wave=PawnGenerator.GeneratePawn(kind,faction);GenSpawn.Spawn(wave,CellFinder.RandomClosewalkCellNear(map.Center,map,10),map);
                    LordMaker.MakeNewLord(faction,new LordJob_AssaultColony(faction),map,new[]{wave});
                    Check(CenturionBehavior.IsRaid(wave)&&BossMapComponent.For(wave).Active,"campaign-style assault Lord also selects raid mode");
                    Save("CenturionRaidAudit",2);return;
                }
                if(stage==2&&loaded)
                {
                    var pawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==pawnId);var record=BossMapComponent.For(pawn);
                    Check(CenturionBehavior.IsRaid(pawn)&&record.Active&&record.Health==4900&&record.Template.Phases.All(ph=>ph.Behaviors.Count==0),"raid mode, HP and disabled fixed behaviors persist through actual save/load");
                    Think(pawn,true);
                    var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);Check(errors.Length==0,"no native errors");
                    File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Fixed, authored and raid Boss centurions passed.");done=true;UnityEngine.Application.Quit();
                }
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"FAIL.txt"),ex.ToString());done=true;UnityEngine.Application.Quit();}
        }
    }
}
