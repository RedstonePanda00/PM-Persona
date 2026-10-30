using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.Romulus
{
    public sealed class ChampionIdentityExtension : DefModExtension { }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class ChampionIdentity
    {
        private static void Postfix(Pawn __instance)
        {
            if (__instance.Dead || __instance.kindDef?.HasModExtension<ChampionIdentityExtension>() != true) return;
            __instance.gender = Gender.Male;
            // Also upgrade saved champions' former placeholder marine gear on map load.
            Replace(__instance, "Apparel_PowerArmor", "PMP_ChampionArmor");
            Replace(__instance, "Apparel_PowerArmorHelmet", "PMP_ChampionHelmet");
            __instance.Drawer.renderer.SetAllGraphicsDirty();
        }

        private static void Replace(Pawn pawn, string oldDef, string newDef)
        {
            if (pawn.apparel == null || pawn.apparel.WornApparel.Any(a => a.def.defName == newDef)) return;
            var old = pawn.apparel.WornApparel.FirstOrDefault(a => a.def.defName == oldDef);
            // New PawnKinds generate their own gear. Only migrate an actual old item here.
            if (old == null) return;
            var next = (Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(newDef));
            if (old.TryGetQuality(out var quality)) next.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
            next.HitPoints = System.Math.Max(1, (int)(next.MaxHitPoints * (old.HitPoints / (float)old.MaxHitPoints)));
            pawn.apparel.Remove(old);
            old.Destroy(DestroyMode.Vanish);
            pawn.apparel.Wear(next, dropReplacedApparel: false);
        }
    }
}
