using System;
using System.Collections.Generic;
using System.Xml;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.GestaltZero
{
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn),
        new[] { typeof(PawnGenerationRequest) })]
    public static class Patch_GestaltZero_GeneratePawn
    {
        public static void Prefix(ref PawnGenerationRequest request)
        {
            GestaltZeroLifecycleExtension extension =
                request.KindDef?.GetModExtension<GestaltZeroLifecycleExtension>();
            if (extension == null)
                return;

            if (extension.forcedBodyType != null)
                request.ForceBodyType = extension.forcedBodyType;
            if (!ModsConfig.BiotechActive)
                return;

            if (extension.forcedXenotype != null)
                request.ForcedXenotype = extension.forcedXenotype;

            var forcedGenes = request.ForcedEndogenes != null
                ? new List<GeneDef>(request.ForcedEndogenes)
                : new List<GeneDef>();
            RemoveCategory(forcedGenes, EndogeneCategory.Melanin);
            RemoveCategory(forcedGenes, EndogeneCategory.HairColor);
            if (extension.forcedSkinGene != null)
                forcedGenes.Add(extension.forcedSkinGene);
            if (extension.forcedHairGene != null)
                forcedGenes.Add(extension.forcedHairGene);
            request.ForcedEndogenes = forcedGenes;
        }

        public static void Postfix(Pawn __result)
        {
            GestaltZeroUtility.ApplyCanonicalAppearance(__result);
        }

        private static void RemoveCategory(List<GeneDef> genes, EndogeneCategory category)
        {
            for (int i = genes.Count - 1; i >= 0; i--)
            {
                if (genes[i]?.endogeneCategory == category)
                    genes.RemoveAt(i);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    [HarmonyPriority(Priority.First)]
    public static class Patch_GestaltZero_InterceptKill
    {
        public static bool Prefix(Pawn __instance, DamageInfo? dinfo, Hediff exactCulprit,
            out bool __state)
        {
            __state = false;
            if (GestaltZeroUtility.IsIntercepting(__instance))
                return false;

            // Skip repeated Kill calls within the same damage chain after pooling Zero.
            if (GestaltZeroUtility.IsPooledZero(__instance))
            {
                __state = true;
                return false;
            }

            __state = GestaltZeroUtility.TryInterceptDeath(__instance);
            return !__state;
        }

        public static Exception Finalizer(Pawn __instance, bool __state, Exception __exception)
        {
            if (__state)
            {
                try
                {
                    // Restore pooled health state after all death postfixes.
                    GestaltZeroUtility.RestoreForReturn(__instance);
                }
                catch (Exception exception)
                {
                    Log.Error("[PMP_Persona] Gestalt Zero post-interception repair failed: " +
                              exception);
                }
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Destroy))]
    [HarmonyPriority(Priority.First)]
    public static class Patch_GestaltZero_InterceptKillFinalizeDestroy
    {
        public static bool Prefix(Pawn __instance, DestroyMode mode, out bool __state)
        {
            __state = false;
            // Intercept KillFinalize only; allow Vanish container and map transitions.
            if (mode != DestroyMode.KillFinalize || __instance == null)
                return true;

            // Block nested KillFinalize destruction of pooled pawns.
            if (GestaltZeroUtility.IsIntercepting(__instance))
                return false;

            if (GestaltZeroUtility.IsPooledZero(__instance))
            {
                __state = true;
                return false;
            }

            if (__instance.Dead)
                return true;

            __state = GestaltZeroUtility.TryInterceptDeath(__instance);
            return !__state;
        }

        public static Exception Finalizer(Pawn __instance, bool __state, Exception __exception)
        {
            if (__state)
            {
                try
                {
                    GestaltZeroUtility.RestoreForReturn(__instance);
                }
                catch (Exception exception)
                {
                    Log.Error("[PMP_Persona] Gestalt Zero post-destroy repair failed: " +
                              exception);
                }
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(BackCompatibility), nameof(BackCompatibility.BackCompatibleDefName),
        new[] { typeof(Type), typeof(string), typeof(bool), typeof(XmlNode) })]
    public static class Patch_GestaltZero_BackCompatibleXenotype
    {
        public static void Postfix(Type defType, string defName, bool forDefInjections,
            ref string __result)
        {
            if (!forDefInjections && defType == typeof(XenotypeDef) &&
                string.Equals(defName, "PMP_GestaltZero", StringComparison.OrdinalIgnoreCase))
                __result = "Baseliner";
        }
    }
}
