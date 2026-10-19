using System;
using System.Collections.Generic;
using System.Linq;
using ProtossMech;
using RimWorld;
using Verse;

namespace PMP_Persona.Prometheus
{
    public sealed class Thing_Fireseed : ThingWithComps, ISuspendableThingHolder
    {
        private ThingOwner<Thing> contents;
        private Dictionary<WorkTypeDef, int> priorities = new Dictionary<WorkTypeDef, int>();
        public bool IsContentsSuspended => true;
        public Pawn_Prometheus HeldPawn => contents.FirstOrDefault() is Corpse corpse
            ? corpse.InnerPawn as Pawn_Prometheus : contents.FirstOrDefault() as Pawn_Prometheus;
        public Thing_Fireseed() { contents = new ThingOwner<Thing>(this, false, LookMode.Deep); }
        public override string LabelNoCount => HeldPawn == null ? base.LabelNoCount : (string)"PMP.Fireseed.Name".Translate(HeldPawn.LabelShortCap);
        public override string DescriptionFlavor => HeldPawn == null ? base.DescriptionFlavor : (string)"PMP.Fireseed.Description".Translate(HeldPawn.LabelShortCap);
        public override string DescriptionDetailed => DescriptionFlavor;
        public override string GetInspectString() => DescriptionFlavor;
        public override bool CanStackWith(Thing other) => false;
        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed) { absorbed = true; }

        internal static Thing_Fireseed ReplaceRemains(Corpse corpse, Dictionary<WorkTypeDef, int> work)
        {
            Map map = corpse.Map;
            IntVec3 cell = corpse.Position;
            ThingOwner owner = corpse.holdingOwner;
            var seed = (Thing_Fireseed)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_Fireseed"));
            seed.priorities = work;
            if (corpse.Spawned) corpse.DeSpawn();
            else owner?.Remove(corpse);
            if (!seed.contents.TryAdd(corpse, false)) throw new InvalidOperationException("Fireseed rejected remains");
            if (map != null)
            {
                if (GenPlace.TryPlaceThing(seed, cell, map, ThingPlaceMode.Near)) return seed;
            }
            else if (owner != null && owner.TryAdd(seed, false)) return seed;
            // Do not lose an individual if a carrier/container refuses the replacement.
            if (owner != null)
            {
                seed.contents.Remove(corpse);
                owner.TryAdd(corpse, false);
                seed.Destroy();
            }
            else Log.Warning("[PMP] Fireseed has no spawned holder: " + seed);
            return seed;
        }

        internal bool TryRestore(Map map, IntVec3 near)
        {
            Pawn_Prometheus pawn = HeldPawn;
            if (pawn == null || pawn.Discarded || map == null) return false;
            IntVec3 cell = GenRadial.RadialCellsAround(near, 6, true).FirstOrDefault(c =>
                c.InBounds(map) && c.Standable(map) && c.GetFirstPawn(map) == null && c.GetDoor(map) == null, IntVec3.Invalid);
            if (!cell.IsValid) return false;
            try
            {
                if (pawn.Dead && !ResurrectionUtility.TryResurrect(pawn,
                    new ResurrectionParams { dontSpawn = true, restoreMissingParts = true })) return false;
                // Keep ownership until placement, including after a recoverable failed placement.
                if (pawn.holdingOwner != null) pawn.holdingOwner.Remove(pawn);
                GenSpawn.Spawn(pawn, cell, map);
                pawn.EnsureApparel();
                if (pawn.workSettings != null)
                    foreach (var setting in priorities)
                        if (setting.Key != null && !pawn.WorkTypeIsDisabled(setting.Key))
                            pawn.workSettings.SetPriority(setting.Key, setting.Value);
                foreach (Hediff injury in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList())
                    pawn.health.RemoveHediff(injury);
                pawn.GetComp<ProtossMechShield>()?.RestoreShield();
                return true;
            }
            catch (Exception e)
            {
                if (!pawn.Spawned && !pawn.Discarded && pawn.holdingOwner == null) contents.TryAdd(pawn, false);
                Log.Error("[PMP] Could not restore fireseed: " + e);
                return false;
            }
        }

        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            if (contents.Count != 0) return;
            base.Destroy(mode);
        }
        public ThingOwner GetDirectlyHeldThings() => contents;
        public void GetChildHolders(List<IThingHolder> output) => ThingOwnerUtility.AppendThingHoldersFromThings(output, contents);
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref contents, "fireseedContents", this);
            Scribe_Collections.Look(ref priorities, "fireseedWorkPriorities", LookMode.Def, LookMode.Value);
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos()) yield return gizmo;
            if (HeldPawn != null) yield return new Command_Action
            {
                defaultLabel = "PMP.Fireseed.Inspect".Translate(), icon = TexCommand.SelectCarriedPawn,
                action = () => { if (HeldPawn != null) Find.WindowStack.Add(new Dialog_InfoCard(HeldPawn)); }
            };
        }
    }

    public sealed class Recipe_RestorePrometheus : RecipeWorker
    {
        // DoBill consumes first, then notifies completion. Defer ALL consumption until revival succeeds.
        public override void ConsumeIngredient(Thing ingredient, RecipeDef recipe, Map map) { }
        public override void Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)
        {
            Thing_Fireseed seed = ingredients.OfType<Thing_Fireseed>().SingleOrDefault();
            var fuel = ingredients.Where(t => t.def.defName == "PM_Solarite" && !t.Destroyed).ToList();
            Thing forge = billDoer?.CurJob?.GetTarget(Verse.AI.TargetIndex.A).Thing;
            if (forge?.def.defName != "ProtossForge" || seed == null || seed.Destroyed
                || fuel.Sum(t => t.stackCount) < 5 || !seed.TryRestore(billDoer.Map, forge.InteractionCell))
            {
                Messages.Message("PMP.Fireseed.RestoreFailed".Translate(), MessageTypeDefOf.RejectInput, false);
                return;
            }
            int remaining = 5;
            foreach (Thing solarite in fuel)
            {
                int count = Math.Min(remaining, solarite.stackCount);
                if (count > 0) solarite.SplitOff(count).Destroy();
                remaining -= count;
            }
            seed.Destroy();
        }
    }
}
