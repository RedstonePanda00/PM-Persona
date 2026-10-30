using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using PMP.Anomaly;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace PMP.Tests
{
    public sealed class LegionRitualAudit : GameComponent
    {
        int frames,stage,pawnId;bool loaded,done;
        string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"LegionRitualAudit");
        public LegionRitualAudit(Game game){}
        void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Path.Combine(Folder,"checks.txt"),"PASS "+message+"\n");}
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref stage,"ritualAuditStage");Scribe_Values.Look(ref pawnId,"ritualAuditPawn");}
        public override void LoadedGame(){loaded=true;}
        public override void GameComponentUpdate()
        {
            if(done||!GenCommandLine.CommandLineArgPassed("pmp-legion-ritual-audit")||Find.CurrentMap==null||++frames<100)return;
            Directory.CreateDirectory(Folder);
            File.WriteAllLines(Path.Combine(Folder,"translation-errors.txt"),LanguageDatabase.activeLanguage.loadErrors.Concat(LanguageDatabase.activeLanguage.defInjections.SelectMany(p=>p.loadErrors)));
            try
            {
                if(!ModsConfig.AnomalyActive)
                {
                    Check(GenTypes.GetTypeInAnyAssembly("PMP.Anomaly.Hediff_LegionChallenge")==null,"ritual runtime excluded without Anomaly");
                    foreach(var name in new[]{"PMP_WarhoundResearch","PMP_LegionGuardResearch","PMP_LegionChosenResearch"})Check(DefDatabase<ResearchProjectDef>.GetNamedSilentFail(name)==null,name+" excluded without Anomaly");
                    Check(DefDatabase<ThingDef>.GetNamedSilentFail("PMP_Warhound")!=null,"campaign enemy race retained without Anomaly");
                    Finish();return;
                }
                Run();
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"FAIL.txt"),ex.ToString());done=true;UnityEngine.Application.Quit();}
        }
        void Finish()
        {
            var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);
            Check(errors.Length==0,"no native errors");File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Anomaly="+ModsConfig.AnomalyActive);done=true;UnityEngine.Application.Quit();
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        void Run()
        {
            var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            var owner=map.GetComponent<LegionChallengeMapComponent>();
            if(stage==0)
            {
                var legionDef=DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion");
                foreach(var name in new[]{"PMP_OutpostGuard","PMP_BelieverCerberus","PMP_BelieverOpis","PMP_M3Believer","PMP_LegionCenturion","PMP_ChampionWarrior","PMP_SanctuaryChampion","PMP_Warhound","PMP_LegionGuard","PMP_LegionChosen"})
                    Check(DefDatabase<PawnKindDef>.GetNamed(name).defaultFactionDef==legionDef,name+" defaults to Romulus legion after native inheritance");
                var worker=map.mapPawns.FreeColonistsSpawned.First();
                worker.health.AddHediff(DefDatabase<HediffDef>.GetNamed("MechlinkImplant"),worker.health.hediffSet.GetBrain());
                Check(MechanitorUtility.IsMechanitor(worker),"mechanitor available for controller selection");
                foreach(var suffix in new[]{"Warhound","LegionGuard","LegionChosen"})
                {
                    var def=DefDatabase<PsychicRitualDef>.GetNamed("PMP_Summon"+suffix) as PsychicRitualDef_LegionChallenge;
                    Check(def!=null&&!def.ConfigErrors().Any(),suffix+" native ritual config and runtime class");
                    Check(!def.Visible,suffix+" ritual hidden before research");
                    Find.ResearchManager.FinishProject(def.researchPrerequisite,false,null,false);
                    Check(def.Visible,suffix+" ritual visible after research");
                    Check(def.offerings.Count==3,suffix+" three distinct material offerings");
                    foreach(var cost in def.offerings)foreach(var item in map.listerThings.ThingsOfDef(cost.thingDef).ToList())item.Destroy();
                    foreach(var cost in def.offerings)
                    {
                        int count=cost.count+1;
                        while(count>0){var t=ThingMaker.MakeThing(cost.thingDef);t.stackCount=Math.Min(count,cost.thingDef.stackLimit);count-=t.stackCount;GenSpawn.Spawn(t,CellFinder.RandomClosewalkCellNear(worker.Position,map,6),map);}
                    }
                    var removed=map.listerThings.ThingsOfDef(def.offerings[0].thingDef).ToList();foreach(var t in removed)t.DeSpawn();
                    Check(!def.TryOfferings(map,new[]{worker},true)&&def.offerings.Skip(1).All(c=>map.listerThings.ThingsOfDef(c.thingDef).Sum(t=>t.stackCount)==c.count+1),suffix+" incomplete offering consumes nothing");
                    foreach(var t in removed)GenSpawn.Spawn(t,CellFinder.RandomClosewalkCellNear(worker.Position,map,6),map);
                    Check(def.TryOfferings(map,new[]{worker},true)&&def.offerings.All(c=>map.listerThings.ThingsOfDef(c.thingDef).Sum(t=>t.stackCount)==1),suffix+" exact material counts consumed atomically");
                    foreach(var cost in def.offerings)foreach(var item in map.listerThings.ThingsOfDef(cost.thingDef).ToList())item.Destroy();
                    var pawn=owner.Summon(def,CellFinder.RandomClosewalkCellNear(worker.Position,map,12));pawn.jobs.StopAll();
                    Check(pawn.Faction.def==legionDef&&pawn.GetLord()?.faction==pawn.Faction,suffix+" summoned pawn and assault lord use its Romulus default faction");
                    var marker=pawn.health.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().Single();
                    Check(marker.remaining==def.defeatsRequired,suffix+" correct initial defeat counter");
                    Check(pawn.RaceProps.mechEnabledWorkTypes.Count==1&&pawn.RaceProps.mechEnabledWorkTypes[0]==WorkTypeDefOf.Hauling,suffix+" only hauling work enabled");
                    Check(ProtossMech.CompOfflineWorking.Has(pawn),suffix+" WildHunt offline-working component");
                    if(suffix=="Warhound")Check(pawn.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation)>.5f,"warhound jaw supports native hauling manipulation");
                    if(suffix=="LegionChosen")
                    {
                        marker.RecordDamage(72);pawnId=pawn.thingIDNumber;stage=1;loaded=false;
                        GameDataSaveLoader.SaveGame("LegionRitualAudit");LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("LegionRitualAudit"));return;
                    }
                    Defeat(pawn,marker,def.defeatsRequired);
                    var letter=Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_LegionController>().Single(l=>l.mech==pawn);
                    Check(letter.Choices.Count()>=3,suffix+" letter lists mechanitor and autonomous options");
                    letter.SelectController(suffix=="LegionGuard"?worker:null);
                    Check(!pawn.health.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().Any(),suffix+" choice resolves trial marker");
                    Check(MechanitorUtility.CanDraftMech(pawn).Accepted&&!pawn.IsColonyMechRequiringMechanitor(),suffix+" draftable without mandatory overseer");
                    Check(suffix=="Warhound"?pawn.GetOverseer()==null:pawn.GetOverseer()==worker,suffix+" chosen controller state applied");
                }
            }
            else if(stage==1&&loaded)
            {
                var pawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==pawnId);var marker=pawn.health.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().Single();
                Check(marker.remaining==3&&marker.accumulatedDamage==72,"partial damage and remaining rounds persist through actual save/load");
                Defeat(pawn,marker,3);stage=2;loaded=false;
                GameDataSaveLoader.SaveGame("LegionRitualChoiceAudit");LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("LegionRitualChoiceAudit"));
            }
            else if(stage==2&&loaded)
            {
                var pawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==pawnId);var marker=pawn.health.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().Single();
                marker.EnsureChoice();
                var letters=Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_LegionController>().Where(l=>l.mech==pawn).ToList();
                Check(letters.Count==1,"pending choice persists without duplicate letters after load");letters[0].SelectController(null);
                Check(pawn.Faction==Faction.OfPlayer&&pawn.GetOverseer()==null&&MechanitorUtility.CanDraftMech(pawn).Accepted,"chosen autonomous reward remains controllable");
                var ordinary=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_Warhound"),Faction.OfMechanoids);GenSpawn.Spawn(ordinary,CellFinder.RandomClosewalkCellNear(pawn.Position,map,10),map);ordinary.Kill(null);
                Check(ordinary.Dead,"ordinary unsummoned legion mech retains normal death");
                var lethal=owner.Summon((PsychicRitualDef_LegionChallenge)DefDatabase<PsychicRitualDef>.GetNamed("PMP_SummonLegionGuard"),CellFinder.RandomClosewalkCellNear(pawn.Position,map,10));
                var trial=lethal.health.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().Single();
                lethal.TakeDamage(new DamageInfo(DamageDefOf.Blunt,99999,100,hitPart:lethal.RaceProps.body.corePart));
                Check(!lethal.Dead&&trial.remaining==1,"lethal native damage finishes exactly one round without killing challenger (dead="+lethal.Dead+", remaining="+trial.remaining+")");
                Check(!lethal.health.hediffSet.hediffs.Any(h=>h is Hediff_Injury||h is Hediff_MissingPart),"lethal round restores destroyed body parts");
                Finish();
            }
        }
        void Defeat(Pawn pawn,Hediff_LegionChallenge marker,int rounds)
        {
            for(int i=0;i<rounds;i++)
            {
                Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame+2);
                int before=marker.remaining;
                if(i==0)
                {
                    float old=marker.accumulatedDamage;
                    var torso=pawn.RaceProps.body.corePart;
                    pawn.TakeDamage(new DamageInfo(DamageDefOf.Blunt,2,100,hitPart:torso));
                    Check(marker.accumulatedDamage>old,"native applied damage reaches the trial hediff counter");
                }
                marker.RecordDamage(marker.damagePerDefeat-marker.accumulatedDamage-.5f);
                Check(marker.remaining==before,"partial cumulative damage does not finish a round");
                marker.RecordDamage(.5f);
                Check(marker.remaining==before-1&&!pawn.Dead,"one threshold consumes exactly one defeat");
                Check(!pawn.health.hediffSet.hediffs.Any(h=>h is Hediff_Injury||h is Hediff_MissingPart),"round fully restores injuries and body parts");
                if(marker.remaining>0)Check(pawn.HostileTo(Faction.OfPlayer),"incomplete challenge remains hostile");
            }
            Check(pawn.Faction==Faction.OfPlayer&&Find.TickManager.Paused,"completed challenge joins colony and pauses game");
        }
    }
}
