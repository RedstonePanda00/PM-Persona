using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using PMP.Campaign;
using PMP_Persona.Storyteller;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 public sealed class CampaignDefaultsAudit : GameComponent {
  int frames,stage,exit;bool done,loaded;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"CampaignDefaultsAudit");
  public CampaignDefaultsAudit(Game game){}
  public override void ExposeData(){Scribe_Values.Look(ref stage,"defaultsAuditStage");}
  public override void LoadedGame(){loaded=true;}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  static IEnumerable<Page> Pages(Page first){for(var page=first;page!=null;page=page.next)yield return page;}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-campaign-defaults-audit"))return;
   if(done){if(++exit>40)Application.Quit();return;}
   if(Find.CurrentMap==null||++frames<80)return;
   Directory.CreateDirectory(Folder);
   try {
    var c=Current.Game.GetComponent<CampaignController>();var manager=Find.ResearchManager;
    var ideologyType=GenTypes.GetTypeInAnyAssembly("PMP.Campaign.CampaignDefaults");
    var modDefaults=ideologyType==null?null:((IEnumerable<GameComponent>)AccessTools.Field(typeof(Game),"components").GetValue(Current.Game)).First(g=>g.GetType()==ideologyType);
    void EnsureIdeology()=>ideologyType?.GetMethod("EnsureIdeology").Invoke(modDefaults,null);
    var extra=DefDatabase<ResearchProjectDef>.GetNamed("PM_PrinterCertificate");
    if(stage==0){
     Check((ideologyType!=null)==ModsConfig.IdeologyActive,"LoadFolders conditionally loads the culture assembly");
     Check("PMP_CampaignIdeo_Name".CanTranslate()==ModsConfig.IdeologyActive,"LoadFolders conditionally loads culture translations");
     var scenario=DefDatabase<ScenarioDef>.GetNamed("PMP_MissionOne_Scenario").scenario;
     Check(scenario.AllParts.OfType<ScenPart_ShadowOfWar>().Any(),"XML uses campaign-specific world generation part");
     var pages=Pages(scenario.GetFirstConfigPage()).ToList();
     Check(!pages.Any(p=>p is Page_ChooseIdeoPreset||p is Page_ConfigureIdeo),"campaign skips ideology selection pages");
     Check(pages.Any(p=>p is Page_SelectStoryteller)&&pages.Any(p=>p is Page_CreateWorldParams)&&pages.Any(p=>p is Page_CampaignStart),"storyteller, world generation and campaign start pages retained");
     Check(Pages(new Scenario().GetFirstConfigPage()).Any(p=>p is Page_ChooseIdeoPreset)==ModsConfig.IdeologyActive,"ordinary scenarios retain native ideology selection");
     Check(!Pages(scenario.CopyForEditing().GetFirstConfigPage()).Any(p=>p is Page_ChooseIdeoPreset||p is Page_ConfigureIdeo),"native scenario copy also skips ideology selection");
     c.Project=new Project {Id="pmp-mission-one",Maps=new List<MapSpec>{new MapSpec {Id="audit-map",Width=250,Height=250}},Campaigns=new List<RimCampaign.Core.Campaign>{new RimCampaign.Core.Campaign {Id="pmp_archive",Nodes=new List<Node>{new Node {Id="open-campaign",Kind="Open",Map="audit-map"}}}}};
     c.CampaignId="pmp_archive";c.NodeId="open-campaign";c.Map=Find.CurrentMap;
     Current.Game.Scenario=scenario;
     var foreign=Find.FactionManager.AllFactionsListForReading.Where(f=>f!=Faction.OfPlayer&&f.ideos!=null).ToDictionary(f=>f,f=>f.ideos.PrimaryIdeo);
     if(ModsConfig.IdeologyActive)Find.IdeoManager.classicMode=true;
     EnsureIdeology();
     Check(foreign.All(pair=>pair.Key.ideos.PrimaryIdeo==pair.Value),"NPC faction cultures unaffected");
     if(ModsConfig.IdeologyActive){
      var ideo=Faction.OfPlayer.ideos.PrimaryIdeo;
      Check(ideo.Fluid&&ideo.development!=null,"new campaign culture is fluid with native development tracker");
      ideo.Fluid=false;EnsureIdeology();
      Check(ideo.Fluid,"previous fixed campaign default upgrades in place");
      ideo.development.TryAddDevelopmentPoints(3);
      EnsureIdeology();Check(ideo.development.Points==3,"initialization preserves earned development points");
      Check(!Find.IdeoManager.classicMode&&Find.IdeoManager.IdeosListForReading.Count(i=>i.initialPlayerIdeo)==1,"migration activates full ideology and a single initial player culture");
      Check(ideo.name=="PMP_CampaignIdeo_Name".Translate()&&ideo.memberName=="PMP_CampaignIdeo_Member".Translate(),"localized Star Eaters culture and hunter member name");
      Check(ideo.StructureMeme.defName=="Structure_Ideological"&&ideo.memes.Count==1,"atheist structure without invented extra memes");
      foreach(string name in new[]{"Cannibalism_Abhorrent","OrganUse_HorribleNoSell","Slavery_Abhorrent","Execution_Abhorrent"}){
       Check(ideo.HasPrecept(DefDatabase<PreceptDef>.GetNamed(name))&&HonorRules.IdeologyFactor(name,false,false)==2,"honor-loss behavior condemned: "+name);
      }
      Check(ideo.HasPrecept(DefDatabase<PreceptDef>.GetNamed("Research_VeryFast")),"native very-fast research precept");
      var anomaly=DefDatabase<PreceptDef>.GetNamedSilentFail("VoidStudy_VeryEfficient");
      Check(ModsConfig.AnomalyActive?anomaly!=null&&ideo.HasPrecept(anomaly):anomaly==null||!ideo.HasPrecept(anomaly),"Anomaly study precept follows DLC activation");
      Check(ideo.FirstIncompatiblePreceptPair()==default(Pair<Precept,Precept>),"no incompatible precepts");
      Check(ideo.FirstRitualMissingTarget()==null&&ideo.FirstConsumableBuildingMissingRitual()==null,"generated rituals and buildings remain valid");
      Check(c.Map.mapPawns.FreeColonistsSpawned.All(p=>p.Ideo==ideo),"existing player colonists converted to campaign culture");
      EnsureIdeology();Check(Faction.OfPlayer.ideos.PrimaryIdeo==ideo,"culture initialization is idempotent");
     }
     var baseline=DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p=>Faction.OfPlayer.def.startingResearchTags?.Any(p.HasTag)==true||Faction.OfPlayer.ideos?.PrimaryIdeo?.memes.Any(m=>m.startingResearchProjects.Contains(p))==true).ToHashSet();
     foreach(var research in DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p=>p.tab?.defName=="ProtossTech"))manager.FinishProject(research,false,null,false);
     manager.FinishProject(extra,false,null,false);manager.SetCurrentProject(extra);
     var partial=DefDatabase<ResearchProjectDef>.AllDefsListForReading.First(p=>!baseline.Contains(p)&&p!=extra&&p.knowledgeCategory==null&&p.tab?.defName!="ProtossTech");
     manager.AddProgress(partial,13);
     if(ModsConfig.AnomalyActive){var project=DefDatabase<ResearchProjectDef>.AllDefsListForReading.First(p=>p.knowledgeCategory!=null);manager.SetCurrentProject(project);manager.FinishProject(project,false,null,false);manager.SetCurrentProject(project);}
     c.CampaignId="other";MissionResearch.InitializeOpenResearch(c);Check(extra.IsFinished,"unrelated campaigns do not reset research");c.CampaignId="pmp_archive";
     // LoadedGame is also the migration path for existing open-chapter saves.
     modDefaults?.LoadedGame();
     MissionResearch.InitializeOpenResearch(c);
     Check(DefDatabase<ResearchProjectDef>.AllDefsListForReading.All(p=>p.IsFinished==baseline.Contains(p)),"only native starting technologies survive the reset");
     Check(manager.GetProgress(partial)==0&&manager.GetProject()==null,"extra partial progress and active research cleared");
     if(ModsConfig.AnomalyActive)Check(manager.CurrentAnomalyKnowledgeProjects.All(p=>p.project==null),"active Anomaly projects cleared");
     manager.AddProgress(extra,37);MissionResearch.InitializeOpenResearch(c);Check(Math.Abs(manager.GetProgress(extra)-37)<.001f,"repeated initialization preserves new open-world research");
     stage=1;GameDataSaveLoader.SaveGame("CampaignDefaultsAudit");LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("CampaignDefaultsAudit"));return;
    }
    if(stage==1&&loaded){
     Check(Math.Abs(manager.GetProgress(extra)-37)<.001f,"new research progress survives save/load migration guard");
     if(ModsConfig.IdeologyActive)Check(Faction.OfPlayer.ideos.PrimaryIdeo.name=="PMP_CampaignIdeo_Name".Translate(),"campaign culture survives save/load");
     if(ModsConfig.IdeologyActive)Check(Faction.OfPlayer.ideos.PrimaryIdeo.Fluid&&Faction.OfPlayer.ideos.PrimaryIdeo.development.Points==3,"fluid culture and development points survive save/load");
     var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);Check(errors.Length==0,"no native errors");
     File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Campaign defaults passed. Ideology="+ModsConfig.IdeologyActive+" Anomaly="+ModsConfig.AnomalyActive);done=true;
    }
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text));done=true;}
  }
 }
}
