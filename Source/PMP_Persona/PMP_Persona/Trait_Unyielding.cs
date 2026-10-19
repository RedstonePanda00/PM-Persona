using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PMP_Persona
{
    [DefOf]
    public static class Trait_UnyieldingDefOf
    {
        public static TraitDef PMPH_Unyielding;
        
        static Trait_UnyieldingDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(Trait_UnyieldingDefOf));
        }
    }
    
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class Patch_TraitPreventsMentalBreak
    {
        [HarmonyPrefix]
        public static bool Prefix(MentalStateHandler __instance, Pawn ___pawn, bool transitionSilently, ref bool __result)
        {
            Pawn pawn = ___pawn;

            if (pawn == null || pawn.story == null || pawn.story.traits == null)
            {
                return true;
            }

            if (pawn.story.traits.HasTrait(Trait_UnyieldingDefOf.PMPH_Unyielding))
            {
                if (!transitionSilently && PawnUtility.ShouldSendNotificationAbout(pawn))
                {
                    MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, "PMPH_MentalBreakResisted".Translate(), 3.0f);
                }

                __result = false;
                return false;
            }

            return true;
        }
    }
}