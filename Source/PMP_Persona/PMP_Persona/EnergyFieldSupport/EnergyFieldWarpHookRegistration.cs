using ProtossMech;
using RimWorld;
using Verse;

namespace PMP_Persona.EnergyFieldSupport
{
    [StaticConstructorOnStartup]
    public static class EnergyFieldWarpHookRegistration
    {
        static EnergyFieldWarpHookRegistration()
        {
            WarpEnergyFieldHooks.CellInAdditionalEnergyField = (map, cell, faction) =>
            {
                GameComponent_EnergyFieldManager mgr = Current.Game?.GetComponent<GameComponent_EnergyFieldManager>();
                return mgr != null && mgr.CellCoveredByFactionField(map, cell, faction);
            };
        }
    }
}
