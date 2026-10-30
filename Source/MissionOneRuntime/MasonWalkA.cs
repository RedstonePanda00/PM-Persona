using HarmonyLib;
using ProtossMech;
using Verse;

namespace PMP.Campaign {
    // The existing PMC component supplies the entire firing/movement behavior.
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.SpawnSetup))]
    static class MasonWalkA {
        static void Postfix(Pawn __instance){
            if(__instance.kindDef?.defName!="PMP_CampaignMason"||__instance.TryGetComp<CompWalkAOnCooldown>()!=null)return;
            var comp=new CompWalkAOnCooldown{parent=__instance};
            comp.Initialize(new CompProperties_WalkAOnCooldown());__instance.AllComps.Add(comp);
            comp.PostSpawnSetup(false);
        }
    }
}
