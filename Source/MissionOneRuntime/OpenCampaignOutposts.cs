using System;
using System.Linq;
using PMP_Persona.Romulus;
using RimCampaign.Runtime;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PMP.Campaign {
    [StaticConstructorOnStartup]
    static class OpenOutpostStart {
        static OpenOutpostStart(){CampaignController.NodeInitialized+=c=>{
            if(c.Project?.Id=="pmp-mission-one"&&c.NodeId=="open-campaign")
                Current.Game.GetComponent<OpenCampaignOutposts>().Begin(c.Map.Tile);
        };}
    }
    public sealed class OpenCampaignOutposts : GameComponent {
        int hardCap=-1;
        long lastDay;
        float longitude;
        Faction legion;
        public int HardCap=>hardCap;
        public OpenCampaignOutposts(Game game){}
        public static int SettlementCount=>Find.WorldObjects.SettlementBases.Count(s=>s.Tile.Layer==Find.WorldGrid.Surface);
        long Day=>(Find.TickManager.TicksAbs+GenDate.LocalTicksOffsetFromLongitude(longitude))/60000;
        public void Begin(PlanetTile landingTile){
            if(hardCap>=0)return;
            hardCap=SettlementCount+4;
            longitude=Find.WorldGrid.LongLatOf(landingTile).x;
            lastDay=Day;
        }
        public override void ExposeData(){
            Scribe_Values.Look(ref hardCap,"openOutpostHardCap",-1);
            Scribe_Values.Look(ref lastDay,"openOutpostLastDay");
            Scribe_Values.Look(ref longitude,"openOutpostLongitude");
            Scribe_References.Look(ref legion,"openOutpostLegion");
        }
        public override void GameComponentTick(){
            if(hardCap<0||Day==lastDay)return;
            lastDay=Day;
            Replenish();
        }
        public int Replenish(){
            if(hardCap<0)return 0;
            int missing=Math.Max(0,hardCap-SettlementCount);
            if(missing==0)return 0;
            if(legion==null){
                var def=DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion");
                legion=Find.FactionManager.FirstFactionOfDef(def);
                if(legion==null){legion=FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(def));Find.FactionManager.Add(legion);}
                Current.Game.GetComponent<LegionFactionRegistry>().Register(legion);
            }
            int added=0;
            while(SettlementCount<hardCap){
                var tile=TileFinder.RandomSettlementTileFor(Find.WorldGrid.Surface,legion);
                // Native TileFinder returns tile zero on failure; never trust that fallback.
                if(!TileFinder.IsValidTileForNewSettlement(tile))break;
                var settlement=(Settlement)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("PMP_RomulusOutpost"));
                settlement.Tile=tile;settlement.SetFaction(legion);
                settlement.Name="PMP.Open.OutpostName".Translate(settlement.ID);
                Find.WorldObjects.Add(settlement);added++;
                if(added>=missing)break;
            }
            if(added>0)Messages.Message("PMP.Open.OutpostsReplenished".Translate(added,SettlementCount,hardCap),MessageTypeDefOf.NeutralEvent,false);
            if(SettlementCount<hardCap)Log.Warning("[PMP open campaign] No valid settlement tile; remaining vacancies will be retried next day.");
            return added;
        }
    }
}
