using HarmonyLib;
using RimWorld;
using Verse;
using System.Reflection;

namespace PMP_Persona
{
    public class CompProperties_NeverDowned : CompProperties
    {
        public CompProperties_NeverDowned()
        {
            this.compClass = typeof(CompNeverDowned);
        }
    }

    public class CompNeverDowned : ThingComp
    {
        [System.ThreadStatic] internal static bool scriptedStun;
        public static void ApplyScriptedStun(Pawn pawn,int ticks,Thing instigator) {
            bool previous=scriptedStun;scriptedStun=true;
            try{pawn.stances.stunner.StunFor(ticks,instigator);}finally{scriptedStun=previous;}
        }
    }

    
    [HarmonyPatch(typeof(Pawn_HealthTracker), "ShouldBeDowned")]
    public static class Patch_Pawn_HealthTracker_ShouldBeDowned
    {
        public static void Postfix(Pawn_HealthTracker __instance, ref bool __result)
        {
            if (!__result)
            {
                return;
            }

            Pawn pawn = __instance.hediffSet.pawn;
            
            if (pawn != null && pawn.GetComp<CompNeverDowned>() != null)
            {
                __result = false;
            }
        }
    }
    
    [HarmonyPatch(typeof(StunHandler), "StunFor")]
    public static class Patch_StunHandler_StunFor
    {
        public static bool Prefix(StunHandler __instance)
        {
            Pawn pawn = __instance.parent as Pawn;

            if (!CompNeverDowned.scriptedStun && pawn != null && pawn.GetComp<CompNeverDowned>() != null)
            {
                MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "StunResisted".Translate(), -1f);
                return false;
            }
            return true;
        }
    }
}
