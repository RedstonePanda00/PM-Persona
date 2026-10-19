using Verse;
namespace PMP_Persona.SupernovaSupport
{
    public static class SupernovaDeployer
    {
        public static bool TryDeployAt(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map))
                return false;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(SupernovaConstants.SupernovaDefName);
            if (def == null)
                return false;

            MapComponent_SupernovaWarpDeploy deploy = MapComponent_SupernovaWarpDeploy.Get(map);
            if (deploy == null)
                return false;

            return deploy.ScheduleDeploy(cell);
        }

        internal static void SpawnSupernovaAt(Map map, IntVec3 cell)
        {
            if (map == null || !cell.InBounds(map))
                return;

            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(SupernovaConstants.SupernovaDefName);
            if (def == null)
                return;

            Thing spawned = GenSpawn.Spawn(def, cell, map, WipeMode.Vanish);
            if (spawned == null)
                return;

            SupportSummonLetterUtility.TrySendSummonLetter(def, spawned);
        }
    }
}
