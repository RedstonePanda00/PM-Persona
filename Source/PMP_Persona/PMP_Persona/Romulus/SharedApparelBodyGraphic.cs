using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.Romulus
{
    // Opt-in texture selection only; the wearer retains their actual body type.
    public sealed class SharedApparelBodyGraphic : DefModExtension
    {
        public BodyTypeDef bodyType;
    }

    [HarmonyPatch(typeof(ApparelGraphicRecordGetter), nameof(ApparelGraphicRecordGetter.TryGetGraphicApparel))]
    internal static class SharedApparelBodyGraphicPatch
    {
        private static void Prefix(Apparel apparel, ref BodyTypeDef bodyType)
        {
            var shared = apparel?.def.GetModExtension<SharedApparelBodyGraphic>();
            if (shared?.bodyType != null)
                bodyType = shared.bodyType;
        }
    }
}
