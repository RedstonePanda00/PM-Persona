using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.FlattenSupport
{
    public sealed class Thing_FlattenedPawn : ThingWithComps, ISuspendableThingHolder, IThingHolderWithDrawnPawn
    {
        private ThingOwner<Pawn> contents;
        private int startedTick, duration;
        private Rot4 facing = Rot4.South;
        private Rot4 displayFacing = Rot4.South;
        private PawnPosture posture;
        [Unsaved] private Pawn preparedPawn;
        [Unsaved] private PawnDrawParms prepared;
        [Unsaved] private bool preparedValid;
        [Unsaved] private Map originalMap;
        [Unsaved] private IntVec3 originalCell;
        [Unsaved] private Pawn captureCandidate;
        public Pawn HeldPawn => contents.Count == 0 ? null : contents[0];
        public Rot4 DisplayFacing => displayFacing;
        public bool IsContentsSuspended => true;
        public bool Ready => Progress >= 1f;
        internal float Progress => duration <= 0 ? 1 : Mathf.Clamp01((Find.TickManager.TicksGame - startedTick) / (float)duration);
        public float HeldPawnDrawPos_Y => DrawPos.y;
        public float HeldPawnBodyAngle => 0;
        public PawnPosture HeldPawnPosture => posture;
        public override string LabelNoCount => HeldPawn == null ? base.LabelNoCount : (string)"PMP.Flatten.SheetName".Translate(HeldPawn.LabelShortCap);
        public override string DescriptionFlavor => HeldPawn == null ? base.DescriptionFlavor : (string)"PMP.Flatten.Sealed".Translate(HeldPawn.LabelShortCap);
        public override string DescriptionDetailed => DescriptionFlavor;
        public Thing_FlattenedPawn() { contents = new ThingOwner<Pawn>(this, false, LookMode.Deep); }

        internal void Capture(Pawn pawn, int animationTicks)
        {
            if (pawn == null || !pawn.Spawned || pawn.Dead || HeldPawn != null) throw new InvalidOperationException("Invalid flatten target");
            originalMap = pawn.Map; originalCell = pawn.Position; captureCandidate = pawn;
            facing = pawn.Rotation; posture = pawn.GetPosture();
            startedTick = Find.TickManager.TicksGame; duration = animationTicks;
            // Create the item owner before capturing the pawn.
            GenSpawn.Spawn(this, originalCell, originalMap);
            pawn.DeSpawn();
            if (!contents.TryAdd(pawn, false)) throw new InvalidOperationException("Flatten container rejected pawn");
            captureCandidate = null;
            FlattenAnomaly.RefreshStudyCache(pawn);
        }

        internal void RollbackCapture()
        {
            Pawn pawn = HeldPawn ?? captureCandidate;
            if (pawn != null && !pawn.Spawned && originalMap != null)
            {
                contents.Remove(pawn);
                GenSpawn.Spawn(pawn, originalCell, originalMap, facing);
            }
            captureCandidate = null;
            if (HeldPawn == null && !Destroyed) Destroy();
        }
        internal void CommitCapture() { originalMap = null; captureCandidate = null; }
        internal void CycleFacing()
        {
            displayFacing = displayFacing == Rot4.South ? Rot4.East : displayFacing == Rot4.East ? Rot4.North : Rot4.South;
            preparedValid = false;
            HeldPawn?.Drawer?.renderer?.renderTree?.SetDirty();
        }

        public bool TryRelease()
        {
            var pawn = HeldPawn;
            if (!Ready || !Spawned || pawn == null) return false;
            IntVec3 cell = IntVec3.Invalid;
            foreach (var candidate in GenRadial.RadialCellsAround(Position, 5, true))
                if (candidate.InBounds(Map) && candidate.Standable(Map) && !candidate.Fogged(Map)
                    && candidate.GetFirstPawn(Map) == null && candidate.GetDoor(Map) == null)
                { cell = candidate; break; }
            if (!cell.IsValid)
            { Messages.Message("PMP.Flatten.NoReleaseCell".Translate(), this, MessageTypeDefOf.RejectInput, false); return false; }
            if (!contents.TryDrop(pawn, cell, Map, ThingPlaceMode.Direct, out _, playDropSound: false)) return false;
            pawn.Rotation = facing;
            preparedValid = false; preparedPawn = null;
            Destroy();
            return true;
        }

        public override void PreApplyDamage(ref DamageInfo dinfo, out bool absorbed) { absorbed = true; }
        public override bool CanStackWith(Thing other) => false;
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
        {
            // Consume only empty sheets after release or rollback.
            if (HeldPawn != null) return;
            bool old = allowDestroyNonDestroyable;
            try { allowDestroyNonDestroyable = true; base.Destroy(mode); }
            finally { allowDestroyNonDestroyable = old; }
        }

        public ThingOwner GetDirectlyHeldThings() => contents;
        public void GetChildHolders(List<IThingHolder> output) => ThingOwnerUtility.AppendThingHoldersFromThings(output, contents);
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Deep.Look(ref contents, "containedPawn", this);
            Scribe_Values.Look(ref startedTick, "flattenStarted"); Scribe_Values.Look(ref duration, "flattenDuration");
            Scribe_Values.Look(ref facing, "flattenFacing", Rot4.South); Scribe_Values.Look(ref posture, "flattenPosture");
            Scribe_Values.Look(ref displayFacing, "flattenDisplayFacing", Rot4.South);
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            FlattenAnomaly.RefreshStudyCache(HeldPawn);
        }
        public override string GetInspectString()
        {
            string text = DescriptionFlavor;
            if (!Ready) text += "\n" + "PMP.Flatten.Forming".Translate();
            string study = FlattenAnomaly.Inspect(this);
            return study.NullOrEmpty() ? text : text + "\n" + study;
        }
        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos()) yield return gizmo;
            var release = new Command_Action { defaultLabel = "PMP.Flatten.Release".Translate(),
                defaultDesc = "PMP.Flatten.ReleaseDesc".Translate(), icon = TexCommand.DropCarriedPawn, action = () => TryRelease() };
            if (!Ready) release.Disable("PMP.Flatten.Forming".Translate());
            else if (!Spawned) release.Disable("PMP.Flatten.MustPlace".Translate());
            yield return release;
            string direction = displayFacing == Rot4.South ? "South" : displayFacing == Rot4.East ? "East" : "North";
            yield return new Command_Action { defaultLabel = "PMP.Flatten.Facing".Translate(direction),
                defaultDesc = "PMP.Flatten.FacingDesc".Translate(), icon = TexButton.Reload, action = CycleFacing };
            if (HeldPawn != null) yield return new Command_Action { defaultLabel = "PMP.Flatten.InspectPawn".Translate(),
                icon = TexCommand.SelectCarriedPawn, action = () => { if(HeldPawn != null) Find.WindowStack.Add(new Dialog_InfoCard(HeldPawn)); } };
            foreach (var gizmo in FlattenAnomaly.Gizmos(this)) yield return gizmo;
        }

        internal PawnDrawParms DrawParameters(Vector3 drawLoc)
        {
            var parms = PawnDrawParms.Default;
            parms.pawn = HeldPawn; parms.facing = displayFacing; parms.posture = posture;
            parms.flags = PawnRenderFlags.Headgear | PawnRenderFlags.Clothes | PawnRenderFlags.NeverAimWeapon;
            parms.rotDrawMode = RotDrawMode.Fresh; parms.tint = Color.white;
            // Render the intact body with shader-based depth collapse.
            parms.matrix = Matrix4x4.TRS(drawLoc, Quaternion.identity, Vector3.one);
            return parms;
        }
        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            var pawn = phase == DrawPhase.ParallelPreDraw ? preparedPawn : HeldPawn;
            if (pawn == null) return;
            try
            {
                var tree = pawn.Drawer.renderer.renderTree;
                if (phase == DrawPhase.EnsureInitialized)
                {
                    FlattenVisual.Prepare(this);
                    preparedPawn = pawn; preparedValid = false;
                    prepared = DrawParameters(drawLoc);
                    tree.EnsureInitialized(prepared.flags);
                }
                else if (phase == DrawPhase.ParallelPreDraw)
                { tree.ParallelPreDraw(prepared); preparedValid = true; }
                else if (phase == DrawPhase.Draw)
                {
                    if (!preparedValid || preparedPawn != pawn)
                    { prepared = DrawParameters(drawLoc); tree.EnsureInitialized(prepared.flags); tree.ParallelPreDraw(prepared); }
                    tree.Draw(prepared); preparedValid = false;
                    FlattenVisual.Draw(this, drawLoc);
                }
            }
            catch (Exception e) { preparedValid = false; Log.ErrorOnce("[PMP Flatten] RenderTree draw: "+e,thingIDNumber ^ 913472); }
        }
        protected override void DrawAt(Vector3 drawLoc, bool flip = false) => DynamicDrawPhaseAt(DrawPhase.Draw, drawLoc, flip);
    }
}
