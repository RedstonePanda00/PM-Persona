using System;
using System.Collections.Generic;
using PMP_Persona.ShipSupport;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PMP_Persona.EnergyFieldSupport
{
    public class EnergyFieldShipSupportWorker : ShipSupportWorker
    {
        protected override bool BlocksActivationWhileActive => false;

        public override bool TryGetActiveTarget(out GlobalTargetInfo target)
        {
            GameComponent_EnergyFieldManager manager =
                Current.Game?.GetComponent<GameComponent_EnergyFieldManager>();
            if (manager != null
                && manager.TryGetEnergyField(Faction.OfPlayer, out Map map, out IntVec3 center))
            {
                target = new GlobalTargetInfo(center, map);
                return true;
            }
            target = GlobalTargetInfo.Invalid;
            return false;
        }

        public override void GetSecondaryActions(
            ShipSupportContext context,
            List<ShipSupportAction> output)
        {
            if (!IsActive())
                return;
            output.Add(new ShipSupportAction
            {
                label = "PMP.Support.EndEarly".Translate(),
                action = delegate
                {
                    GameComponent_EnergyFieldManager manager =
                        Current.Game?.GetComponent<GameComponent_EnergyFieldManager>();
                    if (manager != null && manager.RemoveEnergyField(Faction.OfPlayer))
                    {
                        ShipSupportRuntime.Invalidate();
                        Messages.Message("PMP.Support.EnergyFieldEnded".Translate(),
                            MessageTypeDefOf.NeutralEvent, false);
                    }
                }
            });
        }

        protected override void BeginActivation(ShipSupportContext context)
        {
            Map map = context.Map;
            EnergyFieldTargeting.Begin(map);
            Find.Targeter.BeginTargeting(
                LocationTargetingParameters(map),
                delegate(LocalTargetInfo target)
                {
                    EnergyFieldTargeting.Clear();
                    IntVec3 cell = target.Cell;
                    if (!cell.InBounds(map))
                        return;
                    TryCommit(map, () => EnergyFieldDeployer.TryDeployAt(map, cell, Faction.OfPlayer));
                },
                null,
                EnergyFieldTargeting.Clear);
            context.Window?.Close();
        }
    }
}
