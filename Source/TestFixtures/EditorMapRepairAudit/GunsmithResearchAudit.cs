using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Runtime;
using PMP.Campaign;
using GunSmithFramework;
using GunSmithFramework.UI;
using ProtossMech.GunSmith;
using Dialog_GunSmithTuning = GunSmithFramework.UI.Dialog_GunSmithTuning;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 public sealed partial class MapDefenseAudit {
  Dialog_GunSmithTuning tuningDialog;
  Thing tuningForge,tuningWeapon;
  void GunsmithResearchAudit(CampaignController c){
   var research=DefDatabase<ResearchProjectDef>.GetNamed("PM_PrinterCertificate");
   var host=WildHuntGunSmithHost.Instance;
   if(stage==0){
    // Isolated test profile has no user settings; exercise the user's enabled elite mode.
    GunSmithFrameworkConfig.EliteGunsmithEnabled=true;
    c.State.Fibers.Clear();c.Communications.Messages.Clear();c.RestoreControl();
    var briefing=AccessTools.Property(typeof(MissionEffectsMapComponent),"CurrentPresentation")?.GetValue(null);
    if(briefing!=null)AccessTools.Method(briefing.GetType(),"End").Invoke(briefing,null);
    Check(typeof(MissionResearch).Assembly.GetType("PMP.Campaign.MissionForgeLicense")==null,"research bypass patch removed");
    Find.ResearchManager.ResetAllProgress();
    MissionResearch.GrantForgeResearch(c);
    Check(!research.IsFinished,"research remains locked before the supplied forge arrives");
    tuningForge=c.Place(c.MapSpec.Objects.Single(o=>o.Id=="m2-forge"),c.Map);
    MissionEquipment.Initialize(c);tuningWeapon=c.Actors["hero"].equipment.Primary;
    var comp=CompModularGun.From(tuningWeapon);
    Check(comp!=null&&host.GetLockReason(comp.Platform)!=null,"native gunsmith lock reproduces missing research");
    c.Map.GetComponent<MissionEffectsMapComponent>().MapComponentTick();
    Check(research.IsFinished&&Find.ResearchManager.GetProgress(research)>=research.baseCost,"chapter completes real research progress");
    Check(host.CanModifyWeapon(tuningForge,tuningWeapon)&&host.GetLockReason(comp.Platform)==null,"native gunsmith permissions now accept the mission weapon");
    ShowTuning("PM_Common_MarksmanOptic");stage=1;frames=0;return;
   }
   if(stage==1&&++frames==35){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"marksman-tuning.png"));return;}
   if(stage==1&&frames>=50){tuningDialog.Close();ShowTuning("PM_R42_Sight_02");stage=2;frames=0;return;}
   if(stage==2&&++frames==35){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"red-dot-tuning.png"));return;}
   if(stage==2&&frames>=50){
    tuningDialog.Close();
    // Exercise the real map-entry hook without generating an unrelated fourth story map.
    var extra=DefDatabase<ResearchProjectDef>.GetNamed("PrecisionRifling");Find.ResearchManager.FinishProject(extra,false,null,false);
    MissionResearch.InitializeOpenResearch(c);Check(extra.IsFinished,"story initialization preserves existing research");
    var previousKind=c.Node.Kind;var previousId=c.Project.Id;c.Node.Kind="Open";c.Project.Id="unrelated";
    MissionResearch.InitializeOpenResearch(c);Check(extra.IsFinished,"unrelated campaigns cannot reset Persona research");
    c.Project.Id=previousId;
    var notify=(Action<CampaignController>)AccessTools.Field(typeof(CampaignController),"NodeInitialized").GetValue(null);
    Check(notify!=null,"open initialization subscribed to framework node-entry event");notify(c);
    var resetFinished=DefDatabase<ResearchProjectDef>.AllDefs.Where(p=>p.IsFinished).Select(p=>p.defName).OrderBy(n=>n).ToArray();
    var resetPrints=DefDatabase<ResearchProjectDef>.AllDefs.Select(p=>Find.ResearchManager.GetTechprints(p)).ToArray();
    Find.ResearchManager.ResetAllProgress();ResearchUtility.ApplyPlayerStartingResearch();
    Check(resetFinished.SequenceEqual(DefDatabase<ResearchProjectDef>.AllDefs.Where(p=>p.IsFinished).Select(p=>p.defName).OrderBy(n=>n)),"open chapter research equals native faction/ideology default starting research");
    Check(resetPrints.SequenceEqual(DefDatabase<ResearchProjectDef>.AllDefs.Select(p=>Find.ResearchManager.GetTechprints(p))),"open chapter techprints equal native starting defaults");
    Check(!research.IsFinished&&!extra.IsFinished,"story certificate and unrelated extra research are cleared at open entry");
    Find.ResearchManager.FinishProject(extra,false,null,false);
    c.Map.GetComponent<MissionEffectsMapComponent>().MapComponentTick();
    Check(extra.IsFinished&&!research.IsFinished,"open gameplay ticks neither reset new progress nor regrant story technology");
    c.Node.Kind=previousKind;
    File.WriteAllLines(Path.Combine(Folder,"starting-research.txt"),resetFinished);
    Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native errors while drawing both tuning windows and resetting research");Finish();
   }
  }
  void ShowTuning(string partName){
   var comp=CompModularGun.From(tuningWeapon);comp.SetPartDefName("Optic",partName);var part=comp.GetPart("Optic");
   Check(part.defName==partName&&part.eliteTuning.Any&&GunSmithFrameworkConfig.IsEliteEnabled(comp.Platform),"elite tuning enabled for "+partName);
   using(GunSmithGuiContext.Push(WildHuntGunSmithHost.Instance))Check(GunSmithShareLoadout.GetLockReason(comp.Platform)==null,"separate tuning dialog license check passes for "+partName);
   float before=tuningWeapon.GetStatValue(StatDefOf.Mass,true);
   Check(comp.SetEliteTuning("Optic",1.4f,part.eliteTuning.magnification==null?1f:1.25f),"tuning applies for "+partName);
   Check(Math.Abs(comp.Tuning.Get("Optic",part).weight-1.4f)<.001f&&Math.Abs(tuningWeapon.GetStatValue(StatDefOf.Mass,true)-before)>.0001f,"tuning persists and changes derived weapon mass for "+partName);
   tuningDialog=new Dialog_GunSmithTuning(tuningForge,tuningWeapon,null,WildHuntGunSmithHost.Instance);Find.WindowStack.Add(tuningDialog);
   if(part.eliteTuning.magnification!=null)Traverse.Create(tuningDialog).Field<Vector2>("scroll").Value=new Vector2(0,180);
  }
 }
}
