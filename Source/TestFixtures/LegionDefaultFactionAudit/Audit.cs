using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using Verse;

namespace PMP.Tests
{
    public sealed class LegionDefaultFactionAudit : GameComponent
    {
        int frames;bool done;
        static bool choosingCell;static IntVec3 cell;
        string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"LegionDefaultFactionAudit");
        public LegionDefaultFactionAudit(Game game){}
        void Check(bool ok,string message)
        {
            if(!ok)throw new Exception(message);
            File.AppendAllText(Path.Combine(Folder,"checks.txt"),"PASS "+message+"\n");
        }
        static bool MouseCell(ref IntVec3 __result)
        {
            if(!choosingCell)return true;
            __result=cell;return false;
        }
        public override void GameComponentUpdate()
        {
            if(done||!GenCommandLine.CommandLineArgPassed("pmp-legion-default-faction-audit")||Find.CurrentMap==null||++frames<100)return;
            done=true;Directory.CreateDirectory(Folder);
            var harmony=new Harmony("PMP.Tests.LegionDefaultFactionAudit");
            try
            {
                var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                var def=DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion");
                Check(def.requiredCountAtGameStart==1,"legion requires exactly one faction at world creation");
                var page=new Page_CreateWorldParams();
                AccessTools.Method(typeof(Page_CreateWorldParams),"ResetFactionCounts").Invoke(page,null);
                var configured=(List<FactionDef>)AccessTools.Field(typeof(Page_CreateWorldParams),"factions").GetValue(page);
                Check(configured.Count(f=>f==def)==1,"normal new-world settings include one legion in their actual faction list");
                Check(!def.displayInFactionSelection,"automatically included legion remains hidden from the faction selection UI");
                var factions=Find.FactionManager.AllFactions.Where(f=>f.def==def).ToList();
                Check(factions.Count==1,"native new-world generation already created exactly one legion before any pawn spawning");
                var faction=factions.Single();
                Check(FactionUtility.DefaultFactionFrom(def)==faction,"unpatched native default-faction lookup resolves existing legion");
                Check(GenTypes.GetTypeInAnyAssembly("PMP_Persona.Romulus.LegionDefaultFaction")==null,"no lazy-creation faction patch remains");
                var menu=(List<DebugActionNode>)AccessTools.Method(typeof(DebugToolsSpawning),"SpawnPawn").Invoke(null,null);
                harmony.Patch(AccessTools.Method(typeof(UI),nameof(UI.MouseCell)),prefix:new HarmonyMethod(typeof(LegionDefaultFactionAudit),nameof(MouseCell)));
                foreach(var name in new[]{"PMP_Warhound","PMP_LegionGuard","PMP_LegionChosen","PMP_OutpostGuard","PMP_BelieverCerberus","PMP_BelieverOpis","PMP_M3Believer","PMP_LegionCenturion","PMP_ChampionWarrior","PMP_SanctuaryChampion"})
                {
                    Check(DefDatabase<PawnKindDef>.GetNamed(name).defaultFactionDef==def,name+" native inherited default is Romulus legion");
                    var before=new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned);
                    cell=CellFinder.RandomClosewalkCellNear(map.Center,map,20);choosingCell=true;
                    try{menu.Single(n=>n.label==name).action();}finally{choosingCell=false;}
                    var pawn=map.mapPawns.AllPawnsSpawned.Single(p=>!before.Contains(p));pawn.jobs.StopAll();
                    Check(pawn.Faction==faction,name+" actual developer Spawn Pawn action creates a legion pawn, not a wild pawn");
                    pawn.DeSpawn();
                }
                Check(Find.FactionManager.AllFactions.Count(f=>f.def==def)==1,"repeated developer spawning reuses the one initial legion faction");
                var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();
                File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);Check(errors.Length==0,"no native errors");
                File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Native world creation and actual developer Spawn Pawn actions passed.");
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"FAIL.txt"),ex.ToString());}
            finally{harmony.UnpatchAll(harmony.Id);UnityEngine.Application.Quit();}
        }
    }
}
