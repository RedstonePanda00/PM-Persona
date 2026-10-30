using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP.Anomaly {
    // Native IsOutside treats a despawned entity with no room as outdoors.
    // The sealed volume is indoors; no fake Room or room-wide containment patch.
    [HarmonyPatch(typeof(RegionAndRoomQuery),nameof(RegionAndRoomQuery.IsOutside))]
    static class StasisIndoorSpace {
        static void Postfix(Thing thing,ref bool __result) {
            if(thing.ParentHolder is Building_StasisField) __result=false;
        }
    }
    [StaticConstructorOnStartup]
    static class StasisBootstrap {
        static StasisBootstrap()=>new Harmony("redstone.pmpersona.anomaly.stasis").PatchAll(typeof(StasisBootstrap).Assembly);
    }
    // A cube's existing interest/withdrawal runs on colonists, outside the sealed
    // object. Pause these effects locally while the map's cube is in stasis;
    // keep every hediff and its severity so release resumes the original state.
    static class StasisCubeInfluence {
        public static bool SealedFor(Pawn pawn) {
            var map=pawn.MapHeld;
            if(map==null||map.listerThings.ThingsOfDef(ThingDefOf.GoldenCube).Count>0)return false;
            if(map.mapPawns.AllPawnsSpawned.Any(p=>p.carryTracker?.CarriedThing?.def==ThingDefOf.GoldenCube
                ||p.inventory?.innerContainer.Any(t=>t.def==ThingDefOf.GoldenCube)==true))return false;
            return map.listerThings.ThingsOfDef(DefDatabase<ThingDef>.GetNamed("PMP_StasisField"))
                .OfType<Building_StasisField>().Any(f=>f.ContainedThing?.def==ThingDefOf.GoldenCube);
        }
    }
    [HarmonyPatch(typeof(Hediff_CubeInterest),nameof(Hediff_CubeInterest.TickInterval))]
    static class StasisCubeInterest {
        static bool Prefix(Hediff_CubeInterest __instance)=>!StasisCubeInfluence.SealedFor(__instance.pawn);
    }
    [HarmonyPatch(typeof(Hediff_CubeWithdrawal),nameof(Hediff_CubeWithdrawal.TickInterval))]
    static class StasisCubeWithdrawal {
        static bool Prefix(Hediff_CubeWithdrawal __instance)=>!StasisCubeInfluence.SealedFor(__instance.pawn);
    }
}
