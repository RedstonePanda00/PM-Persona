using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using PMP.Campaign.BossPlugins;
using PMP_Persona.Romulus;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PMP.Tests
{
    public sealed class ChampionBehaviorAudit : GameComponent
    {
        int frames,stage,fixedId,waveId,raidId,heldId;bool loaded,done;double savedHealth,savedCharge;
        string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"ChampionBehaviorAudit");
        public ChampionBehaviorAudit(Game game){}
        public override void ExposeData(){Scribe_Values.Look(ref stage,"championAuditStage");Scribe_Values.Look(ref fixedId,"championFixedId");Scribe_Values.Look(ref waveId,"championWaveId");Scribe_Values.Look(ref raidId,"championRaidId");Scribe_Values.Look(ref heldId,"championHeldId");Scribe_Values.Look(ref savedHealth,"championSavedHealth");Scribe_Values.Look(ref savedCharge,"championSavedCharge");}
        public override void LoadedGame(){loaded=true;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;}
        void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Path.Combine(Folder,"checks.txt"),"PASS "+message+"\n");}
        string Contract(BossTemplate t)=>t.MaxHealth+"/"+t.RegenerationPerSecond+"/"+t.ReviveAfterSeconds+"/"+t.DamageMultiplier+"/"+Json.Write(t.Components);
        void Think(Pawn pawn,bool native)
        {
            object[] args={null,false};AccessTools.Method(typeof(Pawn_JobTracker),"DetermineNextJob").Invoke(pawn.jobs,args);
            Check(native?args[0]!=null:args[0]==null,native?"raid champion uses native think tree":"fixed champion keeps Boss behavior control");
        }
        void Clear(Map map,IntVec3 at,int radius)
        {
            foreach(var t in GenRadial.RadialDistinctThingsAround(at,map,radius,true).Where(t=>t is not Pawn&&t.def.destroyable).ToList())t.Destroy();
            foreach(var c in CellRect.CenteredOn(at,radius).Cells.Where(c=>c.InBounds(map))){map.roofGrid.SetRoof(c,null);map.terrainGrid.SetTerrain(c,TerrainDefOf.Soil);map.fogGrid.Unfog(c);}
        }
        IncidentParms Raid(Map map,Faction faction,float points)=>new IncidentParms{target=map,faction=faction,points=points,forced=true,raidStrategy=RaidStrategyDefOf.ImmediateAttack,raidArrivalMode=PawnsArrivalModeDefOf.EdgeWalkIn};
        public override void GameComponentUpdate()
        {
            if(done||!GenCommandLine.CommandLineArgPassed("pmp-champion-behavior-audit")||Find.CurrentMap==null||++frames<100)return;
            Directory.CreateDirectory(Folder);
            try
            {
                var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                var owner=map.GetComponent<BossMapComponent>();var c=Current.Game.GetComponent<CampaignController>();
                var preset=Json.Read<BossTemplate>(DefDatabase<BossTemplateDef>.GetNamed("PMP_RegularChampionBoss").templateJson);
                var expected=Contract(preset);
                if(stage==0)
                {
                    foreach(var name in new[]{"PMP_ChampionBehaviorMode","PMP_CenturionRaidOrders"})
                        Check(DefDatabase<HediffDef>.GetNamedSilentFail(name)==null,name+" obsolete definition absent");
                    var spec=Json.Read<MapSpec>(File.ReadAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"third-arena-fixture.json")));
                    var placement=spec.Objects.First(p=>p.Id=="m3-champion");var anchor=spec.Objects.First(p=>p.Id=="champion-post");
                    var center=map.Center;Clear(map,center,25);
                    placement.Position=new Cell{X=center.x,Z=center.z};anchor.Position=new Cell{X=center.x,Z=center.z};
                    spec.Regions[0].Min=new Cell{X=center.x-20,Z=center.z-20};spec.Regions[0].Max=new Cell{X=center.x+20,Z=center.z+20};
                    c.Project=new Project{Id="pmp-mission-one",Maps=new List<MapSpec>{spec},Campaigns=new List<RimCampaign.Core.Campaign>{new RimCampaign.Core.Campaign{Id="pmp_archive",Nodes=new List<Node>{new Node{Id="mission-three",Kind="Story",Map=spec.Id},new Node{Id="mission-five",Kind="Story",Map=spec.Id}}}}};
                    c.CampaignId="pmp_archive";c.NodeId="mission-three";c.Map=map;c.State.Phase="Running";
                    c.Project.Campaigns[0].Nodes.Add(new Node{Id="open-campaign",Kind="Story",Map=spec.Id});
                    var third=(Pawn)c.Place(placement,map);fixedId=third.thingIDNumber;
                    var arena=map.GetComponent<BossArenaMapComponent>();
                    if(arena==null){arena=new BossArenaMapComponent(map);map.components.Add(arena);}
                    arena.Attach(c,spec.Components.Single());
                    var rule=arena.RuleFor(third);var record=BossMapComponent.For(third);
                    Check(rule!=null&&!arena.Activated(rule)&&!BossMapComponent.BehaviorsAllowed(third),"authored third-chapter arena stays dormant before sector two is captured");
                    ChampionBehavior.UseRaidLogic(third);LordMaker.MakeNewLord(third.Faction,new LordJob_AssaultColony(third.Faction),map,new[]{third});
                    Check(!ChampionBehavior.IsRaid(third)&&record.Template.Phases[0].Behaviors.Count==2,"third-chapter fixed orders resist incident and assault-Lord raid mode");Think(third,false);
                    c.State.Variables[rule.ActivationVariable]=1;
                    Check(arena.Activated(rule)&&BossMapComponent.BehaviorsAllowed(third),"original sector-two capture variable activates third-chapter champion");
                    var empire=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(spec,"Empire");
                    var target=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,empire);GenSpawn.Spawn(target,center+IntVec3.East*5,map);
                    Check(arena.AllowsTarget(rule,target),"third-chapter champion still accepts Empire targets inside its arena");
                    var player=map.mapPawns.FreeColonistsSpawned.First();player.jobs.StopAll();player.Position=center+IntVec3.North*5;player.Notify_Teleported();
                    Check(!arena.AllowsTarget(rule,player)&&!arena.AllowsStep(rule,third,center+IntVec3.East*22),"third-chapter target-faction and arena-boundary restrictions remain intact");
                    Check(Contract(record.Template)==expected,"fixed-mode health and all skill components match unchanged champion preset");
                    record.Active=false;
                    c.NodeId="mission-five";var at=center+IntVec3.South*45;Clear(map,at,14);
                    var slot=Json.Copy(placement);slot.Id="m5-audit-champion";slot.Position=new Cell{X=at.x,Z=at.z};
                    var wave=(Pawn)c.Place(slot,map);waveId=wave.thingIDNumber;var waveRecord=BossMapComponent.For(wave);
                    LordMaker.MakeNewLord(wave.Faction,new LordJob_AssaultColony(wave.Faction),map,new[]{wave});
                    Check(ChampionBehavior.IsRaid(wave)&&waveRecord.Active&&waveRecord.Template.Phases.All(ph=>ph.Behaviors.Count==0),"fifth-chapter placement and wave Lord select native raid mode");Think(wave,true);
                    Check(Contract(waveRecord.Template)==expected,"raid-mode health, regeneration, adaptation, retaliation and throw parameters stay unchanged");
                    var retaliation=waveRecord.Template.Components.Single(s=>s.Id=="retaliation");
                    var adapted=BossComponents.Incoming(waveRecord,new DamageInfo(DamageDefOf.Bullet,1000,100));
                    Check(adapted.Amount>100&&adapted.Amount<500&&new BossComponentContext(waveRecord,retaliation).Read("charge")==1000,"raid mode retains damage adaptation and original-damage retaliation charge");
                    player.Position=at+IntVec3.East*8;player.Notify_Teleported();
                    // These test factions were created after the quick-test colonists spawned.
                    map.attackTargetsCache.UpdateTarget(player);
                    var wall=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("TableSculpting"),ThingDefOf.Steel);GenSpawn.Spawn(wall,at+IntVec3.East*3,map);
                    waveRecord.Health=76000;Find.TickManager.DebugSetTicksGame(((Find.TickManager.TicksGame/60)+1)*60);owner.MapComponentTick();
                    Check(waveRecord.Health==76077,"raid mode retains 77 HP per second regeneration");
                    var skills=new BossComponentContext(waveRecord,waveRecord.Template.Components.Single(s=>s.Id=="skills"));
                    File.WriteAllText(Path.Combine(Folder,"skill-diagnostics.txt"),"CanGrab="+ChampionSkills.CanGrab(skills,wall)+" available="+skills.Available+" behaviors="+BossMapComponent.BehaviorsAllowed(wave)+" ready="+skills.Read("throwReady")+" tick="+Find.TickManager.TicksGame+" targets="+string.Join(";",map.attackTargetsCache.GetPotentialTargetsFor(wave).Select(t=>t.Thing.Label+" hostile="+t.Thing.HostileTo(wave)+" fog="+t.Thing.Fogged()+" distance="+t.Thing.Position.DistanceTo(wave.Position))));
                    Check(map.listerThings.AllThings.OfType<ChampionThrownThing>().Any(t=>t.Payload==wall),"automatic building throw still executes with native raid movement");
                    c.NodeId="open-campaign";var openSlot=Json.Copy(slot);openSlot.Id="open-audit-champion";openSlot.Position=new Cell{X=center.x-15,Z=center.z};
                    var openPawn=(Pawn)c.Place(openSlot,map);
                    Check(ChampionBehavior.IsRaid(openPawn)&&BossMapComponent.For(openPawn).Template.Phases.All(ph=>ph.Behaviors.Count==0),"explicit free-chapter placements also select raid mode");
                    Check(Contract(BossMapComponent.For(openPawn).Template)==expected,"free-chapter placement retains the unchanged health and skills");
                    BossMapComponent.For(openPawn).Active=false;
                    c.Project=null;c.Map=null;c.Objects.Clear();
                    var faction=Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion"));
                    var worker=(IncidentWorker_RaidEnemy)IncidentDefOf.RaidEnemy.Worker;
                    Check(worker.TryGenerateRaidInfo(Raid(map,faction,39999),out var below,true)&&below.All(p=>p.kindDef.defName!="PMP_ChampionWarrior"),"39999-point raid still has no champion");foreach(var p in below)p.Destroy();
                    var before=new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned);Check(worker.TryExecute(Raid(map,faction,40000)),"actual 40000-point free raid executes");
                    var spawned=map.mapPawns.AllPawnsSpawned.Where(p=>!before.Contains(p)).ToList();
                    var free=spawned.Single(p=>p.kindDef.defName=="PMP_ChampionWarrior");raidId=free.thingIDNumber;
                    Check(ChampionBehavior.IsRaid(free)&&free.GetLord()!=null&&BossMapComponent.For(free).Template.Phases.All(ph=>ph.Behaviors.Count==0),"free raid has exactly one champion following native raid Lord and think tree");Think(free,true);
                    Check(Contract(BossMapComponent.For(free).Template)==expected,"free raid preserves complete champion health and skill contract");
                    LegionRaidChampion.Apply(Raid(map,faction,100000),spawned,100000,true);Check(spawned.Count(p=>p.kindDef.defName=="PMP_ChampionWarrior")==1,"reapplying 100000-point rule cannot add a second champion");
                    var extra=new List<Pawn>();LegionRaidChampion.Apply(Raid(map,faction,100000),extra,100000,true);Check(extra.Count==1&&ChampionBehavior.IsRaid(extra[0]),"100000-point extra champion remains one and has persistent pre-arrival raid orders");extra[0].Destroy();
                    var delayed=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_SanctuaryChampion"),faction);ChampionBehavior.UseRaidLogic(delayed);GenSpawn.Spawn(delayed,CellFinder.RandomClosewalkCellNear(center,map,10),map);
                    Check(ChampionBehavior.IsRaid(delayed)&&BossMapComponent.For(delayed).Template.Phases.All(ph=>ph.Behaviors.Count==0),"delayed champion arrival applies its recorded raid mode");
                    var authored=Json.Read<BossTemplate>(File.ReadAllText(Path.Combine(GenFilePaths.SaveDataFolderPath,"mission-two-champion.json")));
                    var pursuit=owner.Spawn(authored,CellFinder.RandomClosewalkCellNear(center,map,10),faction,"pursuit-audit");ChampionBehavior.UseRaidLogic(pursuit);
                    Check(!ChampionBehavior.IsRaid(pursuit)&&BossMapComponent.For(pursuit).Template.ReviveAfterSeconds==77&&BossMapComponent.For(pursuit).Template.RegenerationPerSecond==3000,"mission-two pursuit champion is excluded from ordinary raid-mode conversion");
                    var held=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_ChampionWarrior"),faction);heldId=held.thingIDNumber;
                    var originalHealth=held.health.hediffSet.hediffs.ToArray();ChampionBehavior.UseRaidLogic(held);
                    var casket=(Building_CryptosleepCasket)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("CryptosleepCasket"));GenSpawn.Spawn(casket,CellFinder.RandomClosewalkCellNear(center,map,15),map);
                    Check(casket.GetDirectlyHeldThings().TryAdd(held)&&LegionBossModes.Get(held)==LegionBossMode.Raid&&originalHealth.SequenceEqual(held.health.hediffSet.hediffs),"unspawned held champion stores pending raid order without modifying health");
                    savedHealth=waveRecord.Health;savedCharge=new BossComponentContext(waveRecord,retaliation).Read("charge");
                    File.WriteAllText(Path.Combine(Folder,"saved-skill-state.txt"),"health="+savedHealth+" charge="+savedCharge);
                    stage=1;loaded=false;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;GameDataSaveLoader.SaveGame("ChampionBehaviorAudit");LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("ChampionBehaviorAudit"));return;
                }
                if(stage==1&&loaded)
                {
                    var held=map.listerThings.AllThings.OfType<Building_CryptosleepCasket>().SelectMany(box=>box.GetDirectlyHeldThings().OfType<Pawn>()).Single(p=>p.thingIDNumber==heldId);
                    Check(LegionBossModes.Get(held)==LegionBossMode.Raid,"pending unspawned order survives native save/load through a real thing holder");
                    held.holdingOwner.Remove(held);GenSpawn.Spawn(held,CellFinder.RandomClosewalkCellNear(map.Center,map,15),map);
                    Check(ChampionBehavior.IsRaid(held)&&Current.Game.GetComponent<LegionBossOrders>().Get(held)==LegionBossMode.Unspecified,"arrival transfers pending order into its Boss record");
                    var fixedPawn=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==fixedId);var wave=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==waveId);var raid=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==raidId);
                    ChampionBehavior.UseRaidLogic(fixedPawn);
                    Check(!ChampionBehavior.IsRaid(fixedPawn)&&BossMapComponent.For(fixedPawn).Template.Phases[0].Behaviors.Count==2,"third-chapter fixed marker and Boss behaviors survive actual save/load");
                    Check(ChampionBehavior.IsRaid(wave)&&ChampionBehavior.IsRaid(raid)&&new[]{wave,raid}.All(p=>BossMapComponent.For(p).Template.Phases.All(ph=>ph.Behaviors.Count==0)),"fifth-chapter and free raid modes survive actual save/load");Think(wave,true);Think(raid,true);
                    var r=BossMapComponent.For(wave);var charge=new BossComponentContext(r,r.Template.Components.Single(s=>s.Id=="retaliation")).Read("charge");
                    Check(r.Health==savedHealth&&Contract(r.Template)==expected&&charge==savedCharge,"raid HP and skill charge persist without resetting on load (HP="+r.Health+"/"+savedHealth+", charge="+charge+"/"+savedCharge+")");
                    var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);Check(errors.Length==0,"no native errors");
                    File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Third-chapter activation, fifth/free raid AI, skill contract, cap and persistence passed.");done=true;UnityEngine.Application.Quit();
                }
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"FAIL.txt"),ex.ToString());done=true;UnityEngine.Application.Quit();}
        }
    }
}
