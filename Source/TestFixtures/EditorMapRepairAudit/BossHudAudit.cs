using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Runtime;
using PMP.Campaign;
using PMP_Persona.VanguardSupport;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 public sealed partial class MapDefenseAudit {
  void BossHudAudit(CampaignController c){
   var champion=BossMapComponent.For((Pawn)c.Objects["m3-champion"]);
   if(stage==0){
    c.State.Fibers.Clear();c.RestoreControl();c.Communications.Messages.Clear();
    c.State.Variables["sector-2-captured"]=0;
    Check(champion!=null&&!BossMapComponent.ShouldShowHealthBar(champion),"champion HUD hidden before encounter activation");
    var ordinary=new BossRecord{Proxy=c.Actors["hero"],Active=false};
    Check(!BossMapComponent.ShouldShowHealthBar(ordinary),"any inactive boss has no health bar");ordinary.Active=true;
    Check(BossMapComponent.ShouldShowHealthBar(ordinary),"active boss without an arena gate has a health bar");ordinary.Ended=true;
    Check(!BossMapComponent.ShouldShowHealthBar(ordinary),"ended boss has no health bar");
    c.Map.GetComponent<MissionThreeMapComponent>().Begin();var session=c.Map.GetComponent<OrbitalSupportSession>();var phase=AccessTools.Field(typeof(OrbitalSupportSession),"Phase");phase.SetValue(session,Enum.Parse(phase.FieldType,"FireControl"));stage=1;frames=0;
   }
   if(stage==1&&++frames==30){Check(!BossMapComponent.ShouldShowHealthBar(champion),"orbital support respects inactive boss state");ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"boss-inactive-orbital.png"));}
   if(stage==1&&frames==45)c.State.Variables["sector-2-captured"]=1;
   if(stage==1&&frames==70){Check(BossMapComponent.ShouldShowHealthBar(champion)&&OrbitalBossBarProbe.Draws>0,"activation shows boss HUD during orbital support");ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"boss-active-orbital.png"));}
   if(stage==1&&frames==85){c.Map.GetComponent<OrbitalSupportSession>().Close();champion.Active=false;Check(!BossMapComponent.ShouldShowHealthBar(champion),"explicit deactivation hides boss HUD again");}
   if(stage==1&&frames>=100){Check(!Log.Messages.Any(e=>e.type==LogMessageType.Error),"no native errors during boss HUD state changes");Finish();}
  }
 }
}
