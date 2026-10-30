using System;
using System.IO;
using System.Linq;
using System.Text;
using PMP_Persona;
using PMP_Persona.Romulus;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Tests
{
    public sealed class RomulusUnitsAudit : GameComponent
    {
        int frames, presentationFrames; bool done, presenting;
        string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath,"RomulusAudit");
        public RomulusUnitsAudit(Game game) { }
        void Check(bool value,string label) { if(!value)throw new Exception(label);File.AppendAllText(Path.Combine(Folder,"results.txt"),"PASS "+label+"\n"); }
        Pawn Spawn(string name,Faction faction,IntVec3 cell)
        {
            var p=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(name),faction);
            GenSpawn.Spawn(p,cell,Find.CurrentMap);p.jobs.StopAll();return p;
        }
        public override void GameComponentUpdate()
        {
            if(presenting)
            {
                if(++presentationFrames==15)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"native-units.png"));
                if(presentationFrames>=30){presenting=false;Application.Quit();}
                return;
            }
            if(!GenCommandLine.CommandLineArgPassed("pmp-romulus-audit")||done||Find.CurrentMap==null||++frames<90)return;
            done=true; Directory.CreateDirectory(Folder);
            try
            {
                foreach(var name in new[]{"PMP_Warhound","PMP_LegionGuard","PMP_LegionChosen","PMP_LegionSpear","PMP_LegionShield","PMP_LegionSpearShot"})
                {
                    var def=DefDatabase<ThingDef>.GetNamed(name);
                    var errors=def.ConfigErrors().ToList();
                    Check(errors.Count==0,name+" native ConfigErrors: "+string.Join("; ",errors));
                }
                var spearDef=DefDatabase<ThingDef>.GetNamed("PMP_LegionSpear");
                Check(DefDatabase<ThingDef>.GetNamed("PMP_RomulusRedWall").label=="罗慕路斯结构墙","Chinese wall label injected from localization");
                Check(DefDatabase<PawnKindDef>.GetNamed("PMP_CampaignMason").label=="梅森","Chinese campaign pawn label injected from localization");
                Check(DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario.name=="噬星者 第一关 档案站","Chinese nested scenario name injected from localization");
                Check(spearDef.recipeMaker==null&&!DefDatabase<RecipeDef>.AllDefsListForReading.Any(d=>d.defName=="Make_PMP_LegionSpear"),"loot spear does not unlock manufacturing");
                foreach(var name in new[]{"PMP_LegionSpear","PMP_LegionShield"})
                {
                    var equipment=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name));
                    Check(equipment.SmeltProducts(1f).Any(p=>p.stackCount>0),name+" yields native smelting products");
                }
                var map=Find.CurrentMap;var origin=new IntVec3(45,0,45);
                foreach(var cell in CellRect.CenteredOn(origin,25))
                {
                    foreach(var thing in cell.GetThingList(map).ToList()) if(!(thing is Pawn))
                    {
                        if(thing.def.destroyable)thing.Destroy(DestroyMode.Vanish);
                        else thing.DeSpawn();
                    }
                    map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil);
                }
                var faction=FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion")));
                Find.FactionManager.Add(faction);
                var hound=Spawn("PMP_Warhound",faction,origin);
                var guard=Spawn("PMP_LegionGuard",faction,origin+new IntVec3(5,0,0));
                var chosen=Spawn("PMP_LegionChosen",faction,origin+new IntVec3(8,0,0));
                var mc=map.GetComponent<LegionMapComponent>();
                Check(guard.equipment.Primary?.def.defName=="PMP_LegionSpear","guard issued independent spear");
                Check(guard.apparel.WornApparel.Single().def.defName=="PMP_LegionShield","guard issued separate belt shield");
                Check(chosen.equipment.Primary==null&&chosen.apparel.WornApparel.Count==0,"chosen issued no weapon or shield");
                Check(guard.GetComp<CompNeverDowned>()!=null&&chosen.GetComp<CompNeverDowned>()!=null,"advanced units reuse never-downed component");
                foreach(var p in new[]{hound,guard,chosen})
                {
                    Check(p.GetStatValue(StatDefOf.PsychicSensitivity)==0,p.def.defName+" psychic sensitivity zero");
                    Check(p.Drawer.renderer!=null,p.def.defName+" native renderer available");
                    foreach(var rot in new[]{Rot4.South,Rot4.East,Rot4.North,Rot4.West})Check(p.Graphic.MatAt(rot)?.mainTexture!=null,p.def.defName+" texture "+rot);
                    var sb=new StringBuilder();sb.AppendLine(p.def.defName+" healthScale="+p.HealthScale);
                    foreach(var part in p.RaceProps.body.AllParts)sb.AppendLine(part.Label+": "+part.def.hitPoints+" × "+p.HealthScale+" = "+part.def.GetMaxHealth(p));
                    foreach(var s in new[]{"MoveSpeed","IncomingDamageFactor","ArmorRating_Sharp","ArmorRating_Blunt","ArmorRating_Heat","MeleeDamageFactor","AimingDelayFactor","RangedCooldownFactor"})sb.AppendLine(s+"="+p.GetStatValue(DefDatabase<StatDef>.GetNamed(s),true,-1));
                    File.WriteAllText(Path.Combine(Folder,p.def.defName+".txt"),sb.ToString());
                }
                var r=LegionDoctrine.Record(guard);var c=LegionDoctrine.Record(chosen);var h=LegionDoctrine.Record(hound);
                Check(r.Advanced&&c.Advanced&&!h.Advanced&&h.Blood,"rank eligibility guard/chosen only; hound bloodbattle");
                r.AddMerit(100);c.AddMerit(700);mc.BeginCycle();mc.UpdateRecipient(h);
                Check(h.AuraRank==3&&h.AuraSource==chosen,"highest in-range rank wins without stacking");
                chosen.Position=origin+new IntVec3(24,0,0);mc.BeginCycle();mc.UpdateRecipient(h);
                Check(h.AuraRank==1&&h.AuraSource==guard,"leaving strongest source range downgrades to remaining source");
                guard.Position=origin+new IntVec3(23,0,0);mc.BeginCycle();mc.UpdateRecipient(h);Check(h.AuraRank==0,"leaving all aura ranges clears effect");
                chosen.Position=origin+new IntVec3(8,0,0);chosen.SetFaction(Faction.OfPlayer);mc.BeginCycle();mc.UpdateRecipient(h);Check(h.AuraRank==0,"hostile factions never share inspiration");
                chosen.SetFaction(faction);
                var leg=guard.RaceProps.body.AllParts.First(p=>p.def.defName.EndsWith("MechanicalLeg"));
                float before=PawnCapacityUtility.CalculatePartEfficiency(guard.health.hediffSet,leg);
                var wound=(Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut,guard,leg);wound.Severity=leg.def.GetMaxHealth(guard)*.7f;guard.health.AddHediff(wound);
                Check(Math.Abs(PawnCapacityUtility.CalculatePartEfficiency(guard.health.hediffSet,leg)-before)<.001f,"injury no longer reduces intact part efficiency");
                Check(guard.health.hediffSet.GetPartHealth(leg)<leg.def.GetMaxHealth(guard),"real damaged HP is preserved");
                Check(guard.health.hediffSet.PainTotal==0,"bloodbattle suppresses pain");
                guard.health.RemoveHediff(wound);
                var missing=HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart,guard,leg);guard.health.AddHediff(missing);
                Check(PawnCapacityUtility.CalculatePartEfficiency(guard.health.hediffSet,leg)==0,"bloodbattle does not restore missing limbs");
                guard.health.RemoveHediff(missing);
                var torso=guard.RaceProps.body.corePart;
                var battleWound=(Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut,guard,torso);
                battleWound.Severity=400;guard.health.AddHediff(battleWound);r.UpdateInjury();r.SetAura(0,null);
                Check(r.BloodStage==2,"severe real injury activates bloodbattle stage 2");
                Check(Math.Abs(guard.GetStatValue(StatDefOf.MoveSpeed,true,-1)-4.6f*1.2f)<.01f,"bloodbattle movement bonus applied after injury protection");
                Check(Math.Abs(guard.GetStatValue(StatDefOf.MeleeDamageFactor,true,-1)-1.25f)<.01f,"bloodbattle melee bonus 25 percent");
                r.SetAura(3,chosen);
                Check(Math.Abs(guard.GetStatValue(StatDefOf.MoveSpeed,true,-1)-4.6f*1.2f*1.15f)<.01f,"rank inspiration composes once with bloodbattle");
                guard.health.RemoveHediff(battleWound);r.UpdateInjury();r.SetAura(0,null);
                Check(r.BloodStage==0,"repair removes bloodbattle bonus");
                var shield=guard.apparel.WornApparel.Single().GetComp<CompLegionShield>();
                Check(shield.Energy>.99f,"new shield starts full");
                var shot=new DamageInfo(DamageDefOf.Bullet,100);shield.PostPreApplyDamage(ref shot,out bool absorbed);
                Check(absorbed&&Math.Abs(shield.Energy-.9f)<.01f,"native shield absorbs 100 damage from 1000 capacity");
                var emp=new DamageInfo(DamageDefOf.EMP,1);shield.PostPreApplyDamage(ref emp,out absorbed);
                Check(shield.Energy<=0,"EMP breaks shield");
                guard.stances.stunner.StunFor(120,null);Check(!guard.stances.stunner.Stunned,"existing component prevents stun");
                var human=Spawn("Colonist",Faction.OfPlayer,origin+new IntVec3(0,0,8));
                Check(LegionDoctrine.Record(human)==null,"ordinary pawn receives no doctrine");
                var equip=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_LegionShield"));human.apparel.Wear(equip);
                Check(LegionDoctrine.Record(human)?.Advanced==true,"advanced equipment grants qualification outside Romulus faction");
                LegionDoctrine.Record(human).AddMerit(100);human.apparel.Remove(equip);LegionDoctrine.Refresh(human);
                Check(!LegionDoctrine.Record(human).Blood&&LegionDoctrine.Record(human).Merit==100,"unequip disables effects and preserves personal merit");
                human.apparel.Wear(equip);Check(LegionDoctrine.Record(human).Rank==1,"re-equip restores personal rank");
                float prior=r.Merit;mc.RecordDamage(human,guard,10);mc.RecordDamage(human,chosen,30);mc.Award(human);
                Check(r.Merit>prior,"merit divided among valid attackers");float saved=r.Merit;mc.RecordDamage(human,guard,10);mc.Award(human);Check(r.Merit==saved,"same victim cannot be farmed after resurrection");
                r.AddMerit(100000);Check(r.Merit==700&&r.Rank==3,"finite promotion cap");
                r.Merit=0;c.Merit=0;
                var casualty=Spawn("Colonist",Faction.OfPlayer,origin+new IntVec3(0,0,12));
                float firstDamage=casualty.TakeDamage(new DamageInfo(DamageDefOf.Bullet,5,10,-1,guard,casualty.RaceProps.body.corePart)).totalDamageDealt;
                var lethal=new DamageInfo(DamageDefOf.Bullet,999,10,-1,chosen,casualty.RaceProps.body.corePart);
                lethal.SetIgnoreInstantKillProtection(true);
                float lastDamage=casualty.TakeDamage(lethal).totalDamageDealt;
                Check(casualty.Dead,"real lethal hit kills target");
                float pool=Mathf.Clamp(casualty.kindDef.combatPower,20,200);
                Check(Math.Abs(r.Merit-pool*firstDamage/(firstDamage+lastDamage))<.01f&&Math.Abs(r.Merit+c.Merit-pool)<.01f,"real lethal hit credited once using applied damage: first="+firstDamage+" last="+lastDamage+" guard="+r.Merit+" chosen="+c.Merit+" pool="+pool+" hostile="+guard.HostileTo(casualty));
                // A sealed wall ring is unreachable to normal pawns, but accessible to this comp.
                hound.Position=origin;var goal=origin+new IntVec3(4,0,0);var walls=new System.Collections.Generic.List<Thing>();
                foreach(var cell in CellRect.CenteredOn(goal,1).EdgeCells)
                {var w=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);GenSpawn.Spawn(w,cell,map);walls.Add(w);}
                var vault=hound.GetComp<CompWallVault>();var path=vault.FindPath(goal,PathEndMode.OnCell,false);
                Check(path!=null&&path.Zip(path.Skip(1),(a,b)=>(a-b).LengthHorizontalSquared).Contains(4),"hound path includes single-wall vault");
                Check(!map.reachability.CanReach(hound.Position,goal,PathEndMode.OnCell,TraverseParms.For(human)),"normal pawn still cannot cross same wall ring");
                hound.pather.StartPath(goal,PathEndMode.OnCell);bool sawVault=false;
                for(int i=0;i<250&&hound.pather.Moving;i++)
                {
                    hound.pather.PatherTick();sawVault|=vault.Vaulting;
                    if(vault.Vaulting)Check(hound.Rotation==Rot4.East,"east vault uses east facing");
                }
                Check(sawVault&&hound.Position==goal,"native path follower executes vault and arrives");
                hound.Position=origin;
                foreach(var cell in CellRect.CenteredOn(goal,2).EdgeCells)
                {var w=ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel);GenSpawn.Spawn(w,cell,map);walls.Add(w);}
                Check(vault.FindPath(goal,PathEndMode.OnCell,false)==null,"two-thick wall blocks vault");
                foreach(var w in walls)w.Destroy(DestroyMode.Vanish);
                human.DeSpawn();
                int index=0;
                foreach(var p in new[]{hound,guard,chosen})
                {
                    p.Position=origin+new IntVec3(index++*4,0,0);p.jobs.StopAll();p.Rotation=Rot4.South;
                    var job=JobMaker.MakeJob(JobDefOf.Wait_Combat);job.expiryInterval=6000;p.jobs.StartJob(job,JobCondition.InterruptForced);
                }
                Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                Find.CameraDriver.SetRootPosAndSize((origin+new IntVec3(4,0,0)).ToVector3Shifted(),8);
                File.WriteAllText(Path.Combine(Folder,"complete.txt"),"All assertions passed.");
                presenting=true;
            }
            catch(Exception e){File.WriteAllText(Path.Combine(Folder,"failure.txt"),e.ToString());Log.Error(e.ToString());}
            if(!presenting)Application.Quit();
        }
    }
}
