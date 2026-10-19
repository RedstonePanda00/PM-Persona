using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    public interface ILegionBombardmentHandler
    {
        void ExecuteBombard(Building_CarrierOrbiter orbiter, byte variantIndex, int clusterIndex, IntVec3 targetCell,
            int now, out int travelTicks);
    }

    public abstract class LegionBombardmentHandlerBase : ILegionBombardmentHandler
    {
        protected abstract float LineLengthCells { get; }
        protected abstract int StrikeCount { get; }
        protected abstract float ExplosionRadius { get; }
        protected abstract int ExplosionDamage { get; }
        protected abstract int DirectHitDamage { get; }
        protected abstract float LateralOffsetMax { get; }

        public void ExecuteBombard(Building_CarrierOrbiter orbiter, byte variantIndex, int clusterIndex,
            IntVec3 targetCell, int now, out int travelTicks)
        {
            travelTicks = 0;
            if (orbiter == null || orbiter.Map == null)
                return;

            orbiter.DispatchOneDroneToTarget(clusterIndex, targetCell, out travelTicks, out Vector2 startXZ,
                (StrikeCount - 1) * LegionBombardmentUtility.StrikeStaggerTicks);
            if (travelTicks <= 0)
                return;

            Map map = orbiter.Map;
            List<IntVec3> cells = StrikeCount <= 1
                ? new List<IntVec3> { targetCell }
                : LegionBombardmentUtility.BuildCenteredLineCells(targetCell, startXZ, LineLengthCells, StrikeCount,
                    LateralOffsetMax, map);

            for (int i = 0; i < cells.Count; i++)
            {
                int triggerTick = now + travelTicks + i * LegionBombardmentUtility.StrikeStaggerTicks;
                orbiter.EnqueueLegionStrike(triggerTick, cells[i], ExplosionRadius, ExplosionDamage, DirectHitDamage);
            }
        }
    }

    public sealed class LegionBombardmentHandler_AA : LegionBombardmentHandlerBase
    {
        protected override float LineLengthCells => 10f;
        protected override int StrikeCount => 20;
        protected override float ExplosionRadius => 1.5f;
        protected override int ExplosionDamage => 35;
        protected override int DirectHitDamage => 0;
        protected override float LateralOffsetMax => 0.5f;
    }

    public sealed class LegionBombardmentHandler_AG : LegionBombardmentHandlerBase
    {
        protected override float LineLengthCells => 6f;
        protected override int StrikeCount => 7;
        protected override float ExplosionRadius => 5.5f;
        protected override int ExplosionDamage => 200;
        protected override int DirectHitDamage => 300;
        protected override float LateralOffsetMax => 3f;
    }

    public sealed class LegionBombardmentHandler_LB : LegionBombardmentHandlerBase
    {
        protected override float LineLengthCells => 0f;
        protected override int StrikeCount => 1;
        protected override float ExplosionRadius => 14.9f;
        protected override int ExplosionDamage => 550;
        protected override int DirectHitDamage => 1000;
        protected override float LateralOffsetMax => 0f;
    }

    public sealed class LegionBombardmentHandler_Default : ILegionBombardmentHandler
    {
        private static readonly LegionBombardmentHandler_LB FallbackLb = new LegionBombardmentHandler_LB();

        public void ExecuteBombard(Building_CarrierOrbiter orbiter, byte variantIndex, int clusterIndex,
            IntVec3 targetCell, int now, out int travelTicks)
        {
            FallbackLb.ExecuteBombard(orbiter, variantIndex, clusterIndex, targetCell, now, out travelTicks);
        }
    }

    public static class LegionBombardmentRegistry
    {
        private static readonly LegionBombardmentHandler_Default DefaultHandler = new LegionBombardmentHandler_Default();
        private static readonly Dictionary<string, ILegionBombardmentHandler> HandlersByClassName =
            new Dictionary<string, ILegionBombardmentHandler>();

        static LegionBombardmentRegistry()
        {
            RegisterHandler(typeof(LegionBombardmentHandler_Default).FullName, DefaultHandler);
            RegisterHandler(typeof(LegionBombardmentHandler_AA).FullName, new LegionBombardmentHandler_AA());
            RegisterHandler(typeof(LegionBombardmentHandler_AG).FullName, new LegionBombardmentHandler_AG());
            RegisterHandler(typeof(LegionBombardmentHandler_LB).FullName, new LegionBombardmentHandler_LB());
        }

        public static void RegisterHandler(string className, ILegionBombardmentHandler handler)
        {
            if (string.IsNullOrEmpty(className) || handler == null)
                return;
            HandlersByClassName[className] = handler;
        }

        public static ILegionBombardmentHandler GetHandler(Building_CarrierOrbiter orbiter, byte variantIndex)
        {
            CarrierLegionVariantConfig cfg = orbiter?.GetVariantConfig(variantIndex);
            if (cfg == null || string.IsNullOrEmpty(cfg.bombardmentHandlerClass))
                return DefaultHandler;

            string className = cfg.bombardmentHandlerClass;
            if (HandlersByClassName.TryGetValue(className, out ILegionBombardmentHandler cached))
                return cached;

            Type type = GenTypes.GetTypeInAnyAssembly(className);
            if (type == null)
            {
                Log.Warning("[PMP CarrierAircraftLegion] Unknown bombardment handler: " + className + ", using default.");
                return DefaultHandler;
            }

            if (Activator.CreateInstance(type) is ILegionBombardmentHandler instance)
            {
                HandlersByClassName[className] = instance;
                return instance;
            }

            Log.Warning("[PMP CarrierAircraftLegion] Type does not implement ILegionBombardmentHandler: " + className);
            return DefaultHandler;
        }
    }
}
