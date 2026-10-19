using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using PMP_Persona.ShipSupport;

namespace PMP_Persona.FlattenSupport
{
    public sealed class FlattenConfig : DefModExtension
    {
        public int side = 16;
        public int animationTicks = 120;
    }

    public sealed class FlattenShipSupportWorker : ShipSupportWorker
    {
        internal static Map TargetMap;
        private static int previewSide = 16;
        internal static CellRect Area(IntVec3 center, Map map, int side = 16) =>
            new CellRect(center.x - side / 2, center.z - side / 2, side, side).ClipInsideMap(map);
        internal static List<Pawn> Targets(Map map, CellRect area) => map.mapPawns.AllPawnsSpawned
            .Where(p => !p.Dead && !p.Destroyed && area.Contains(p.Position)).ToList();

        internal static void DrawPreview()
        {
            if (TargetMap == null || TargetMap != Find.CurrentMap || !UI.MouseCell().InBounds(TargetMap)) return;
            GenDraw.DrawFieldEdges(Area(UI.MouseCell(), TargetMap, previewSide).Cells.ToList());
        }

        public override bool TryGetActiveTarget(out GlobalTargetInfo target)
        {
            var sheetDef = DefDatabase<ThingDef>.GetNamedSilentFail("PMP_FlattenedPawn");
            if (sheetDef != null) foreach (var map in Find.Maps)
                foreach (var thing in map.listerThings.ThingsOfDef(sheetDef))
                    if (thing is Thing_FlattenedPawn sheet && !sheet.Ready)
                    { target = new GlobalTargetInfo(sheet); return true; }
            target = GlobalTargetInfo.Invalid; return false;
        }

        protected override void BeginActivation(ShipSupportContext context)
        {
            var map = context.Map;
            var config = def.GetModExtension<FlattenConfig>() ?? new FlattenConfig();
            TargetMap = map; previewSide = Math.Max(1, config.side);
            Find.Targeter.BeginTargeting(LocationTargetingParameters(map), target =>
            {
                TargetMap = null;
                if (Targets(map, Area(target.Cell, map, previewSide)).Count == 0)
                { Messages.Message("PMP.Flatten.NoPawns".Translate(), MessageTypeDefOf.RejectInput, false); return; }
                TryCommit(map, () => Activate(map, target.Cell, config));
            }, null, () => TargetMap = null);
            context.Window?.Close();
        }

        internal static bool Activate(Map map, IntVec3 center, FlattenConfig config)
        {
            var pawns = Targets(map, Area(center, map, Math.Max(1, config.side)));
            if (pawns.Count == 0) return false;
            var made = new List<Thing_FlattenedPawn>();
            try
            {
                var def = DefDatabase<ThingDef>.GetNamed("PMP_FlattenedPawn");
                foreach (var pawn in pawns)
                {
                    var sheet = (Thing_FlattenedPawn)ThingMaker.MakeThing(def);
                    made.Add(sheet);
                    sheet.Capture(pawn, Math.Max(0, config.animationTicks));
                }
                foreach (var sheet in made) sheet.CommitCapture();
                return true;
            }
            catch
            {
                // A failed activation must not leave paid-for half captures behind.
                foreach (var sheet in made) sheet.RollbackCapture();
                throw;
            }
        }
    }
}
