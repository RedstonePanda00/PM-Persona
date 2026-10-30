using System;
using System.IO;
using System.Linq;
using PMP.Campaign.BossPlugins;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;
using HarmonyLib;

namespace PMP.Tests
{
    [StaticConstructorOnStartup]
    static class AuditClock
    {
        static AuditClock(){if(GenCommandLine.CommandLineArgPassed("pmp-champion-audit"))new Harmony("pmp.tests.champion.clock").Patch(AccessTools.Method(typeof(TickManager),"DoSingleTick"),prefix:new HarmonyMethod(typeof(AuditClock),nameof(AllowTick)));}
        // A load may advance ticks before the first Update even when speed was saved as Paused.
        // Flight is stepped explicitly below; isolate serialization from incidental combat.
        static bool AllowTick()=>false;
    }
    public sealed class ChampionBossAudit : GameComponent
    {
        int stage,frames,payloadId,championId,victimId,expectedPayloadHp;
        float baseImpactDamage;
        double expectedCharge;
        bool loading,done;
        string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"ChampionAudit");
        public ChampionBossAudit(Game game){}
        void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Path.Combine(Folder,"results.txt"),"PASS "+message+"\n");}
        Pawn SpawnChampion(IntVec3 at)
        {
            var pawn=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_SanctuaryChampion"),Faction.OfPlayer);
            GenSpawn.Spawn(pawn,at,Find.CurrentMap);pawn.jobs.StopAll();return pawn;
        }
        Thing Wall(IntVec3 at)
        {
            var thing=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);GenSpawn.Spawn(thing,at,Find.CurrentMap);return thing;
        }
        static BossComponentContext Context(BossRecord r,string module)=>new BossComponentContext(r,r.Template.Components.Single(c=>c.Module==module));
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage,"championAuditStage");Scribe_Values.Look(ref payloadId,"payloadId");Scribe_Values.Look(ref championId,"championId");Scribe_Values.Look(ref victimId,"victimId");Scribe_Values.Look(ref expectedPayloadHp,"payloadHp");Scribe_Values.Look(ref expectedCharge,"expectedCharge");
            Scribe_Values.Look(ref baseImpactDamage,"baseImpactDamage");
        }
        public override void GameComponentUpdate()
        {
            if(!GenCommandLine.CommandLineArgPassed("pmp-champion-audit")||done||loading||Find.CurrentMap==null)return;
            Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
            if(++frames<90)return;
            Directory.CreateDirectory(Folder);
            try{
                var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                if(stage==0){
                    foreach(var cell in CellRect.CenteredOn(new IntVec3(50,0,50),26)){
                        if(!cell.InBounds(map))continue;
                        foreach(var thing in cell.GetThingList(map).ToList())if(!(thing is Pawn)){if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);else thing.DeSpawn();}
                        map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
                    }
                    foreach(var id in new[]{"boss.adaptation","persona.champion.retaliation","persona.champion.skills"})Check(Modules.Create(id) is BossComponentPlugin,"plugin discovery "+id);
                    var champ=SpawnChampion(new IntVec3(40,0,40));championId=champ.thingIDNumber;
                    var record=BossMapComponent.For(champ);Check(record!=null&&record.Template.ReviveAfterSeconds==0,"regular champion attaches boss preset without revival");
                    Check(champ.GetGizmos().OfType<Command_Action>().Any(c=>c.defaultLabel=="PMP.Champion.Throw".Translate()),"skill exposed through native Pawn Gizmos");
                    Check(champ.abilities==null||!champ.abilities.abilities.Any(a=>a.def.defName.Contains("Champion")),"skill does not add native Ability");
                    var charge=Context(record,"persona.champion.retaliation");
                    double before=record.Health;
                    champ.TakeDamage(new DamageInfo(DamageDefOf.Blunt,40));Check(record.Health==before-40&&charge.Read("charge")==40,"small damage unchanged and recorded");
                    before=record.Health;champ.TakeDamage(new DamageInfo(DamageDefOf.Blunt,4000));
                    Check(Math.Abs(before-record.Health-500)<.01&&charge.Read("charge")==4040,"4000 damage compressed to 500 while raw charge retained");
                    foreach(var name in new[]{"BombSuper","Vaporize"}){
                        before=record.Health;champ.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed(name),4000));
                        Check(Math.Abs(before-record.Health-500)<.01,"common damage remains constrained: "+name);
                    }
                    foreach(var name in new[]{"PMP_OrbitalImpact","PMP_BombDirectHit","PMP_PurificationBeamDamage"}){
                        before=record.Health;champ.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed(name),1000));
                        Check(before-record.Health==1000,"explicit ship custom damage exception: "+name);
                    }
                    var target=SpawnChampion(new IntVec3(43,0,40));var targetRecord=BossMapComponent.For(target);
                    targetRecord.Active=false;double stored=charge.Read("charge");
                    target.TakeDamage(new DamageInfo(DamageDefOf.Blunt,10,instigator:champ));Check(charge.Read("charge")==stored,"fully absorbed hit preserves all charge");
                    targetRecord.Active=true;before=targetRecord.Health;
                    target.TakeDamage(new DamageInfo(DamageDefOf.Blunt,10,instigator:champ));
                    Check(targetRecord.Health<before&&charge.Read("charge")==0,"successful boss hit consumes charge exactly once");
                    Check(Context(targetRecord,"persona.champion.retaliation").Read("charge")==stored+10,"target records amplified hit before its own adaptation");
                    champ.TakeDamage(new DamageInfo(DamageDefOf.Blunt,100));
                    var wall=Wall(new IntVec3(46,0,40));int hp=wall.HitPoints;
                    wall.TakeDamage(new DamageInfo(DamageDefOf.Blunt,10,instigator:champ));
                    Check(charge.Read("charge")==0&&hp-wall.HitPoints>=110,"successful building hit uses and consumes charge");
                    int previous=wall.HitPoints;wall.TakeDamage(new DamageInfo(DamageDefOf.Blunt,10,instigator:champ));
                    Check(previous-wall.HitPoints==15&&charge.Read("charge")==0,"next hit cannot reuse consumed charge (vanilla Blunt building multiplier 1.5)");
                    var skills=Context(record,"persona.champion.skills");
                    var friendly=Wall(new IntVec3(38,0,42));friendly.SetFaction(Faction.OfPlayer);
                    Check(!ChampionSkills.CanGrab(skills,friendly)&&!ChampionSkills.CanGrab(skills,target),"grab rejects friendly building and Pawn");
                    var payload=Wall(new IntVec3(42,0,45));payloadId=payload.thingIDNumber;baseImpactDamage=payload.MaxHitPoints*.5f;expectedPayloadHp=payload.MaxHitPoints-(int)(baseImpactDamage*DamageDefOf.Blunt.buildingDamageFactor);
                    Check(ChampionSkills.CanGrab(skills,payload),"grab accepts nearby factionless building");
                    var victim=SpawnChampion(new IntVec3(47,0,45));victimId=victim.thingIDNumber;
                    var victimRecord=BossMapComponent.For(victim);victimRecord.Template.Components.Clear(); // isolate equal collision damage, including friendly fire
                    victimRecord.TemplateJson=Json.Write(victimRecord.Template);
                    Check(ChampionSkills.TryThrow(skills,payload,new IntVec3(57,0,45)),"throw launch succeeds");
                    var flight=map.listerThings.AllThings.OfType<ChampionThrownThing>().Single();
                    Check(flight.Payload==payload&&!payload.Spawned&&payload.holdingOwner!=null,"flight owns original object without cloning");
                    Check(!ChampionSkills.TryThrow(skills,wall,new IntVec3(55,0,40)),"throw cooldown blocks second launch");
                    champ.TakeDamage(new DamageInfo(DamageDefOf.Blunt,123));expectedCharge=charge.Read("charge");
                    stage=1;loading=true;GameDataSaveLoader.SaveGame("Champion-Component-Audit");
                    LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("Champion-Component-Audit"));return;
                }
                if(stage==1){
                    var champ=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==championId);
                    var record=BossMapComponent.For(champ);var charge=Context(record,"persona.champion.retaliation");
                    Check(charge.Read("charge")==expectedCharge,"raw damage charge persists through actual game save/load: actual="+charge.Read("charge")+" expected="+expectedCharge+" tick="+Find.TickManager.TicksGame+" record="+record.Id+" state="+Json.Write(record.ModuleState));
                    var flight=map.listerThings.AllThings.OfType<ChampionThrownThing>().Single();
                    Check(flight.Payload.thingIDNumber==payloadId,"airborne original object persists through game save/load");
                    var victim=map.mapPawns.AllPawnsSpawned.Single(p=>p.thingIDNumber==victimId);var victimRecord=BossMapComponent.For(victim);double hp=victimRecord.Health;
                    for(int i=0;i<150&&!flight.Destroyed;i++)flight.DoTick();
                    var landed=map.listerThings.AllThings.Single(t=>t.thingIDNumber==payloadId);
                    Check(flight.Destroyed&&landed.Spawned&&Math.Abs(landed.HitPoints-expectedPayloadHp)<=1,"collision applies half maximum HP as vanilla Blunt (including building multiplier) and restores original object");
                    Check(hp-victimRecord.Health==baseImpactDamage+expectedCharge,"collision hits friendly unit and adds reserved charge once");
                    Check(charge.Read("charge")==0,"collision consumes charge; payload self-damage does not reuse it");
                    var ordinary=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);GenSpawn.Spawn(ordinary,new IntVec3(60,0,60),map);
                    Check(BossMapComponent.For(ordinary)==null,"ordinary pawns are not opted into components");
                    champ.TakeDamage(new DamageInfo(DefDatabase<DamageDef>.GetNamed("PMP_OrbitalImpact"),1000000));
                    Check(record.Ended&&champ.Dead&&record.FallenUntil<0,"regular champion dies permanently without resurrection");
                    File.AppendAllText(Path.Combine(Folder,"results.txt"),"COMPLETE\n");done=true;Application.Quit();
                }
            }catch(Exception ex){done=true;File.AppendAllText(Path.Combine(Folder,"results.txt"),"FAIL "+ex+"\n");Log.Error("[ChampionAudit] "+ex);Application.Quit();}
        }
    }
}
