using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.Romulus
{
    public sealed class LegionApparelUpgrade
    {
        public ThingDef from;
        public ThingDef to;
    }

    public sealed class LegionApparelMigrationExtension : DefModExtension
    {
        public List<LegionApparelUpgrade> upgrades = new List<LegionApparelUpgrade>();
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class LegionApparelMigration
    {
        private static void Postfix(Pawn __instance)
        {
            var pawn = __instance;
            var config = pawn.kindDef?.GetModExtension<LegionApparelMigrationExtension>();
            if (config == null || pawn.Dead || pawn.Faction?.IsPlayer == true || pawn.apparel == null) return;
            bool changed = false;
            foreach (var upgrade in config.upgrades)
            {
                if (upgrade.from == null || upgrade.to == null || pawn.apparel.WornApparel.Any(a => a.def == upgrade.to)) continue;
                var old = pawn.apparel.WornApparel.FirstOrDefault(a => a.def == upgrade.from);
                if (old == null) continue;
                var next = (Apparel)ThingMaker.MakeThing(upgrade.to);
                if (old.TryGetQuality(out var quality)) next.TryGetComp<CompQuality>()?.SetQuality(quality, ArtGenerationContext.Outsider);
                next.HitPoints = Math.Max(1, (int)(next.MaxHitPoints * (old.HitPoints / (float)old.MaxHitPoints)));
                pawn.apparel.Remove(old);
                old.Destroy(DestroyMode.Vanish);
                pawn.apparel.Wear(next, dropReplacedApparel: false);
                changed = true;
            }
            if (changed) pawn.Drawer.renderer.SetAllGraphicsDirty();
        }
    }
}
