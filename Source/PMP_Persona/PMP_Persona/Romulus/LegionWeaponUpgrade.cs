using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.Romulus
{
    public sealed class LegionUpgradeRecipe : DefModExtension
    {
        public ThingDef prototype;
    }

    public sealed class RecipeWorker_LegionWeaponUpgrade : RecipeWorker { }

    [HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    static class LegionWeaponUpgradeQuality
    {
        static void Postfix(RecipeDef recipeDef, List<Thing> ingredients, ref IEnumerable<Thing> __result)
        {
            if (recipeDef.Worker is not RecipeWorker_LegionWeaponUpgrade) return;
            var prototype = recipeDef.GetModExtension<LegionUpgradeRecipe>()?.prototype;
            if (prototype == null) return;
            // Only the actual ingredient list may establish the floor; never scan inventory.
            QualityCategory? floor = null;
            foreach (var ingredient in ingredients)
                if (ingredient.def == prototype && ingredient.TryGetQuality(out var quality) &&
                    (!floor.HasValue || quality > floor.Value)) floor = quality;
            __result = PreserveQuality(__result, floor);
        }

        static IEnumerable<Thing> PreserveQuality(IEnumerable<Thing> products, QualityCategory? floor)
        {
            foreach (var product in products)
            {
                var quality = product.TryGetComp<CompQuality>();
                // Native crafting has already rolled the new quality when each product is yielded.
                if (floor.HasValue && quality != null && quality.Quality < floor.Value)
                    quality.SetQuality(floor.Value, ArtGenerationContext.Colony);
                yield return product;
            }
        }
    }
}
