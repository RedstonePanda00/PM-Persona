using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Runtime;
using Verse;
using UnityEngine;

namespace PMP.Campaign {
    public sealed class NormalIsolationAudit : GameComponent {
        int frames,stage;
        static bool finished;
        string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"NormalIsolationAudit");
        public NormalIsolationAudit(Game game){}
        public override void ExposeData()=>Scribe_Values.Look(ref stage,"normalIsolationStage");
        void Check(bool ok,string text){if(!ok)throw new Exception(text);File.AppendAllText(Path.Combine(Folder,"results.txt"),"PASS "+text+"\n");}
        public override void GameComponentUpdate(){
            if(!GenCommandLine.CommandLineArgPassed("pmp-isolation-normal-audit")||finished||Find.CurrentMap==null||++frames<90)return;
            Directory.CreateDirectory(Folder);
            try {
                var c=Current.Game.GetComponent<CampaignController>();var map=Find.CurrentMap;
                Check(!c.Active&&!c.StoryActive&&c.Map==null,"ordinary game has no campaign project, node or owning map");
                Check(map.GetComponent<MissionThreeMapComponent>()==null,"ordinary map has no third chapter or assault module");
                Check(map.GetComponent<MissionEffectsMapComponent>()==null,"ordinary map does not auto-attach Persona effects");
                Check(map.GetComponent<StealthMapComponent>()==null&&map.GetComponent<EscortMapComponent>()==null&&map.GetComponent<TutorialMapComponent>()==null,"ordinary map does not auto-attach stealth, escort or tutorial");
                Check(map.GetComponent<BossMapComponent>().Records.Count==0,"ordinary map has no campaign boss records");
                Check(!c.Freeze&&!c.Presentation&&MissionEffectsMapComponent.CurrentPresentation==null,"ordinary game has no campaign pause, camera or briefing");
                var runtime=typeof(CampaignController).Assembly;
                Check((bool)AccessTools.Method(runtime.GetType("RimCampaign.Runtime.CampaignGameOver"),"Prefix").Invoke(null,null),"ordinary game retains native defeat handler");
                Check(map.mapPawns.AllPawnsSpawned.All(p=>p.health.hediffSet.hediffs.All(h=>!(h is Hediff_MissionLocal))),"ordinary pawns have no campaign-local protection");
                Check(typeof(MissionScope).Assembly.GetType("PMP.Campaign.MissionForgeLicense")==null,"no campaign research-check bypass exists");
                if(stage==0){stage=1;GameDataSaveLoader.SaveGame("NormalIsolationRoundtrip");GameDataSaveLoader.LoadGame("NormalIsolationRoundtrip");return;}
                File.WriteAllText(Path.Combine(Folder,"complete.txt"),File.ReadAllLines(Path.Combine(Folder,"results.txt")).Length+" checks passed, including ordinary save/load");
                finished=true;Application.Quit();
            }catch(Exception ex){finished=true;File.WriteAllText(Path.Combine(Folder,"failure.txt"),ex.ToString());Log.Error("[NormalIsolationAudit] "+ex);Application.Quit();}
        }
    }
}
