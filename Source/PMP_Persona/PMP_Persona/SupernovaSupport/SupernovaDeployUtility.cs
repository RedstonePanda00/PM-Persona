using PMP_Persona.ShipSupport;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public class SupernovaShipSupportWorker : ShipSupportWorker
    {
        public override bool TryGetActiveTarget(out GlobalTargetInfo target)
        {
            if (base.TryGetActiveTarget(out target))
                return true;

            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    Map map = Find.Maps[i];
                    MapComponent_SupernovaWarpDeploy deploy =
                        map?.GetComponent<MapComponent_SupernovaWarpDeploy>();
                    if (deploy != null && deploy.TryGetPendingTarget(out IntVec3 cell))
                    {
                        target = new GlobalTargetInfo(cell, map);
                        return true;
                    }
                }
            }

            target = GlobalTargetInfo.Invalid;
            return false;
        }

        protected override void BeginActivation(ShipSupportContext context)
        {
            Map map = context.Map;
            SupernovaTargeting.Begin(map);
            Find.Targeter.BeginTargeting(
                LocationTargetingParameters(map),
                delegate(LocalTargetInfo target)
                {
                    SupernovaTargeting.Clear();
                    IntVec3 cell = target.Cell;
                    if (!cell.InBounds(map))
                        return;
                    TryCommit(map, () => SupernovaDeployer.TryDeployAt(map, cell));
                },
                null,
                SupernovaTargeting.Clear);
            context.Window?.Close();
        }
    }
}
