using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;
using PMP_Persona.ShipSupport;

namespace PMP_Persona.SingularitySupport
{
    public sealed class SingularityConfig : DefModExtension
    {
        public float radius = 8;
        public int ruptureTicks = 60, absorptionTicks = 300, closureTicks = 60;
        public float shakeStrength = .18f;
    }

    public sealed class SingularityShipSupportWorker : ShipSupportWorker
    {
        internal static Map TargetMap;
        private static float targetRadius;
        internal static void DrawPreview()
        {
            var map = TargetMap;
            if (map == null || map != Find.CurrentMap) return;
            var cell = UI.MouseCell();
            if (!cell.InBounds(map)) return;
            GenDraw.DrawRadiusRing(cell, targetRadius);
            foreach (var thing in SingularitySession.Collect(map, cell, targetRadius))
                if (thing.def.category == ThingCategory.Building && (thing.def.size.x > 1 || thing.def.size.z > 1))
                    GenDraw.DrawFieldEdges(new List<IntVec3>(thing.OccupiedRect().Cells));
        }
        public override bool TryGetActiveTarget(out GlobalTargetInfo target)
        {
            foreach (var map in Find.Maps)
            {
                var effect = map.GetComponent<SingularitySession>();
                if (effect != null && !effect.Completed) { target = new GlobalTargetInfo(effect.Center, map); return true; }
            }
            target = GlobalTargetInfo.Invalid; return false;
        }
        protected override void BeginActivation(ShipSupportContext context)
        {
            var map = context.Map;
            var config = def.GetModExtension<SingularityConfig>() ?? new SingularityConfig();
            TargetMap = map; targetRadius = config.radius;
            Find.Targeter.BeginTargeting(LocationTargetingParameters(map), target =>
            {
                TargetMap = null;
                TryCommit(map, () => SingularitySession.Start(map, target.Cell, config, def));
            }, null, () => TargetMap = null);
            context.Window?.Close();
        }
    }
}
