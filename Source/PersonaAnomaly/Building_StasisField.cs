using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Anomaly {
    public sealed class Building_StasisField : Building_Casket, ISuspendableThingHolder, IThingHolderWithDrawnPawn {
        public Thing pendingEntity;
        public bool Powered => GetComp<CompPowerTrader>()?.PowerOn == true;
        public bool IsContentsSuspended => true;
        public Pawn HeldPawn => ContainedThing as Pawn;
        public float HeldPawnDrawPos_Y => DrawPos.y + .04f;
        public float HeldPawnBodyAngle => 0;
        public PawnPosture HeldPawnPosture => PawnPosture.Standing;
        public override bool CanOpen => false;
        public CompStudiable StudyComp => ContainedThing?.TryGetComp<CompStudiable>();
        public bool CanStudy => Spawned && Powered && StudyComp is CompStudiable study && study.studyEnabled
            && !study.Completed && study.StudyUnlocked() && study.TicksTilNextStudy <= 0;
        private Pawn preparedPawn;
        private PawnDrawParms prepared;
        private bool preparedValid;

        public static bool IsEntity(Thing thing) {
            if (thing == null || thing.Destroyed) return false;
            if (thing is Pawn p) return !p.Dead && (p.IsMutant || p.TryGetComp<CompHoldingPlatformTarget>() != null);
            var study = thing.TryGetComp<CompStudiable>();
            return thing.def.category == ThingCategory.Item && !(thing is Corpse)
                && study?.KnowledgeCategory != null && study.AnomalyKnowledge > 0;
        }
        public static bool CanCarry(Thing thing) => IsEntity(thing) && (thing is not Pawn pawn
            || pawn.Downed || pawn.GetComp<CompActivity>()?.IsDormant == true);
        public override bool Accepts(Thing thing) => Powered && !HasAnyContents && IsEntity(thing);
        public override bool TryAcceptThing(Thing thing, bool allowSpecialEffects = true) {
            if (!Accepts(thing)) return false;
            // Real containment retains the original object. No ticking of its owner.
            var oldMap=thing.Map;var oldCell=thing.Position;var oldRotation=thing.Rotation;
            if (thing.Spawned) thing.DeSpawn();
            if (innerContainer.TryAddOrTransfer(thing, 1) != 1) {
                if(oldMap!=null&&!thing.Destroyed&&thing.holdingOwner==null)GenSpawn.Spawn(thing,oldCell,oldMap,oldRotation);
                return false;
            }
            pendingEntity = null;
            RefreshStudyCache();
            return true;
        }
        public void RefreshStudyCache() {
            // The native public hook initializes study unlocks and updates the
            // study manager. It explicitly supports things held by a mapped owner.
            StudyComp?.PostSpawnSetup(respawningAfterLoad: true);
        }
        protected override void Tick() {
            base.Tick();
            // Poll the settled power state, not PowerTurnedOff signals: WildHunt's
            // flare immunity restores its marked consumers inside PowerNetTick.
            if (HasAnyContents && !Powered) EjectContents();
            if (pendingEntity?.Destroyed == true) pendingEntity = null;
        }
        public override void EjectContents() {
            if (!Spawned) return;
            innerContainer.TryDropAll(InteractionCell, Map, ThingPlaceMode.Near);
            preparedPawn = null; preparedValid = false;
        }
        public override void Destroy(DestroyMode mode = DestroyMode.Vanish) {
            // Drop before Building_Casket's destruction path can injure its pawn.
            if (Spawned && HasAnyContents) EjectContents();
            base.Destroy(mode);
        }
        public override void SpawnSetup(Map map, bool respawningAfterLoad) {
            base.SpawnSetup(map, respawningAfterLoad);
            RefreshStudyCache();
        }
        public override void ExposeData() {
            base.ExposeData(); Scribe_References.Look(ref pendingEntity, "stasisPendingEntity");
        }
        public void Assign(Thing entity) { if (!HasAnyContents && IsEntity(entity)) pendingEntity = entity; }
        public override string GetInspectString() {
            string text = base.GetInspectString();
            if (HasAnyContents) text += "\n" + "PMP.Stasis.Suspended".Translate();
            if (pendingEntity != null) text += "\n" + "PMP.Stasis.Assigned".Translate(pendingEntity.LabelShortCap);
            if (StudyComp is CompStudiable study) {
                if (study.Completed) text += "\n" + "PMP.Stasis.StudyComplete".Translate();
                else if (study.TicksTilNextStudy > 0) text += "\n" + "CanBeStudiedInDuration".Translate(study.TicksTilNextStudy.ToStringTicksToPeriod());
                else if (!study.StudyUnlocked()) text += "\n" + "PMP.Stasis.StudyLocked".Translate();
            }
            return text;
        }
        public override IEnumerable<Gizmo> GetGizmos() {
            foreach (var gizmo in base.GetGizmos()) yield return gizmo;
            if (Faction != Faction.OfPlayer) yield break;
            if (!HasAnyContents) {
                var capture = new Command_Action { defaultLabel = "PMP.Stasis.Assign".Translate(),
                    defaultDesc = "PMP.Stasis.AssignDesc".Translate(), icon = TexCommand.SelectCarriedPawn,
                    action = () => Find.Targeter.BeginTargeting(new TargetingParameters {
                        canTargetPawns = true, canTargetItems = true, canTargetBuildings = false,
                        validator = t => t.HasThing && t.Thing.Spawned && t.Thing.Map == Map && CanCarry(t.Thing)
                    }, t => Assign(t.Thing)) };
                if (!Powered) capture.Disable("NoPower".Translate());
                yield return capture;
                if (pendingEntity != null) yield return new Command_Action {defaultLabel="Cancel".Translate(),icon=TexCommand.ClearPrioritizedWork,action=()=>pendingEntity=null};
            } else {
                yield return new Command_Action { defaultLabel="PMP.Stasis.Release".Translate(),
                    defaultDesc="PMP.Stasis.ReleaseDesc".Translate(),icon=TexCommand.DropCarriedPawn,action=EjectContents };
                if (StudyComp is CompStudiable study) yield return new Command_Toggle {
                    defaultLabel="PMP.Stasis.Study".Translate(), defaultDesc="PMP.Stasis.StudyDesc".Translate(),
                    icon=ContentFinder<Texture2D>.Get("UI/Icons/Study"),isActive=()=>study.studyEnabled,
                    toggleAction=()=>study.SetStudyEnabled(!study.studyEnabled) };
            }
        }
        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn pawn) {
            foreach(var option in base.GetFloatMenuOptions(pawn)) yield return option;
            if (StasisWork.CanHaul(pawn,this)) yield return FloatMenuUtility.DecoratePrioritizedTask(
                new FloatMenuOption("PMP.Stasis.Carry".Translate(pendingEntity.LabelShortCap),()=>pawn.jobs.TryTakeOrderedJob(StasisWork.HaulJob(this))),pawn,this);
        }
        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false) {
            base.DynamicDrawPhaseAt(phase, drawLoc, flip);
            var pawn = phase == DrawPhase.ParallelPreDraw ? preparedPawn : HeldPawn;
            if (pawn != null) {
                var tree=pawn.Drawer.renderer.renderTree;
                if (phase==DrawPhase.EnsureInitialized) {
                    preparedPawn=pawn;preparedValid=false;
                    prepared=PawnDrawParms.Default;prepared.pawn=pawn;prepared.facing=Rot4.South;
                    prepared.posture=PawnPosture.Standing;prepared.rotDrawMode=RotDrawMode.Fresh;
                    prepared.flags=PawnRenderFlags.Headgear|PawnRenderFlags.Clothes|PawnRenderFlags.NeverAimWeapon;
                    prepared.tint=Color.white;
                    prepared.matrix=Matrix4x4.TRS(drawLoc+new Vector3(0,.04f,.08f),Quaternion.identity,Vector3.one*.8f);
                    tree.EnsureInitialized(prepared.flags);
                } else if(phase==DrawPhase.ParallelPreDraw) {tree.ParallelPreDraw(prepared);preparedValid=true;}
                else if(phase==DrawPhase.Draw&&preparedValid) {tree.Draw(prepared);preparedValid=false;}
            }
            if(phase==DrawPhase.Draw&&HasAnyContents) {
                if(HeldPawn==null) ContainedThing.Graphic.Draw(drawLoc+new Vector3(0,.05f,.08f),Rot4.North,ContainedThing);
                StasisVisual.Draw(drawLoc);
            }
        }
    }
}
