using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.Romulus
{
    public sealed class LegionSalvageExtension : DefModExtension
    {
        public int armorFragments;
    }

    // Counts are authored on PawnKind/race definitions. Equipment never opts a wearer in.
    public static class LegionSalvage
    {
        public static bool Applies(Pawn pawn) => pawn?.Faction != null && !pawn.Faction.IsPlayer &&
            LegionDoctrine.IsLegionFaction(pawn.Faction);
        public static int Count(Pawn pawn) => System.Math.Max(0,
            (pawn.kindDef?.GetModExtension<LegionSalvageExtension>() ??
             pawn.def.GetModExtension<LegionSalvageExtension>())?.armorFragments ?? 0);
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    internal static class LegionDeathSalvage
    {
        private sealed class DeathState
        {
            public Map Map;
            public IntVec3 Position;
            public int Count;
            public List<Thing> Gear;
        }

        // Snapshot only: a boss or another system may cancel Kill. No gear is removed here.
        private static void Prefix(Pawn __instance, out DeathState __state)
        {
            __state = null;
            if (__instance.Dead || __instance.health.isBeingKilled || !LegionSalvage.Applies(__instance)) return;
            var gear = new List<Thing>();
            if (__instance.equipment != null) gear.AddRange(__instance.equipment.AllEquipmentListForReading);
            if (__instance.apparel != null) gear.AddRange(__instance.apparel.WornApparel);
            if (__instance.inventory != null) gear.AddRange(__instance.inventory.innerContainer.Where(t => !(t is Pawn)));
            __state = new DeathState { Map = __instance.MapHeld, Position = __instance.PositionHeld,
                Count = LegionSalvage.Count(__instance), Gear = gear.Distinct().ToList() };
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Pawn __instance, DeathState __state)
        {
            if (__state == null || !__instance.Dead) return;
            // Native death notifications and drops have run. Delete exactly this pawn's items,
            // including weapons already dropped by Kill, never other loot in the same cell.
            foreach (var item in __state.Gear)
                if (!item.Destroyed) item.Destroy(DestroyMode.Vanish);
            var corpse = __instance.Corpse;
            var owner = corpse?.holdingOwner;
            if (corpse != null && !corpse.Destroyed) corpse.Destroy(DestroyMode.Vanish);
            if (__state.Count <= 0) return;
            var fragment = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_ArchotechArmorFragment"));
            fragment.stackCount = __state.Count;
            if (__state.Map != null && __state.Position.InBounds(__state.Map))
            {
                if (GenPlace.TryPlaceThing(fragment, __state.Position, __state.Map, ThingPlaceMode.Near)) return;
            }
            else if (owner != null && owner.TryAdd(fragment)) return;
            fragment.Destroy(DestroyMode.Vanish);
        }
    }
}
