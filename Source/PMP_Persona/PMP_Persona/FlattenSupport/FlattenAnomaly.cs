using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona.FlattenSupport
{
    internal static class FlattenAnomaly
    {
        // Check DLC availability before accessing anomaly components or Defs.
        internal static CompStudiable StudyComp(Thing_FlattenedPawn sheet)
        {
            if (!ModsConfig.AnomalyActive || sheet?.HeldPawn == null) return null;
            var pawn = sheet.HeldPawn;
            var comp = pawn.TryGetComp<CompStudiable>();
            // Require vanilla anomaly eligibility.
            return comp?.KnowledgeCategory != null && comp.AnomalyKnowledge > 0f
                && (pawn.IsMutant || comp.RequiresHoldingPlatform
                    || (comp.RequiresImprisonment && (pawn.Inhumanized() || pawn.kindDef.studiableAsPrisoner)))
                ? comp : null;
        }
        internal static void RefreshStudyCache(Pawn pawn)
        {
            if (!ModsConfig.AnomalyActive || pawn == null) return;
            var comp = pawn.TryGetComp<CompStudiable>();
            if (comp == null) return;
            // Restore the held pawn's unsaved study reference.
            AccessTools.Field(typeof(CompStudiable), "compStudyUnlocks")?.SetValue(comp, pawn.TryGetComp<CompStudyUnlocks>());
        }
        internal static bool CanStudy(Thing_FlattenedPawn sheet)
        {
            var comp = StudyComp(sheet);
            return comp != null && sheet.Spawned && sheet.Ready && !sheet.HeldPawn.Dead
                && comp.studyEnabled && !comp.Completed && comp.StudyUnlocked()
                && (comp.Props.frequencyTicks <= 0 || comp.TicksTilNextStudy <= 0);
            // Use sealed-sheet custody with vanilla study progress and multipliers.
        }
        internal static bool CanResearch(Pawn pawn) => ModsConfig.AnomalyActive && pawn != null
            && pawn.skills != null && !pawn.WorkTypeIsDisabled(WorkTypeDefOf.DarkStudy)
            && !StatDefOf.EntityStudyRate.Worker.IsDisabledFor(pawn)
            && !StatDefOf.StudyEfficiency.Worker.IsDisabledFor(pawn);
        internal static string Inspect(Thing_FlattenedPawn sheet)
        {
            var comp = StudyComp(sheet);
            if (comp == null) return null;
            if (comp.Completed) return "PMP.Flatten.StudyCompleted".Translate();
            if (!comp.studyEnabled) return "PMP.Flatten.StudyDisabled".Translate();
            if (comp.TicksTilNextStudy > 0) return "CanBeStudiedInDuration".Translate(comp.TicksTilNextStudy.ToStringTicksToPeriod());
            return comp.StudyUnlocked() ? (string)"PMP.Flatten.StudyReady".Translate() : (string)"PMP.Flatten.StudyLocked".Translate();
        }
        internal static IEnumerable<Gizmo> Gizmos(Thing_FlattenedPawn sheet)
        {
            var comp = StudyComp(sheet);
            if (comp == null) yield break;
            yield return new Command_Toggle { defaultLabel = "PMP.Flatten.Study".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Icons/Study"),
                defaultDesc = "PMP.Flatten.StudyDesc".Translate(), isActive = () => comp.studyEnabled,
                toggleAction = () => comp.SetStudyEnabled(!comp.studyEnabled) };
        }
        internal static void StudyInteraction(Thing_FlattenedPawn sheet, Pawn studier)
        {
            if (!CanStudy(sheet) || !CanResearch(studier)) return;
            var comp = StudyComp(sheet);
            comp.Study(studier, .87f, comp.AdjustedAnomalyKnowledgePerStudy * studier.GetStatValue(StatDefOf.StudyEfficiency));
            // Persist completed interactions immediately to prevent duplicate rewards.
            comp.studyInteractions++;
            if (comp.studyInteractions >= 5)
            {
                comp.studyInteractions = 0;
                comp.lastStudiedTick = Find.TickManager.TicksGame;
                TaleRecorder.RecordTale(TaleDefOf.StudiedEntity, studier, sheet.HeldPawn);
            }
        }
    }

    public sealed class WorkGiver_StudyFlattenedPawn : WorkGiver_Scanner
    {
        public override bool Prioritized => true;
        public override bool ShouldSkip(Pawn pawn, bool forced = false) => !FlattenAnomaly.CanResearch(pawn);
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            if (!ModsConfig.AnomalyActive) yield break;
            var def = DefDatabase<ThingDef>.GetNamed("PMP_FlattenedPawn");
            foreach (var thing in pawn.Map.listerThings.ThingsOfDef(def)) yield return thing;
        }
        public override float GetPriority(Pawn pawn, TargetInfo target) =>
            Find.TickManager.TicksGame - (FlattenAnomaly.StudyComp(target.Thing as Thing_FlattenedPawn)?.lastStudiedTick ?? 0);
        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false) =>
            FlattenAnomaly.CanResearch(pawn) && t is Thing_FlattenedPawn sheet && FlattenAnomaly.CanStudy(sheet) && !t.IsForbidden(pawn)
            && pawn.CanReserveAndReach(t, PathEndMode.Touch, Danger.Some, 1, -1, null, forced);
        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false) => ModsConfig.AnomalyActive
            ? JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("PMP_StudyFlattenedPawn"),t) : null;
    }

    public sealed class JobDriver_StudyFlattenedPawn : JobDriver
    {
        private Thing_FlattenedPawn Sheet => TargetThingA as Thing_FlattenedPawn;
        private bool sessionFinished;
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            FlattenAnomaly.CanResearch(pawn) && pawn.Reserve(TargetThingA,job,1,-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (!FlattenAnomaly.CanResearch(pawn)) yield break;
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(() => !FlattenAnomaly.CanResearch(pawn) || (!sessionFinished && !FlattenAnomaly.CanStudy(Sheet)));
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            int ticks=Mathf.Max(1,Mathf.RoundToInt(1200f/Mathf.Max(.01f,pawn.GetStatValue(StatDefOf.EntityStudyRate))));
            var study=Toils_General.WaitWith(TargetIndex.A,ticks,useProgressBar:true);
            study.activeSkill=()=>SkillDefOf.Intellectual;
            study.AddPreTickIntervalAction(delta=>pawn.skills?.Learn(SkillDefOf.Intellectual,.1f*delta));
            yield return study;
            yield return Toils_General.Do(()=>
            {
                var comp=FlattenAnomaly.StudyComp(Sheet);
                FlattenAnomaly.StudyInteraction(Sheet,pawn);
                sessionFinished=comp==null || comp.studyInteractions==0 || comp.Completed;
            });
            yield return Toils_Jump.JumpIf(study,()=>!sessionFinished);
        }
        public override void ExposeData()
        {base.ExposeData();Scribe_Values.Look(ref sessionFinished,"flattenStudyFinished");}
    }
}
