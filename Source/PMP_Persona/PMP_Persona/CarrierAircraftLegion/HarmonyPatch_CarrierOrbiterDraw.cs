using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    [HarmonyPatch(typeof(MapDrawer), nameof(MapDrawer.DrawMapMesh))]
    public static class HarmonyPatch_CarrierOrbiterDraw
    {
        private const string CarrierOrbiterDefName = "PMP_CarrierOrbiter";

        [HarmonyPostfix]
        public static void Postfix()
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return;

            ThingDef carrierOrbiterDef = DefDatabase<ThingDef>.GetNamedSilentFail(CarrierOrbiterDefName);
            if (carrierOrbiterDef == null)
                return;

            List<Thing> carrierList = map.listerThings.ThingsOfDef(carrierOrbiterDef);
            if (carrierList.Count == 0)
                return;

            Building_CarrierOrbiter.BeginCarrierDrawFrame();
            for (int i = 0; i < carrierList.Count; i++)
            {
                if (carrierList[i] is Building_CarrierOrbiter cb && cb.Spawned && !cb.Destroyed)
                    Building_CarrierOrbiter.CollectCarrierMatrices(cb);
            }

            Building_CarrierOrbiter.FlushCarrierDraw();
        }
    }
}
