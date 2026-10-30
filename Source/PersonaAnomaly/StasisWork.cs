using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP.Anomaly {
    public static class StasisWork {
        public static bool CanHaul(Pawn pawn,Building_StasisField field) => field.Spawned&&field.Powered&&!field.HasAnyContents
            &&field.pendingEntity is Thing target&&target.Spawned&&target.Map==field.Map&&Building_StasisField.CanCarry(target)
            &&!field.IsForbidden(pawn)&&!target.IsForbidden(pawn)
            &&pawn.CanReserveAndReach(field,PathEndMode.Touch,Danger.Some)
            &&pawn.CanReserveAndReach(target,PathEndMode.ClosestTouch,Danger.Some);
        public static Job HaulJob(Building_StasisField field) {var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("PMP_CarryToStasis"),field,field.pendingEntity);job.count=1;return job;}
        public static bool CanResearch(Pawn pawn)=>pawn.skills!=null&&!pawn.WorkTypeIsDisabled(WorkTypeDefOf.DarkStudy)
            &&!StatDefOf.EntityStudyRate.Worker.IsDisabledFor(pawn)&&!StatDefOf.StudyEfficiency.Worker.IsDisabledFor(pawn);
        public static void Study(Building_StasisField field,Pawn pawn) {
            if(!field.CanStudy||!CanResearch(pawn))return;
            var comp=field.StudyComp;
            comp.Study(pawn,.87f,comp.AdjustedAnomalyKnowledgePerStudy*pawn.GetStatValue(StatDefOf.StudyEfficiency));
            if(++comp.studyInteractions>=5) {
                comp.studyInteractions=0;comp.lastStudiedTick=Find.TickManager.TicksGame;
                TaleRecorder.RecordTale(TaleDefOf.StudiedEntity,pawn,field.ContainedThing);
            }
        }
    }
    public sealed class WorkGiver_CarryToStasis : WorkGiver_Scanner {
        public override ThingRequest PotentialWorkThingRequest=>ThingRequest.ForDef(DefDatabase<ThingDef>.GetNamed("PMP_StasisField"));
        public override bool HasJobOnThing(Pawn pawn,Thing t,bool forced=false)=>t is Building_StasisField f&&StasisWork.CanHaul(pawn,f);
        public override Job JobOnThing(Pawn pawn,Thing t,bool forced=false)=>StasisWork.HaulJob((Building_StasisField)t);
    }
    public sealed class JobDriver_CarryToStasis : JobDriver {
        private Building_StasisField Field=>TargetThingA as Building_StasisField;
        public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(TargetThingA,job,1,-1,null,errorOnFailed)&&pawn.Reserve(TargetThingB,job,1,-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils() {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnDestroyedOrNull(TargetIndex.B);
            this.FailOn(()=>!Field.Powered||Field.HasAnyContents||Field.pendingEntity!=TargetThingB||!Building_StasisField.CanCarry(TargetThingB));
            yield return Toils_Goto.GotoThing(TargetIndex.B,PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            yield return Toils_General.WaitWith(TargetIndex.A,120,useProgressBar:true);
            yield return Toils_General.Do(()=>Field.TryAcceptThing(pawn.carryTracker.CarriedThing));
        }
    }
    public sealed class WorkGiver_StudyStasis : WorkGiver_Scanner {
        public override bool Prioritized=>true;
        public override bool ShouldSkip(Pawn pawn,bool forced=false)=>!StasisWork.CanResearch(pawn);
        public override ThingRequest PotentialWorkThingRequest=>ThingRequest.ForDef(DefDatabase<ThingDef>.GetNamed("PMP_StasisField"));
        public override float GetPriority(Pawn pawn,TargetInfo target)=>Find.TickManager.TicksGame-((target.Thing as Building_StasisField)?.StudyComp?.lastStudiedTick??0);
        public override bool HasJobOnThing(Pawn pawn,Thing t,bool forced=false)=>StasisWork.CanResearch(pawn)&&t is Building_StasisField f&&f.CanStudy&&!t.IsForbidden(pawn)&&pawn.CanReserveAndReach(t,PathEndMode.Touch,Danger.Some);
        public override Job JobOnThing(Pawn pawn,Thing t,bool forced=false)=>JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("PMP_StudyStasis"),t);
    }
    public sealed class JobDriver_StudyStasis : JobDriver {
        private Building_StasisField Field=>TargetThingA as Building_StasisField;
        private bool finished;
        public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(TargetThingA,job,1,-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils() {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOn(()=>!StasisWork.CanResearch(pawn)||(!finished&&!Field.CanStudy));
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            var study=Toils_General.WaitWith(TargetIndex.A,Mathf.Max(1,Mathf.RoundToInt(1200f/Mathf.Max(.01f,pawn.GetStatValue(StatDefOf.EntityStudyRate)))),useProgressBar:true);
            study.activeSkill=()=>SkillDefOf.Intellectual;
            study.AddPreTickIntervalAction(delta=>pawn.skills?.Learn(SkillDefOf.Intellectual,.1f*delta));
            yield return study;
            yield return Toils_General.Do(()=>{StasisWork.Study(Field,pawn);finished=Field.StudyComp==null||Field.StudyComp.studyInteractions==0||Field.StudyComp.Completed;});
            yield return Toils_Jump.JumpIf(study,()=>!finished);
        }
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref finished,"stasisStudyFinished");}
    }
}
