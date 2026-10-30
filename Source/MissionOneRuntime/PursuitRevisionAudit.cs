using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using Verse;
using Verse.AI;

namespace PMP.Campaign {
    // Invoked only by the explicit isolated mission-two audit.
    static class PursuitRevisionAudit {
        sealed class Context:IBossContext {
            public Pawn PawnValue,TargetValue;public int Clock,Approaches;
            public object Pawn=>PawnValue;public object Target=>TargetValue;public object Map=>PawnValue.Map;
            public double Health=>77777;public double MaxHealth=>77777;public int Phase=>0;public int Tick=>Clock;
            public IDictionary<string,string> State{get;set;}=new Dictionary<string,string>();
            public bool Approach(float range){
                Approaches++;
                return new BossContext(BossMapComponent.For(PawnValue),TargetValue,State).Approach(range);
            }
            public bool FireWeapon(float range)=>false;public bool CastAbility(string name,float range)=>false;public bool DamageTarget(float power,float range)=>false;
        }
        static void Move(Pawn p,IntVec3 at){p.jobs.StopAll();p.Position=at;p.Notify_Teleported();}
        public static void Run(CampaignController c,BossRecord boss,Action<bool,string> check){
            var wallDef=DefDatabase<ThingDef>.GetNamed("PMP_RomulusRedWall");
            var structures=c.Objects.Values.Where(t=>t?.Spawned==true&&(t.def==wallDef||t.def.defName=="PMP_RomulusSpearTurret")).ToList();
            check(structures.All(t=>t.Faction==boss.Proxy.Faction)&&boss.Proxy.Faction.Name==RcText.T("罗穆卢斯"),"all current walls and turrets belong to Romulus");
            var existing=structures.First(t=>t.def==wallDef);check(wallDef.statBases.Single(s=>s.stat==StatDefOf.MaxHitPoints).value==77777&&existing.MaxHitPoints==77775&&existing.GetStatValue(StatDefOf.Flammability)==0,"wall config 77777 HP, native five-point rounding to "+existing.MaxHitPoints+", flammability="+existing.GetStatValue(StatDefOf.Flammability));
            check(c.MapSpec.Interactions.Count(s=>s.BlockCells.Count>0)==32,"32 authored sidewall collapse interactions");
            var cracks=new MissionWallCracks();cracks.Bind(c);check(cracks.HostCount==32,"crack overlay binds all interactive walls without separate wall definitions");
            var hero=c.Actors["hero"];var champion=boss.Proxy;var home=champion.Position;var heroHome=hero.Position;
            var context=new Context{PawnValue=champion,TargetValue=hero};var module=new BossBreachApproach();
            var action=boss.Template.Phases[0].Behaviors.Single(a=>a.Module=="boss.breachApproach");
            var sandbox=new IntVec3(5,0,270);var obstacleCell=sandbox+IntVec3.East;
            foreach(var at in new[]{sandbox,obstacleCell,obstacleCell+IntVec3.East,sandbox+IntVec3.East*5}){
                if(at.GetEdifice(c.Map)!=null)throw new Exception("Native pursuit test area occupied");
                c.Map.terrainGrid.SetTerrain(at,DefDatabase<TerrainDef>.GetNamed("Concrete"));c.Map.roofGrid.SetRoof(at,null);
            }
            Move(champion,sandbox);Move(hero,sandbox+IntVec3.East*5);champion.stances.CancelBusyStanceSoft();
            check(SceneEffect.ResolveCell(c,"champion")==sandbox,"effect follows live champion instead of old marker");
            var events=c.Map.weatherManager.eventHandler.LiveEventsListForReading;int eventCount=events.Count;
            new SceneEffect().Execute(new Act{Target="champion",Text="lightning"},c,"",0);
            check(events.Count==eventCount+1&&Traverse.Create(events.Last()).Field<IntVec3>("strikeLoc").Value==sandbox,"native champion lightning uses its current cell");
            var wall=GenSpawn.Spawn(ThingMaker.MakeThing(wallDef),obstacleCell,c.Map);wall.SetFaction(champion.Faction);
            module.Execute(action,context);check(champion.CurJob==null&&!wall.Destroyed,"champion stops at adjacent wall instead of pathfinding around it");
            for(int tick=6;tick<=84;tick+=6){context.Clock=tick;module.Execute(action,context);}
            check(!wall.Destroyed&&wall.HitPoints==wall.MaxHitPoints,"wall remains until breach duration despite massive HP");
            context.State=Json.Read<Dictionary<string,string>>(Json.Write(context.State));context.Clock=90;module.Execute(action,context);
            check(wall.Destroyed,"breach destroys high-HP wall after exactly 90 active ticks; state roundtrip preserved");
            Move(champion,obstacleCell);var rock=GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.CollapsedRocks),obstacleCell+IntVec3.East,c.Map);
            context.Clock=96;module.Execute(action,context);
            for(int tick=102;tick<=180;tick+=6){context.Clock=tick;module.Execute(action,context);}
            check(!rock.Destroyed,"second obstacle requires a fresh full duration");context.Clock=186;module.Execute(action,context);
            check(rock.Destroyed,"fallen-rock obstacle also delays champion for 90 ticks");
            var untouched=GenSpawn.Spawn(ThingMaker.MakeThing(wallDef),obstacleCell+IntVec3.East,c.Map);
            boss.Active=false;context.Clock=192;module.Execute(action,context);check(!untouched.Destroyed&&champion.CurJob==null,"disabled encounter cannot breach or move");boss.Active=true;untouched.Destroy(DestroyMode.Vanish);
            check(action.Parameters.TryGetValue("maxObstacles",out var maximum)&&maximum=="3","mission-two explicitly limits the whole shortcut to three obstacles");
            var trialWalls=new List<Thing>();
            void Place(IntVec3 at){
                if(at.GetEdifice(c.Map)!=null)throw new Exception("Pursuit wall-limit test area occupied: "+at);
                c.Map.terrainGrid.SetTerrain(at,DefDatabase<TerrainDef>.GetNamed("Concrete"));c.Map.roofGrid.SetRoof(at,null);
                trialWalls.Add(GenSpawn.Spawn(ThingMaker.MakeThing(wallDef),at,c.Map));
            }
            void Reset(){foreach(var t in trialWalls)if(!t.Destroyed)t.Destroy(DestroyMode.Vanish);trialWalls.Clear();Move(champion,sandbox);context.State.Clear();context.Approaches=0;context.Clock=0;}
            Reset();Move(hero,sandbox+IntVec3.East*28);
            // Disconnected walls still belong to the same proposed straight shortcut.
            for(int i=1;i<=12;i++)Place(sandbox+IntVec3.East*(i*2-1));
            for(int tick=0;tick<=240;tick+=6){context.Clock=tick;module.Execute(action,context);}
            check(trialWalls.All(t=>!t.Destroyed)&&context.Approaches>0&&champion.CurJob?.targetA.Cell==hero.Position,"twelve separated walls use native detour, never restart a three-wall breach batch");
            Reset();Move(hero,sandbox+IntVec3.East*8);
            for(int i=1;i<=3;i++)Place(sandbox+IntVec3.East*i);
            module.Execute(action,context);
            for(int tick=6;tick<=90;tick+=6){context.Clock=tick;module.Execute(action,context);}
            check(trialWalls[0].Destroyed&&!trialWalls[1].Destroyed&&!trialWalls[2].Destroyed&&context.Approaches==0,"three-wall shortcut is allowed, each obstacle retains its own 90-tick timer");
            Reset();for(int i=1;i<=3;i++)Place(sandbox+IntVec3.East*i);
            module.Execute(action,context);context.Clock=48;module.Execute(action,context);
            Place(sandbox+IntVec3.East*5);context.Clock=54;module.Execute(action,context);
            check(trialWalls.All(t=>!t.Destroyed)&&context.Approaches==1&&!context.State.ContainsKey("elapsed"),"a fourth wall cancels an already-started breach and clears partial progress");
            trialWalls.Last().Destroy(DestroyMode.Vanish);champion.jobs.StopAll();context.Clock=60;module.Execute(action,context);
            for(int tick=66;tick<=144;tick+=6){context.Clock=tick;module.Execute(action,context);}
            check(!trialWalls[0].Destroyed,"returning to three walls cannot reuse cancelled breach progress");
            context.Clock=150;module.Execute(action,context);check(trialWalls[0].Destroyed,"three remaining walls permit a fresh full-duration breach");
            Reset();Move(hero,sandbox+new IntVec3(8,0,8));
            for(int i=0;i<4;i++)Place(sandbox+new IntVec3(i+1,0,i));
            module.Execute(action,context);
            check(context.Approaches==1&&trialWalls.All(t=>!t.Destroyed),"diagonal corner obstacles count toward the same three-wall limit");
            Reset();
            Move(champion,home);Move(hero,heroHome);
            var escort=c.Map.GetComponent<EscortMapComponent>();var final=c.MapSpec.Objects.Single(o=>o.Id=="extraction").Position;
            check(escort.Waypoints.Last().X==final.X&&escort.Waypoints.Last().Z==final.Z&&escort.Waypoints.Count==2,"guide route ends at extraction without obsolete eastward detour");
            var knight=escort.GuidePawn;var knightHome=knight.Position;var route=escort.Waypoints.ToList();int index=escort.Index;
            var end=new IntVec3(final.X,0,final.Z);Move(knight,end);Move(hero,new IntVec3(end.x-20,0,end.z));escort.Index=escort.Waypoints.Count;
            boss.Active=false;
            for(int i=0;i<31;i++)Find.TickManager.DoSingleTick();
            check(escort.Arrived&&!escort.Running,"guide reaches final marker while waiting for distant player");
            for(int i=0;i<120;i++)Find.TickManager.DoSingleTick();
            check(knight.Position.DistanceTo(end)<=1&&!knight.pather.Moving,"guide stays at extraction for 120 native ticks");
            escort.Begin("knight","hero",route);escort.Index=index;Move(knight,knightHome);Move(hero,heroHome);boss.Active=true;
        }
    }
}
