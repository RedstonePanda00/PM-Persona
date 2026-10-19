using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace PMP_Persona.ShipSupport
{
    public sealed class ShipSupportContext
    {
        public readonly ShipSupportDef Def;
        public readonly Map Map;
        public readonly Window Window;

        public ShipSupportContext(ShipSupportDef def, Map map, Window window)
        {
            Def = def;
            Map = map;
            Window = window;
        }
    }

    public sealed class ShipSupportAction
    {
        public string label;
        public string disabledReason;
        public Action action;

        public bool Enabled => action != null && disabledReason.NullOrEmpty();
    }

    public abstract class ShipSupportWorker
    {
        public ShipSupportDef def;

        protected virtual bool BlocksActivationWhileActive => true;

        public virtual bool TryGetActiveTarget(out GlobalTargetInfo target)
        {
            Thing thing = ShipSupportRuntime.FindActiveThing(def?.activeThingDef);
            if (thing != null)
            {
                target = new GlobalTargetInfo(thing);
                return true;
            }
            target = GlobalTargetInfo.Invalid;
            return false;
        }

        public bool IsActive()
        {
            return TryGetActiveTarget(out GlobalTargetInfo _);
        }

        public virtual string GetStatusText(ShipSupportContext context)
        {
            bool active = IsActive();
            if (active)
                return "PMP.Support.Status.Active".Translate();
            if (!ShipSupportRuntime.HasFleetBeacon())
                return "PMP.Support.Status.RequiresBeacon".Translate();
            if (!ShipSupportPaymentService.CanAfford(def, context.Map))
                return "PMP.Support.Status.NotEnoughResources".Translate();
            return "PMP.Support.Status.Ready".Translate();
        }

        public virtual ShipSupportAction GetPrimaryAction(ShipSupportContext context)
        {
            if (BlocksActivationWhileActive && TryGetActiveTarget(out GlobalTargetInfo activeTarget))
            {
                return new ShipSupportAction
                {
                    label = "PMP.Support.View".Translate(),
                    action = delegate
                    {
                        CameraJumper.TryJumpAndSelect(activeTarget);
                        context.Window?.Close();
                    }
                };
            }

            string disabledReason = GetActivationDisabledReason(context);
            return new ShipSupportAction
            {
                label = IsActive()
                    ? (string)"PMP.Support.Redeploy".Translate()
                    : (string)"PMP.Support.Activate".Translate(),
                disabledReason = disabledReason,
                action = disabledReason.NullOrEmpty() ? (Action)(() => BeginActivation(context)) : null
            };
        }

        public virtual void GetSecondaryActions(ShipSupportContext context, List<ShipSupportAction> output)
        {
        }

        protected virtual string GetActivationDisabledReason(ShipSupportContext context)
        {
            if (context?.Map == null)
                return "PMP.Support.Error.NoMap".Translate();
            if (!ShipSupportRuntime.HasFleetBeacon())
                return "PMP.Support.Status.RequiresBeacon".Translate();
            if (BlocksActivationWhileActive && IsActive())
                return "PMP.Support.Status.Active".Translate();
            if (!ShipSupportPaymentService.CanAfford(def, context.Map))
                return "PMP.Support.Status.NotEnoughResources".Translate();
            return null;
        }

        protected abstract void BeginActivation(ShipSupportContext context);

        protected bool TryCommit(Map map, Func<bool> executeEffect)
        {
            string disabled = GetActivationDisabledReason(new ShipSupportContext(def, map, null));
            if (!disabled.NullOrEmpty())
            {
                Messages.Message(disabled, MessageTypeDefOf.RejectInput, false);
                return false;
            }

            if (!ShipSupportPaymentService.TryBeginPayment(def, map, out ShipSupportPaymentReceipt receipt))
            {
                Messages.Message("PMP.Support.Status.NotEnoughResources".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }

            try
            {
                if (!executeEffect())
                {
                    receipt.Rollback();
                    Messages.Message("PMP.Support.Error.ActivationFailed".Translate(),
                        MessageTypeDefOf.RejectInput, false);
                    return false;
                }

                receipt.Commit();
                ShipSupportRuntime.Invalidate();
                return true;
            }
            catch (Exception exception)
            {
                receipt.Rollback();
                Log.Error("[PMP ShipSupport] Failed to activate " + def?.defName + ": " + exception);
                Messages.Message("PMP.Support.Error.ActivationFailed".Translate(),
                    MessageTypeDefOf.RejectInput, false);
                return false;
            }
        }

        protected static TargetingParameters LocationTargetingParameters(Map map)
        {
            return new TargetingParameters
            {
                canTargetLocations = true,
                canTargetSelf = false,
                canTargetPawns = false,
                canTargetBuildings = false,
                validator = target => map != null && target.Cell.InBounds(map)
            };
        }
    }
}
