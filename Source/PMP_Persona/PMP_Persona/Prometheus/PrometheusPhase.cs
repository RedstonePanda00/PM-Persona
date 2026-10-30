using RimWorld;
using Verse;
using Verse.AI;

namespace PMP_Persona.Prometheus
{
    public sealed class CompProperties_PrometheusPhase : CompProperties_AbilityEffect
    {
        public CompProperties_PrometheusPhase() { compClass = typeof(CompAbilityEffect_PrometheusPhase); }
    }
    public sealed class CompAbilityEffect_PrometheusPhase : CompAbilityEffect
    {
        public override bool Valid(LocalTargetInfo target, bool throwMessages = false)
        {
            Pawn pawn = parent.pawn;
            return pawn.Spawned && target.Cell != pawn.Position && target.Cell.InBounds(pawn.Map)
                && target.Cell.Standable(pawn.Map) && !target.Cell.Fogged(pawn.Map)
                && target.Cell.GetFirstPawn(pawn.Map) == null && target.Cell.GetDoor(pawn.Map) == null
                && pawn.Position.InHorDistOf(target.Cell, parent.def.verbProperties.range)
                && GenSight.LineOfSight(pawn.Position, target.Cell, pawn.Map);
        }
        public override void Apply(LocalTargetInfo target, LocalTargetInfo dest)
        {
            if (!Valid(target)) return;
            base.Apply(target, dest);
            Pawn pawn = parent.pawn;
            FleckMaker.Static(pawn.Position, pawn.Map, FleckDefOf.PsycastSkipFlashEntry, 0.7f);
            pawn.Position = target.Cell;
            pawn.Notify_Teleported(endCurrentJob: false);
            pawn.GetComp<CompPrometheusTactics>()?.BeginPhaseShieldRecovery();
            FleckMaker.Static(target.Cell, pawn.Map, FleckDefOf.PsycastSkipInnerExit, 0.7f);
        }
    }
    public sealed class CompProperties_PrometheusTactics : CompProperties
    {
        public CompProperties_PrometheusTactics() { compClass = typeof(CompPrometheusTactics); }
    }
    public sealed class CompPrometheusTactics : ThingComp
    {
        private int phaseShieldUntilTick = -1;

        public void BeginPhaseShieldRecovery() => phaseShieldUntilTick = Find.TickManager.TicksGame + 300;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref phaseShieldUntilTick, "phaseShieldUntilTick", -1);
        }

        public override void CompTick()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick > phaseShieldUntilTick - 300 && tick <= phaseShieldUntilTick
                && parent is Pawn recovering && recovering.Spawned && !recovering.Dead)
                recovering.GetComp<ProtossMech.ProtossMechShield>()?.AddShieldEnergy(50f / 300f);
            if (!parent.IsHashIntervalTick(30) || !(parent is Pawn pawn) || !pawn.Spawned || pawn.Dead
                || pawn.Downed || !pawn.Drafted || pawn.equipment?.Primary?.def.IsRangedWeapon != true
                || pawn.CurJob?.playerForced == true) return;
            Ability ability = pawn.abilities?.GetAbility(DefDatabase<AbilityDef>.GetNamed("PMP_PrometheusPhase"));
            if (ability == null || !ability.CanCast) return;
            Pawn threat = null;
            foreach (Pawn other in pawn.Map.mapPawns.AllPawnsSpawned)
                if (other != pawn && !other.Dead && !other.Downed && other.HostileTo(pawn)
                    && other.Position.InHorDistOf(pawn.Position, 2.9f)) { threat = other; break; }
            if (threat == null) return;
            IntVec3 best = IntVec3.Invalid;
            float score = pawn.Position.DistanceToSquared(threat.Position);
            var effect = ability.CompOfType<CompAbilityEffect_PrometheusPhase>();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(pawn.Position, 8, false))
            {
                if (!cell.InBounds(pawn.Map) || !cell.InAllowedArea(pawn) || !effect.Valid(cell)) continue;
                float distance = cell.DistanceToSquared(threat.Position);
                if (distance > score) { best = cell; score = distance; }
            }
            if (best.IsValid) pawn.jobs.TryTakeOrderedJob(ability.GetJob(best, best), JobTag.Misc);
        }
    }
}
