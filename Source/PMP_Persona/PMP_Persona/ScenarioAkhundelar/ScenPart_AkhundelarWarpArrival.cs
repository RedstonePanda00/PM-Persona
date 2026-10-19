using System.Collections.Generic;
using RimWorld;
using Verse;

namespace PMP_Persona.ScenarioAkhundelar
{
    public class ScenPart_AkhundelarWarpArrival : ScenPart
    {
        public override void GenerateIntoMap(Map map)
        {
            base.GenerateIntoMap(map);
            if (Find.GameInitData == null)
            {
                return;
            }

            ThingDef pylonDef = DefDatabase<ThingDef>.GetNamedSilentFail("Pylon");
            if (pylonDef == null)
            {
                Log.Error("[PMP_Persona] Akhundelar arrival: Pylon def missing (ProtossMech Core).");
                return;
            }

            IntVec3 near = map.Center;
            if (!AkhundelarArrivalUtility.TryFindPylonBottomLeftNear(map, near, pylonDef, out IntVec3 pylonBl))
            {
                Log.Warning("[PMP_Persona] Akhundelar arrival: using map center for Pylon search fallback.");
                pylonBl = map.Center;
            }

            MapGenerator.PlayerStartSpot = pylonBl;

            List<Thing> startingItems = new List<Thing>();
            foreach (ScenPart part in Find.Scenario.AllParts)
            {
                startingItems.AddRange(part.PlayerStartingThings());
            }

            foreach (Thing t in startingItems)
            {
                if (t.def.CanHaveFaction)
                {
                    t.SetFactionDirect(Faction.OfPlayer);
                }
            }

            if (startingItems.Count > 0)
            {
                List<List<Thing>> groups = new List<List<Thing>> { startingItems };
                DropPodUtility.DropThingGroupsNear(
                    MapGenerator.PlayerStartSpot,
                    map,
                    groups,
                    110,
                    instaDrop: true,
                    leaveSlag: false,
                    canRoofPunch: true,
                    forbid: true,
                    allowFogged: false);
            }

            PawnKindDef kaldalisKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("PMPH_Kaldalis");
            PawnKindDef probeKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("PM_Probe");
            if (kaldalisKind == null || probeKind == null)
            {
                Log.Error("[PMP_Persona] Akhundelar arrival: missing PawnKindDef PMPH_Kaldalis or PM_Probe.");
                return;
            }

            List<Pawn> startingColonists = Find.GameInitData.startingAndOptionalPawns;
            if (startingColonists.NullOrEmpty())
            {
                Log.Error("[PMP_Persona] Akhundelar arrival: no starting colonists.");
                return;
            }

            Pawn zero = startingColonists[0];
            Pawn kaldalis = GenerateMech(kaldalisKind);
            List<Pawn> probes = new List<Pawn>();
            for (int i = 0; i < 5; i++)
            {
                probes.Add(GenerateMech(probeKind));
            }

            List<Pawn> allMechs = new List<Pawn> { kaldalis };
            allMechs.AddRange(probes);
            AkhundelarArrivalUtility.TryAddOverseerRelations(zero, allMechs);

            Thing anchor = ThingMaker.MakeThing(ThingDef.Named("PMPH_AkhundelarAnchorBeacon"));
            GenSpawn.Spawn(anchor, pylonBl, map, WipeMode.Vanish);
            anchor.TryGetComp<CompAkhundelarAnchorBeacon>()?.ConfigureScenarioData(pylonBl, zero, kaldalis, probes);
        }

        private static Pawn GenerateMech(PawnKindDef kind)
        {
            PawnGenerationRequest request = new PawnGenerationRequest(
                kind,
                Faction.OfPlayer,
                PawnGenerationContext.NonPlayer,
                null,
                forceGenerateNewPawn: false,
                allowDead: false,
                allowDowned: false,
                canGeneratePawnRelations: true,
                mustBeCapableOfViolence: false,
                1f,
                forceAddFreeWarmLayerIfNeeded: false,
                allowGay: true,
                allowPregnant: false,
                allowFood: true,
                allowAddictions: true,
                inhabitant: false,
                certainlyBeenInCryptosleep: false,
                forceRedressWorldPawnIfFormerColonist: false,
                worldPawnFactionDoesntMatter: false,
                0f,
                0f,
                null,
                1f,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                forceNoIdeo: false,
                forceNoBackstory: false,
                forbidAnyTitle: false,
                forceDead: false,
                null,
                null,
                null,
                null,
                null,
                0f,
                DevelopmentalStage.Newborn);
            return PawnGenerator.GeneratePawn(request);
        }
    }
}
