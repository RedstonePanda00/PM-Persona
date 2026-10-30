using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PMP_Persona.Romulus;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace PMP.Tests
{
    public sealed class LegionRaidAudit : GameComponent
    {
        int frames;bool done;
        string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"LegionRaidAudit");
        public LegionRaidAudit(Game game){}
        void Check(bool ok,string message)
        {
            if(!ok)throw new Exception(message);
            File.AppendAllText(Path.Combine(Folder,"checks.txt"),"PASS "+message+"\n");
        }
        IncidentParms Raid(Map map,Faction faction,float points)=>new IncidentParms
        {
            target=map,faction=faction,points=points,forced=true,
            raidStrategy=RaidStrategyDefOf.ImmediateAttack,raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn
        };
        public override void GameComponentUpdate()
        {
            if(done||!GenCommandLine.CommandLineArgPassed("pmp-legion-raid-audit")||Find.CurrentMap==null||++frames<100)return;
            done=true;Directory.CreateDirectory(Folder);
            try
            {
                var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                var def=DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion");var faction=Find.FactionManager.FirstFactionOfDef(def);
                var raid=(IncidentWorker_RaidEnemy)IncidentDefOf.RaidEnemy.Worker;
                Check(faction!=null,"legion already exists at world start");
                Check(!def.raidsForbidden&&def.canStageAttacks,"native raid and staged attack switches enabled");
                Check(raid.FactionCanBeGroupSource(faction,Raid(map,faction,5000)),"legion passes native enemy-raid faction eligibility");
                var groups=def.pawnGroupMakers.Where(g=>g.kindDef==PawnGroupKindDefOf.Combat).ToList();
                var champion=DefDatabase<PawnKindDef>.GetNamed("PMP_ChampionWarrior");
                Check(groups.Count>0&&groups.All(g=>g.options.All(o=>o.kind!=champion)),"normal combat groups exclude champions");
                var kinds=new HashSet<PawnKindDef>();
                foreach(float points in new[]{90f,180f,500f,1500f,4000f,10000f,39999f,40000f,100000f})
                {
                    var parms=new PawnGroupMakerParms{faction=faction,groupKind=PawnGroupKindDefOf.Combat,points=points,raidStrategy=RaidStrategyDefOf.ImmediateAttack,seed=417};
                    Check(PawnGroupMakerUtility.TryGetRandomPawnGroupMaker(parms,out var maker),"native combat group available at "+points+" points");
                    var selected=maker.GeneratePawnKindsExample(parms).ToList();
                    Check(selected.Count>0&&selected.All(k=>k!=champion),"normal weighted selection produces units without champions at "+points);
                    foreach(var kind in selected)kinds.Add(kind);
                }
                foreach(var name in new[]{"PMP_Warhound","PMP_OutpostGuard","PMP_BelieverCerberus","PMP_BelieverOpis","PMP_LegionCenturion","PMP_LegionGuard","PMP_LegionChosen"})
                    Check(kinds.Any(k=>k.defName==name),name+" reachable through native weighted raid selection");
                for(int i=0;i<8;i++)
                {
                    Rand.PushState(500+i);
                    Pawn centurion;
                    try{centurion=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_LegionCenturion"),faction);}finally{Rand.PopState();}
                    Check(centurion.equipment.Primary?.def.defName=="PMP_CenturionMinigun","centurion generates Absolution with seed "+i);
                    centurion.Destroy();
                }
                var below=Raid(map,faction,39999);
                Check(raid.TryGenerateRaidInfo(below,out var lowPawns,true),"native raid info generates below champion threshold");
                Check(lowPawns.All(p=>p.kindDef!=champion),"39999-point raid contains no champion");
                foreach(var pawn in lowPawns)pawn.Destroy();
                var before=new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned);
                var high=Raid(map,faction,40000);
                Check(raid.TryExecute(high),"actual 40000-point native raid incident executes");
                var spawned=map.mapPawns.AllPawnsSpawned.Where(p=>!before.Contains(p)).ToList();
                Check(spawned.Count>1&&spawned.Count(p=>p.kindDef==champion)==1,"actual raid spawns exactly one champion at 40000 points");
                Check(spawned.All(p=>p.Faction==faction&&p.GetLord()!=null),"all actual raid units including champion join native raid lords");
                Check(spawned.Where(p=>p.kindDef.defName=="PMP_LegionCenturion").Any()&&spawned.Where(p=>p.kindDef.defName=="PMP_LegionCenturion").All(p=>p.equipment.Primary?.def.defName=="PMP_CenturionMinigun"),"actual raid centurions all carry Absolution");
                LegionRaidChampion.Apply(high,spawned,100000,true);
                Check(spawned.Count(p=>p.kindDef==champion)==1,"repeated high-point champion rule does not add a second champion");
                var extra=new List<Pawn>();LegionRaidChampion.Apply(Raid(map,faction,100000),extra,100000,true);
                Check(extra.Count==1&&extra[0].kindDef==champion,"100000-point raid rule adds only one champion");extra[0].Destroy();
                var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();
                File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);Check(errors.Length==0,"no native errors");
                File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Native groups, champion threshold/cap, actual raid and centurion weapons passed.");
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"FAIL.txt"),ex.ToString());}
            UnityEngine.Application.Quit();
        }
    }
}
