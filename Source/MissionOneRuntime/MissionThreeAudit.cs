using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using RimCampaign.Core;
using PMP_Persona;
using PMP_Persona.VanguardSupport;
namespace PMP.Campaign {
 public sealed class MissionThreeAudit:GameComponent {
  int stage,lastSector=-1;float start=-1;bool done,equipmentChecked,podsChecked;int constructionWait;HashSet<int> operated=new HashSet<int>();List<string> checks=new List<string>();
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"MissionThreeAudit");
  public MissionThreeAudit(Game game){}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"m3AuditStage");Scribe_Values.Look(ref constructionWait,"constructionWait");Scribe_Values.Look(ref lastSector,"m3AuditSector",-1);Scribe_Collections.Look(ref checks,"m3AuditChecks",LookMode.Value);}
  void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add("PASS "+label);File.WriteAllLines(Path.Combine(Folder,"results.txt"),checks);}
  public override void GameComponentUpdate(){
   if(done||!GenCommandLine.CommandLineArgPassed("pmp-mission-three-audit")||GenCommandLine.CommandLineArgPassed("pmp-m3-profile"))return;
   var c=Current.Game.GetComponent<CampaignController>();if(c?.Active!=true||c.EntryPending||c.Map==null)return;
   Directory.CreateDirectory(Folder);if(start<0)start=Time.realtimeSinceStartup;
   try{
    if(Time.realtimeSinceStartup-start>600)throw new Exception("Timeout stage="+stage+" sector="+lastSector);
    var session=c.Map.GetComponent<OrbitalSupportSession>();
    if(stage==0&&MissionEffectsMapComponent.CurrentPresentation?.Elapsed>15){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"01-briefing.png"));stage=1;}
    if(stage==1&&session?.FireControlReady==true){
     foreach(var rule in c.MapSpec.Rules.Where(r=>r.Id.StartsWith("m3-enemy-refill-"))){
      var action=rule.Actions.First(a=>a.Kind=="rc.reinforce");var slots=c.MapSpec.Objects.Where(o=>o.Group==action.Target&&o.Kind=="PawnKind").ToList();
      var victim=c.Objects[slots[0].Id];victim.Destroy();new TaskReinforce().Execute(action,c,"",0);
      Check(slots.All(o=>c.Objects[o.Id] is Pawn p&&p.Spawned&&!p.Dead),rule.Id+" restores ordinary roster");
     }
     Check(c.MapSpec.Objects.Single(o=>o.Id=="m3-champion").Group=="champion-defense"&&c.MapSpec.Objects.Single(o=>o.Id=="m3-champion").Kind=="PawnKind","Champion outside refill pools and boss resurrection machinery");
     Check(c.MapSpec.Objects.Where(o=>o.Kind=="Thing"&&o.Position.X<110).All(o=>o.Faction=="Empire"),"all camp fixtures belong to Sophia Empire");
     Check(c.NodeId=="mission-three"&&c.Map.Size.x==500&&c.Map.Size.z==400,"third chapter map and node");Check(c.Map.GetComponent<StealthMapComponent>()==null,"no stealth component");Check(session.Unlimited&&session.WeaponAvailable(3)&&!session.WeaponAvailable(1)&&!session.WeaponAvailable(2),"unlimited and heavy-only start");
     File.WriteAllLines(Path.Combine(Folder,"placement-review.txt"),c.MapSpec.Objects.Where(o=>o.Kind=="Thing"&&o.SpawnAtStart).Select(o=>o.Id+" "+o.Def.Name+" "+o.Position.X+","+o.Position.Z+" "+(c.Objects.TryGetValue(o.Id,out var t)?(t.Spawned?"spawned":"LOST"):"MISSING")));
     var hero=c.Actors["hero"];float health=hero.health.summaryHealth.SummaryHealthPercent;hero.TakeDamage(new DamageInfo(DamageDefOf.Bomb,9999,2));Check(!hero.Dead&&!hero.Downed&&hero.health.summaryHealth.SummaryHealthPercent==health,"Mason scene protection");
     Traverse.Create(typeof(PMPH_OrbitalIncoming).Assembly.GetType("PMP_Persona.VanguardSupport.OrbitalDamageAudit17")).Method("Run",c.Map,Folder).GetValue();Check(File.Exists(Path.Combine(Folder,"damage-results.txt")),"orbital mixed-damage regression report");
     Check(!typeof(Skyfaller).IsAssignableFrom(typeof(PMPH_OrbitalIncoming)),"orbital rounds no longer inherit Skyfaller");
     var shot=PMPH_OrbitalIncoming.Spawn(DefDatabase<ThingDef>.GetNamed("PMP_PrismHeavyParticleIncoming"),new IntVec3(40,0,40),c.Map);Traverse.Create(shot).Method("Impact").GetValue();Check(shot.Destroyed&&c.Map.listerThings.AllThings.OfType<Explosion>().Any(),"native non-Skyfaller impact explosion");
     var engageProbe=new Act{Kind="rc.task.engage",Target="sector-1-side",When=new Condition{Op="Equal",Key="audit-unmet-completion",Value=1}};
     Check(!c.TestCondition(engageProbe.When),"combat completion gate initially closed");
     new TaskEngage().Execute(engageProbe,c,"hero",1d/60);
     Check(hero.CurJob?.def==JobDefOf.Goto||hero.CurJob?.def==JobDefOf.AttackStatic||hero.CurJob?.def==JobDefOf.AttackMelee,"closed completion gate still issues an attack or firing-position approach");
     ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"02-fire-control.png"));stage=10;GameDataSaveLoader.SaveGame("MissionThreeRoundtrip");GameDataSaveLoader.LoadGame("MissionThreeRoundtrip");return;
    }
    if(stage==10&&session?.FireControlReady==true){Check(session.Unlimited&&session.WeaponAvailable(3)&&!session.WeaponAvailable(1),"unlimited and weapon gates survive native save/load");Check(c.Map.components.Count(x=>x is MissionThreeMapComponent)==1&&!c.Map.components.Any(x=>x.GetType().Name=="AssaultMapComponent"),"map adapter restored; obsolete assault runner absent");Check(c.MapSpec.Groups.Count(g=>!string.IsNullOrEmpty(g.Actor))==3,"three independently bound authored task trees");Check(Validation.Check(c.Project).Count==0,"authoring project passes reference validation");
     foreach(var name in new[]{"PMP_RomulusDome","PMP_RomulusRedWall"})Check(!DefDatabase<ThingDef>.GetNamed(name).ConfigErrors().Any(),name+" config validation");
     Check(c.MapSpec.Rules.Count(r=>r.ShowOnMap&&r.Events.Any(e=>e.Kind=="Periodic"&&e.Seconds==30))==7,"seven editable 30-second reinforcement handles");
     Check(c.MapSpec.Objects.Where(o=>o.Id.StartsWith("mason-route-")).All(o=>o.Position.Z<174)&&c.MapSpec.Objects.Where(o=>o.Id.StartsWith("empire-route-")).All(o=>o.Position.Z>174),"routes occupy separate districts");
     foreach(var def in DefDatabase<PawnKindDef>.AllDefs.Where(d=>d.defName.StartsWith("PMP_M3")||d.defName=="PMP_SanctuaryChampion"))Check(!def.ConfigErrors().Any(),def.defName+" native config validation");
     stage=2;
    }
    if(stage==11){
     var airborne=c.Objects.Where(k=>k.Key.StartsWith("empire-slot-")).Select(k=>k.Value as Pawn).Where(p=>p!=null&&!p.Spawned&&!p.Dead).ToList();
     Check(airborne.Count>0&&airborne.All(p=>p.ParentHolder!=null&&c.ResolveTriggerPawn(p.ThingID)==p),"in-flight recruits and instance-bound tasks survive native save/load");
     equipmentChecked=true;stage=2;
    }
    if(stage==2){Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
     if(!podsChecked){
      Check(Validation.Actions(c.MapSpec.Entry.Concat(c.MapSpec.Rules.SelectMany(r=>r.Actions))).Where(a=>a.Kind=="rc.reinforce"&&a.Target.StartsWith("empire-")).All(a=>a.Arrival=="Pods"),"all Imperial arrival triggers use drop pods");
      Check(!Validation.Actions(c.MapSpec.Groups.Single(g=>g.Id=="m3-empire-tasks").Actions).Any(a=>a.Kind=="WaitUntil"&&a.When.Key.EndsWith("-shield")),"Imperial assault never waits for shield shutdown before attacking");
      podsChecked=true;
     }
     var troops=c.Objects.Where(k=>k.Key.StartsWith("empire-slot-")).Select(k=>k.Value as Pawn).Where(p=>p!=null&&!p.Dead&&!p.Destroyed).ToList();
     if(troops.Count>48)throw new Exception("Empire living cap exceeded");
     if(!equipmentChecked&&troops.Count>=12){
      Check(troops.All(p=>p.apparel.WornApparel.Any(a=>a.def.defName=="Apparel_ArmorCataphract")&&p.apparel.WornApparel.Any(a=>a.def.defName=="Apparel_ArmorHelmetCataphract")),"all recruits wear cataphract armor and helmets");
      Check(troops.All(p=>p.inventory.innerContainer.Where(t=>t.def.defName=="GoJuice").Sum(t=>t.stackCount)==3),"three go-juice per new recruit");
      Check(troops.All(p=>p.equipment.Primary!=null&&p.apparel.WornApparel.Cast<Thing>().Concat(p.equipment.AllEquipmentListForReading).All(t=>!t.TryGetQuality(out var q)||q>=QualityCategory.Good)),"XML gear quality floor Good");
      Check(troops.Select(p=>p.equipment.Primary.def.defName).Distinct().Count()==3,"all three native weapon types generated");
      var fill=new Act{Target="empire-reinforcements",Arrival="Pods",Value=100,Text="m3-empire-tasks",Origin="m3-empire-refill-0"};new TaskReinforce().Execute(fill,c,"",0);
      Check(c.Objects.Count(k=>k.Key.StartsWith("empire-slot-")&&k.Value is Pawn p&&!p.Dead&&!p.Destroyed)==48,"reinforcement bounded by shared 48 living slots");
      Check(c.Objects.Where(k=>k.Key.StartsWith("empire-slot-")).Select(k=>k.Value).Any(t=>!t.Spawned&&t.ParentHolder!=null),"reinforcements enter native drop pods before landing");
      new TaskReinforce().Execute(fill,c,"",0);Check(c.Objects.Count(k=>k.Key.StartsWith("empire-slot-")&&k.Value is Pawn p&&!p.Dead&&!p.Destroyed)==48,"full roster does not generate excess troops");equipmentChecked=true;
      stage=11;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;GameDataSaveLoader.SaveGame("ReinforcementPodRoundtrip");GameDataSaveLoader.LoadGame("ReinforcementPodRoundtrip");return;
     }
     int sector=Enumerable.Range(1,3).Count(i=>c.State.Variables.GetValueOrDefault("sector-"+i+"-captured")>0);
     var hero=c.Actors["hero"];
     foreach(int i in Enumerable.Range(1,3)){
      if(!operated.Contains(i)&&c.Objects.TryGetValue("shield-control-"+i,out var shield)&&shield.Spawned&&hero.CurJob?.def==JobDefOf.Wait&&hero.CurJob.targetA.Thing==shield){Check(hero.CanReachImmediate(shield,PathEndMode.Touch),"operator touching shield "+i);operated.Add(i);}
      if(c.State.Variables.GetValueOrDefault("sector-"+i+"-shield")>0&&!operated.Contains(i))throw new Exception("Shield advanced without a physical operation: "+i);
     }
     if(sector<3)foreach(var kv in c.Objects.Where(k=>k.Key.StartsWith("s"+(sector+1)+"-")&&(k.Value is Pawn||k.Key.Contains("-turret-"))))if(kv.Value?.Spawned==true)kv.Value.Destroy();
     if(sector!=lastSector){lastSector=sector;File.WriteAllText(Path.Combine(Folder,"progress.txt"),"sector="+sector);
      if(sector>0)Check(c.State.Variables.GetValueOrDefault("sector-"+sector+"-captured")==1,"capture sector "+sector);
      if(sector<3){foreach(var kv in c.Objects.Where(k=>k.Key.StartsWith("s"+(sector+1)+"-")))if(kv.Value?.Spawned==true)kv.Value.Destroy();if(sector==2)c.Objects["m3-champion"].Destroy();}
      else {Check(session.WeaponAvailable(1)&&session.WeaponAvailable(2),"progressive unlocks");Check(c.State.Firings.Where(k=>k.Key.StartsWith("m3-empire-refill-")).Sum(k=>k.Value)>0,"periodic reinforcement rule fired");Check(c.Objects.Any(k=>k.Key.StartsWith("empire-slot-")&&k.Value is Pawn p&&p.Position.x>200),"Empire traversed west-to-east front");stage=20;}
     }
    }
    if(stage==20){Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
     if(c.State.Firings.GetValueOrDefault("m3-empire-refill-3")>0){
      Check(true,"frontline reinforcement continues after third capture");
      var slot=c.MapSpec.Objects.First(o=>o.Group=="empire-reinforcements");c.Objects[slot.Id].Destroy();
      var action=c.MapSpec.Rules.Single(r=>r.Id=="m3-empire-refill-3").Actions[0];new TaskReinforce().Execute(action,c,"",0);
      var origin=c.MapSpec.Rules.Single(r=>r.Id==action.Origin).Position;Check(!c.Objects[slot.Id].Spawned&&c.Objects[slot.Id].ParentHolder!=null,"forward replacement starts in a pod");stage=21;return;

     }
    }
    if(stage==21){
     var slot=c.MapSpec.Objects.First(o=>o.Group=="empire-reinforcements");var replacement=c.Objects[slot.Id];if(!replacement.Spawned)return;
     var origin=c.MapSpec.Rules.Single(r=>r.Id=="m3-empire-refill-3").Position;
     Check(replacement.Position.DistanceTo(new IntVec3(origin.X,0,origin.Z))<24,"forward replacement lands near editable origin");
     Check(c.Actors["hero"].Position.z<174,"Mason remains in southern district");stage=22;constructionWait=Find.TickManager.TicksGame;
     GameDataSaveLoader.SaveGame("OutpostConstructionRoundtrip");GameDataSaveLoader.LoadGame("OutpostConstructionRoundtrip");return;
    }
    if(stage==22){
     Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;
     var engineers=c.Objects.Where(k=>k.Key.StartsWith("engineer-")).Select(k=>k.Value as Pawn).Where(p=>p?.Spawned==true).ToList();
     var generators=c.Map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("ChemfuelPoweredGenerator")).Where(t=>t.Position.x>200).ToList();
     var turrets=c.Map.listerThings.ThingsOfDef(ThingDefOf.Turret_MiniTurret).Where(t=>t.Position.x>200).ToList();
     var pending=c.Map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).Concat(c.Map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)).Count(t=>t.Position.x>200&&t.Faction==engineers.FirstOrDefault()?.Faction);
     if(generators.Count<2||turrets.Count<4||pending>0||generators.Any(t=>t.TryGetComp<CompRefuelable>()?.HasFuel!=true)||turrets.Any(t=>t.TryGetComp<CompPowerTrader>()?.PowerOn!=true)){
      File.WriteAllText(Path.Combine(Folder,"construction.txt"),$"engineers={engineers.Count} generators={generators.Count} turrets={turrets.Count} pending={pending} fueled={generators.Count(t=>t.TryGetComp<CompRefuelable>()?.HasFuel==true)} powered={turrets.Count(t=>t.TryGetComp<CompPowerTrader>()?.PowerOn==true)}\n"+string.Join("\n",engineers.Select(p=>$"{p.ThingID} {p.Position} {p.CurJob} duty={p.mindState.duty?.def.defName}")));
      return;
     }
     Check(engineers.Count==8,"four air-dropped engineers at each of the first two checkpoints; eight separate slots");
     Check(generators.Count==2&&turrets.Count==4&&pending==0,"native hauling and construction finished both outposts after save/load");
     Check(turrets.All(t=>t.TryGetComp<CompPowerTrader>().PowerOn),"constructed turrets powered by refueled generators");
     Check(c.MapSpec.Rules.Where(r=>r.Id.StartsWith("m3-engineers-")).All(r=>c.State.Firings.GetValueOrDefault(r.Id)==1),"engineer drop fires exactly once per captured checkpoint");
     ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"04-built-outposts.png"));c.Objects["anomaly-hub"].Destroy();stage=3;
    }
    if(stage==3&&c.State.Phase=="Succeeded"){
     Check(c.Map.GetComponent<OrbitalSupportSession>()==null,"fire control released on victory");Check(Enumerable.Range(1,3).All(i=>c.State.Variables.GetValueOrDefault("sector-"+i+"-captured")>0),"three captured sectors");Check(Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec,"Empire").RelationKindWith(Faction.OfPlayer)==FactionRelationKind.Neutral,"Empire becomes neutral");ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"03-victory.png"));stage=4;start=Time.realtimeSinceStartup;
    }
    if(stage==4&&Time.realtimeSinceStartup-start>2){done=true;File.WriteAllText(Path.Combine(Folder,"complete.txt"),checks.Count+" checks passed (scripted combat clearance; native walking, contact operation, periodic arrivals, capture, dialogue)");GenScene.GoToMainMenu();LongEventHandler.ExecuteWhenFinished(()=>Application.Quit());}
   }catch(Exception e){done=true;File.WriteAllText(Path.Combine(Folder,"failure.txt"),e.ToString());Log.Error(e.ToString());Application.Quit();}
  }
 }
}

