using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PMP.Tests {
 [HarmonyPatch(typeof(Root_Play),nameof(Root_Play.SetupForQuickTestPlay))]
 static class MapDefenseSetup {
  static void Postfix(){if(!GenCommandLine.CommandLineArgPassed("pmp-map-defense-audit"))return;
   Current.Game.Scenario=DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario;Find.Scenario.PreConfigure();
   var c=Current.Game.GetComponent<CampaignController>();var def=DefDatabase<CampaignDef>.GetNamed("PMP_MissionOne");
   c.Prepare(def.LoadProject(),def.campaignId,GenCommandLine.CommandLineArgPassed("pmp-gunsmith-research-audit")?"mission-two":"mission-three");
   Find.GameInitData.startingAndOptionalPawns=c.Actors.Values.ToList();Find.GameInitData.startingPawnCount=c.Actors.Count;
   Find.GameInitData.mapSize=c.MapSpec.Width;Find.GameInitData.mapGeneratorDef=DefDatabase<MapGeneratorDef>.GetNamed("RC_Pocket");
  }
 }
 public sealed partial class MapDefenseAudit:GameComponent {
  const string Prefix="m3-defense-20261024-";bool done;int stage,frames;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"MapDefenseAudit");
  public MapDefenseAudit(Game game){}
  void Check(bool ok,string message){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+message+"\n");if(!ok)throw new Exception(message);}
  IEnumerable<IntVec3> Footprint(Placement p){var size=p.Kind=="Thing"?DefDatabase<ThingDef>.GetNamed(p.Def.Name).size:IntVec2.One;return GenAdj.OccupiedRect(new IntVec3(p.Position.X,0,p.Position.Z),new Rot4(p.Rotation),size);}
  public override void GameComponentUpdate(){
   if(done||!GenCommandLine.CommandLineArgPassed("pmp-map-defense-audit"))return;
   var c=Current.Game.GetComponent<CampaignController>();if(c?.Map==null)return;if(!GenCommandLine.CommandLineArgPassed("pmp-ending-audit"))Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
   if(c.Transitioning||c.EntryPending)return;Directory.CreateDirectory(Folder);
   try{
    if(GenCommandLine.CommandLineArgPassed("pmp-gunsmith-research-audit")){GunsmithResearchAudit(c);return;}
    if(GenCommandLine.CommandLineArgPassed("pmp-ending-audit")){EndingAudit(c);return;}
    if(GenCommandLine.CommandLineArgPassed("pmp-combat-supplies-audit")){CombatSuppliesAudit(c);return;}
    if(GenCommandLine.CommandLineArgPassed("pmp-boss-hud-audit")){BossHudAudit(c);return;}
    if(GenCommandLine.CommandLineArgPassed("pmp-legion-stat-audit")){LegionStatAudit(c);return;}
    if(GenCommandLine.CommandLineArgPassed("pmp-hull-reference")){HullReference(c);return;}
    if(stage==0){
     Check(c.Map.Size.x==500&&c.Map.Size.z==400,"rebuilt third map generated at saved dimensions");
     var all=c.MapSpec.Objects;var added=all.Where(p=>p.Id.StartsWith(Prefix)&&p.Kind!="Marker").ToList();
     Check(added.Count==365,"all 365 added buildings and units present in project");
     var occupancy=new Dictionary<IntVec3,List<string>>();
     foreach(var p in all.Where(p=>new[]{"Thing","Pawn","PawnKind","Boss"}.Contains(p.Kind)&&p.Def.Name!="HiddenConduit"&&p.Layer!="InitializedNature"))foreach(var cell in Footprint(p)){
      if(!occupancy.TryGetValue(cell,out var ids))occupancy[cell]=ids=new List<string>();ids.Add(p.Id);
     }
     foreach(var p in added){
      Check(Footprint(p).All(cell=>cell.InBounds(c.Map)&&occupancy[cell].Count==1),"native footprint clear: "+p.Id);
      Check(c.Objects.TryGetValue(p.Id,out var thing)&&thing.Spawned&&!thing.Destroyed,"spawned: "+p.Id);
      Check(thing.Position.x==p.Position.X&&thing.Position.z==p.Position.Z,"spawn kept authored position: "+p.Id);
      Check(thing.Faction==Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec,"Hostile"),"Romulus faction: "+p.Id);
     }
     foreach(var p in added.Where(p=>p.Kind=="PawnKind"))Check(p.Position.Z<174,"new troops confined to southern district");
     Check(added.Count(p=>p.Def.Name=="Turret_AutoChargeBlaster")==32&&added.Count(p=>p.Def.Name=="PMP_RomulusSpearTurret")==18,"southern and northern turret counts");
     Check(DefDatabase<ThingDef>.GetNamed("PMP_RomulusSpearTurret").statBases.Single(s=>s.stat==StatDefOf.MaxHitPoints).value==3773,"spear turret authored MaxHitPoints is 3773");
     Check(c.Objects.Where(k=>k.Key.StartsWith(Prefix)&&k.Value.def.defName=="PMP_RomulusSpearTurret").All(k=>k.Value.MaxHitPoints==3775&&k.Value.HitPoints==3775),"native MaxHitPoints rounding (>200 to nearest five) yields 3775/3775 HP");
     foreach(var a in c.MapSpec.Entry.Where(a=>a.Id.StartsWith(Prefix)&&a.Kind=="rc.defendGroup"))new DefendGroup().Execute(a,c,"",0);
     Check(added.Where(p=>p.Kind=="PawnKind").All(p=>(c.Objects[p.Id] as Pawn)?.GetLord()?.LordJob is LordJob_DefendPoint),"all new infantry and centipedes receive native defend-point duties");
     var mechWeapons=added.Where(p=>p.Def.Name.StartsWith("Mech_Centipede")).Select(p=>(c.Objects[p.Id] as Pawn)?.equipment?.Primary?.def.defName).ToList();
     Check(mechWeapons.Count==10&&mechWeapons.All(n=>!string.IsNullOrEmpty(n))&&mechWeapons.Distinct().Count()==2,"ten centipedes generated with both requested weapon variants");
     File.WriteAllText(Path.Combine(Folder,"centipede-weapons.txt"),string.Join("\n",mechWeapons));
     var hero=c.Actors["hero"];var routes=all.Where(p=>p.Id.StartsWith("mason-route-")).OrderBy(p=>p.Id).ToList();
     for(int i=1;i<routes.Count;i++){var from=new IntVec3(routes[i-1].Position.X,0,routes[i-1].Position.Z);var to=new IntVec3(routes[i].Position.X,0,routes[i].Position.Z);Check(c.Map.reachability.CanReach(from,to,PathEndMode.OnCell,TraverseParms.For(hero)),"Mason route reachable "+routes[i-1].Id+" -> "+routes[i].Id);}
     Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native errors while generating revised third map");
     // End only this isolated audit's briefing so captures show the actual map.
     c.State.Fibers.Clear();c.RestoreControl();c.Communications.Messages.Clear();c.Map.fogGrid.ClearAllFog();
     var presentation=AccessTools.Property(AccessTools.TypeByName("PMP.Campaign.MissionEffectsMapComponent"),"CurrentPresentation")?.GetValue(null);
     if(presentation!=null)AccessTools.Method(presentation.GetType(),"End").Invoke(presentation,null);
     if(Revision)RunBehaviorChecks(c);
     Find.CameraDriver.SetRootPosAndSize(new Vector3(329,0,143),24);stage=1;frames=0;return;
    }
    if(stage==1&&++frames==25)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"south-defenses.png"));
    if(stage==1&&frames>=50){Find.CameraDriver.SetRootPosAndSize(new Vector3(268,0,246),42);stage=2;frames=0;}
    if(stage==2&&++frames==25)ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"north-defenses.png"));
    if(stage==2&&frames>=50){Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no errors after drawing both districts");if(Revision){WallGallery(c);stage=3;frames=0;}else Finish();}
    if(stage>=3)RevisionCaptures(c);
   }catch(Exception e){done=true;File.WriteAllText(Path.Combine(Folder,"failure.txt"),e.ToString());Application.Quit();}
  }
  void Finish(){File.WriteAllText(Path.Combine(Folder,"complete.txt"),"PASS");done=true;Application.Quit();}
 }
}
