using System;
using System.Collections.Generic;
using RimCampaign.SDK;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using Verse;
using Verse.AI.Group;
using PMP_Persona.VanguardSupport;
namespace PMP.Campaign {
 [StaticConstructorOnStartup]
 public static class MissionThreeFireControlOverlay {
  static MissionThreeFireControlOverlay(){VanguardFireControl.DrawOverlay+=session=>{var c=Current.Game?.GetComponent<CampaignController>();if(MissionScope.Matches(c,"mission-three")&&c.Map?.GetComponent<MissionThreeMapComponent>()?.Active==true){c.GameComponentOnGUI();c.Map.GetComponent<BossMapComponent>()?.MapComponentOnGUI();}};}
 }
 public sealed class MissionThreeMapComponent:CustomMapComponent,ICampaignMapModule {
  bool enabled,started,ended,championDefeated,hubDestroyed;int mask=8;
  CampaignController C=>Current.Game?.GetComponent<CampaignController>();
  public bool Active=>enabled&&MissionScope.Owns(C,map,"mission-three")&&!C.Transitioning;
  public MissionThreeMapComponent(Map map):base(map){}
  public void Attach(CampaignController c,MapComponentSpec spec){enabled=spec.Enabled;}
  public void SetEnabled(bool value){if(!value)Detach();else enabled=true;}
  public void Detach(){enabled=false;map.GetComponent<OrbitalSupportSession>()?.Close();}
  public override void MapRemoved(){Detach();}
  public void Begin(){if(!Active||started)return;started=true;
   var hero=C.Actors["hero"];hero.drafter.Drafted=true;hero.drafter.FireAtWill=true;OrbitalSupportSession.Start(map,true,8);}
  public override void MapComponentTick(){
   if(!Active||C.EntryPending||Find.TickManager.TicksGame%60!=0)return;
   var hero=C.Actors["hero"];var protection=DefDatabase<HediffDef>.GetNamed("PMP_SceneProtection");if(!hero.health.hediffSet.HasHediff(protection))hero.health.AddHediff(protection);
   if(!started||ended)return;
   int desired=C.State.Variables.GetValueOrDefault("sector-2-shield")>0?14:C.State.Variables.GetValueOrDefault("sector-1-shield")>0?12:8;
   var session=map.GetComponent<OrbitalSupportSession>();if(session!=null&&mask!=desired){mask=desired;session.ConfigureWeapons(mask);}
   if(FinalTargetsDestroyed(C)){
    ended=true;C.State.Variables["m3-ended"]=1;map.GetComponent<CombatSuppliesMapComponent>()?.SetEnabled(false);session?.Close();C.Signal("AssaultVictory","mission-three");
   }
  }
  public static bool FinalTargetsDestroyed(CampaignController c){
   var owner=c.Map?.GetComponent<MissionThreeMapComponent>();if(owner==null)return false;
   // Destroyed pawns/buildings are not guaranteed to resolve as save references.
   // Latch actual destruction, never infer a kill merely from a missing reference.
   if(c.Objects.TryGetValue("m3-champion",out var actor)&&actor is Pawn champion&&(champion.Dead||champion.Destroyed))owner.championDefeated=true;
   if(c.Objects.TryGetValue("anomaly-hub",out var hub)&&hub?.Destroyed==true)owner.hubDestroyed=true;
   return owner.championDefeated&&owner.hubDestroyed;
  }
  public override void MapComponentDraw(){if(!Active||!started||ended||Find.CurrentMap!=map)return;if(C.State.Variables.TryGetValue("assault-target-x",out var x)&&C.State.Variables.TryGetValue("assault-target-z",out var z)){var cell=new IntVec3((int)x,0,(int)z);GenDraw.DrawRadiusRing(cell,3);GenDraw.DrawLineBetween(C.Actors["hero"].DrawPos,cell.ToVector3Shifted());}}
  public override void ExposeData(){base.ExposeData();if(Scribe.mode==LoadSaveMode.Saving&&C?.Map==map)FinalTargetsDestroyed(C);Scribe_Values.Look(ref championDefeated,"championDefeated");Scribe_Values.Look(ref hubDestroyed,"hubDestroyed");Scribe_Values.Look(ref enabled,"enabled");Scribe_Values.Look(ref started,"started");Scribe_Values.Look(ref ended,"ended");Scribe_Values.Look(ref mask,"mask",8);}
 }
 [CampaignModule("pmp.m3.start","Action","Begin map-scoped unlimited Prism fire control.")]
 public sealed class StartMissionThree:IActionModule{public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;c.Map.GetComponent<MissionThreeMapComponent>()?.Begin();return true;}}
 [CampaignModule("pmp.m3.neutral","Action","End cooperation with the local campaign Empire faction.")]
 public sealed class MissionThreeNeutral:IActionModule{
  public bool Execute(Act a,IHost host,string source,double delta){
   var c=(CampaignController)host;if(!MissionScope.Matches(c,"mission-three"))return true;
   c.State.Variables["m3-ended"]=1;var hero=c.Actors["hero"];hero.Rotation=Rot4.South;hero.drafter.FireAtWill=true;
   var f=Current.Game.GetComponent<CampaignFactionRegistry>().Resolve(c.MapSpec,"Empire");
   f.SetRelation(new FactionRelation(Faction.OfPlayer,FactionRelationKind.Neutral){baseGoodwill=0});Faction.OfPlayer.RelationWith(f).baseGoodwill=0;
   Messages.Message(RcText.T("索菲亚帝国：盟友 → 中立"),MessageTypeDefOf.NeutralEvent,false);return true;
  }
 }
}

