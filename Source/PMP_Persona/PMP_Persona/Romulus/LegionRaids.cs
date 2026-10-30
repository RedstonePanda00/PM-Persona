using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.Romulus
{
    public sealed class LegionRaidRules : DefModExtension
    {
        public PawnKindDef champion;
        public float championMinPoints = 40000f;
    }

    public static class LegionRaidChampion
    {
        public static void Apply(IncidentParms parms, List<Pawn> pawns, float initialPoints, bool debugTest)
        {
            var rules = parms.faction?.def.GetModExtension<LegionRaidRules>();
            if (rules?.champion == null || initialPoints < rules.championMinPoints || pawns == null ||
                pawns.Any(p => p.kindDef == rules.champion) || parms.target is not Map map) return;

            // This is one extra champion per raid, outside the normal weighted group pool.
            var champion = PawnGenerator.GeneratePawn(rules.champion, parms.faction, map.Tile);
            if (!debugTest) parms.raidArrivalMode.Worker.Arrive(new List<Pawn> { champion }, parms);
            pawns.Add(champion);
            parms.pawnCount = pawns.Count;
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_Raid), nameof(IncidentWorker_Raid.TryGenerateRaidInfo))]
    static class LegionRaidChampionRule
    {
        static void Prefix(IncidentParms parms, out float __state) => __state = parms.points;

        static void Postfix(IncidentWorker_Raid __instance, IncidentParms parms, List<Pawn> pawns,
            bool debugTest, bool __result, float __state)
        {
            if (__result && __instance is IncidentWorker_RaidEnemy)
                LegionRaidChampion.Apply(parms, pawns, __state, debugTest);
        }
    }
}
