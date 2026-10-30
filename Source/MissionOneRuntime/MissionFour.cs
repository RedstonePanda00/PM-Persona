using System.Linq;
using HarmonyLib;
using PMP_Persona;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using PMP_Persona.Romulus;

namespace PMP.Campaign {
    // Reconstructed on every spawn/load; the shared component retains its normal
    // health behavior. It does not intercept death or grant mission-three immunity.
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.SpawnSetup))]
    static class MissionFourMasonComponent {
        static void Postfix(Pawn __instance) {
            var c=Current.Game?.GetComponent<CampaignController>();
            if(!MissionScope.Matches(c,"mission-four")||!c.Actors.TryGetValue("hero",out var hero)||hero!=__instance||hero.TryGetComp<CompNeverDowned>()!=null)return;
            var comp=new CompNeverDowned{parent=hero};comp.Initialize(new CompProperties_NeverDowned());hero.AllComps.Add(comp);comp.PostSpawnSetup(false);
        }
    }
    [CampaignModule("pmp.m4.setup","Action","Initialize Persona mission-four equipment and remove preceding chapter protection.")]
    public sealed class MissionFourSetup:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta) {
            var c=(CampaignController)host;
            if(!MissionScope.Matches(c,"mission-four"))return true;
            var hero=c.Actors["hero"];
            if(hero.Spawned&&!c.State.Variables.ContainsKey("m4-hero-ready")){
                foreach(var h in hero.health.hediffSet.hediffs.Where(h=>h is Hediff_MissionLocal).ToList())hero.health.RemoveHediff(h);
                if(hero.drafter!=null){hero.drafter.Drafted=true;hero.drafter.FireAtWill=true;}
                c.State.Variables["m4-hero-ready"]=1;
            }
            foreach(var pawn in c.Objects.Values.OfType<Pawn>().Distinct().Where(p=>p.Spawned&&!p.Dead)) {
                if(c.State.Variables.ContainsKey("m4-equipped:"+pawn.ThingID))continue;
                string weapon=pawn.kindDef.defName=="PMP_OutpostGuard"?"PMP_BelieverSpearRifle":pawn.kindDef.defName=="PMP_LegionCenturion"?"PMP_CenturionMinigun":null;
                if(weapon!=null&&pawn.equipment?.Primary==null)pawn.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(weapon)));
                c.State.Variables["m4-equipped:"+pawn.ThingID]=1;
            }
            return true;
        }
    }
    // This reusable building has no HP; campaign interactions own the installation
    // sequence. Suppression is saved on the building, independent of campaign IDs.
    public sealed class RomulusSubnode:Building {
        public bool Suppressed;
        Graphic suppressedGraphic;
        public override Graphic Graphic=>Suppressed?(suppressedGraphic??=base.Graphic.GetColoredVersion(base.Graphic.Shader,new Color(.32f,.32f,.32f),new Color(.32f,.32f,.32f))):base.Graphic;
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref Suppressed,"anchorSuppressed");}
        public override string GetInspectString()=>string.Join("\n",new[]{base.GetInspectString(),RcText.T(Suppressed?"现实稳定锚：节点已压制":"现实稳定锚：尚未安装")}.Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>s.Trim()));
    }
    [CampaignModule("pmp.suppressSubnode","Action","Mark Target Romulus subnode as suppressed and extinguish its energy appearance.")]
    public sealed class SuppressSubnode:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){
            var c=(CampaignController)host;var node=(RomulusSubnode)c.Objects[a.Target];
            if(node.Suppressed)return true;
            node.Suppressed=true;
            // Reuse the apocriton's native visual and sound without its damage/explosion rules.
            var effect=DefDatabase<EffecterDef>.GetNamed("BlastMechBandShockwave").Spawn();
            effect.Trigger(new TargetInfo(node),new TargetInfo(node));effect.Cleanup();
            DefDatabase<SoundDef>.GetNamed("Explosion_MechBandShockwave").PlayOneShot(new TargetInfo(node));
            if(Find.CurrentMap==node.Map)Find.CameraDriver.shaker.DoShake(1f);
            foreach(var pawn in node.Map.mapPawns.AllPawnsSpawned.Where(p=>!p.Dead&&LegionDoctrine.IsLegionFaction(p.Faction)))
                CompNeverDowned.ApplyScriptedStun(pawn,300,node);
            return true;
        }
    }
}
