using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PMP_Persona {
    [DefOf] public static class PersonaHeartDefOf {
        public static HediffDef PMPH_HediffProtossHeart;
        static PersonaHeartDefOf(){DefOfHelper.EnsureInitializedInCtor(typeof(PersonaHeartDefOf));}
    }
    public static class ProtossHeart {
        public static bool Installed(Pawn pawn)=>pawn?.health?.hediffSet.HasHediff(PersonaHeartDefOf.PMPH_HediffProtossHeart)==true;
    }
    // BurningResponse is the native panic-running subtree, independent of mental breaks.
    [HarmonyPatch(typeof(ThinkNode_ConditionalBurning),"Satisfied")]
    static class HeartBurningResponse {
        static void Postfix(Pawn pawn,ref bool __result){if(__result&&ProtossHeart.Installed(pawn))__result=false;}
    }
    [HarmonyPatch(typeof(MentalStateHandler),nameof(MentalStateHandler.TryStartMentalState))]
    static class HeartFirePanic {
        static bool Prefix(Pawn ___pawn,MentalStateDef stateDef,ref bool __result){
            if(stateDef!=MentalStateDefOf.PanicFleeFire||!ProtossHeart.Installed(___pawn))return true;
            __result=false;return false;
        }
    }
}
