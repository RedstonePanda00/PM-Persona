using System.Collections.Generic;
using RimWorld;
using RsPandaLibrary;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PMP_Persona
{
    public class Verb_WarbringerSweepLaser : Verb, IContinuousTurretVerb
    {
        private const int TargetLostHoldTicks = 60;

        private sealed class RuntimeState
        {
            public Vector3 currentAimWorld;
            public bool beamEngaged;
            public Thing lastTargetThing;
            public int targetLostHoldTicksLeft;
            public MoteDualAttached beamMote;
            public Sustainer beamSustainer;
            public int damageTickCounter;
        }

        private static readonly Dictionary<Verb, RuntimeState> StatesByVerb = new Dictionary<Verb, RuntimeState>();
        private static readonly List<IntVec3> TmpLineCells = new List<IntVec3>();
        private static readonly HashSet<Thing> TmpDamagedThings = new HashSet<Thing>();
        private static bool warnedBadBeamMoteDef;

        private VerbProperties_WarbringerSweepLaser SweepProps => verbProps as VerbProperties_WarbringerSweepLaser;

        public void TickContinuous(CompMultiTurretGun.TurretUnit unit, LocalTargetInfo target, bool canShoot)
        {
            RuntimeState state = GetOrCreateState();
            Map map = caster?.Map;
            if (!canShoot || caster == null || !caster.Spawned || map == null)
            {
                StopContinuous(unit);
                return;
            }

            if (!target.IsValid || !CanHitTargetFromTurret(unit, target))
            {
                if (state.beamEngaged && state.targetLostHoldTicksLeft < TargetLostHoldTicks)
                {
                    state.targetLostHoldTicksLeft++;
                    MaintainBeamVisuals(state, unit, GetTurretMuzzleWorld(unit), state.currentAimWorld, map);
                    return;
                }

                StopContinuous(unit);
                return;
            }

            state.targetLostHoldTicksLeft = 0;

            Vector3 muzzle = GetTurretMuzzleWorld(unit);
            Vector3 desiredAim = target.CenterVector3;
            Thing targetThing = target.Thing;

            if (!state.beamEngaged)
            {
                state.currentAimWorld = desiredAim;
                state.beamEngaged = true;
            }
            else
            {
                float step = GetAimStep(muzzle, desiredAim);
                state.currentAimWorld = Vector3.MoveTowards(state.currentAimWorld, desiredAim, step);
            }

            state.lastTargetThing = targetThing;

            MaintainBeamVisuals(state, unit, muzzle, state.currentAimWorld, map);

            state.damageTickCounter++;
            if (state.damageTickCounter >= (SweepProps?.damageTickInterval ?? 5))
            {
                state.damageTickCounter = 0;
                ApplyBeamDamage(muzzle, state.currentAimWorld, map);
                SpawnBeamFlecks(muzzle, state.currentAimWorld, map);
            }
        }

        private float GetAimStep(Vector3 muzzle, Vector3 desiredAim)
        {
            float range = Vector3.Distance(muzzle, desiredAim);
            return Mathf.Max(0.02f, range * (SweepProps?.aimRotateSpeedDeg ?? 60f) * Mathf.Deg2Rad / 60f);
        }

        private void MaintainBeamVisuals(
            RuntimeState state,
            CompMultiTurretGun.TurretUnit unit,
            Vector3 muzzle,
            Vector3 aimWorld,
            Map map)
        {
            unit.curRotation = (aimWorld - muzzle).AngleFlat();
            EnsureBeamMote(state, map);
            UpdateBeamMote(state, muzzle, aimWorld, map);
            MaintainBeamSound(state, muzzle, map);
        }

        public void StopContinuous(CompMultiTurretGun.TurretUnit unit)
        {
            if (!StatesByVerb.TryGetValue(this, out RuntimeState state))
                return;

            DestroyBeamMote(state);
            EndBeamSound(state);
            state.beamEngaged = false;
            state.lastTargetThing = null;
            state.targetLostHoldTicksLeft = 0;
            state.damageTickCounter = 0;
        }

        private Vector3 GetTurretMuzzleWorld(CompMultiTurretGun.TurretUnit unit)
        {
            if (CompMultiTurretGun.TryGetTurretShootFromVerb(this, out _, out Vector3 draw))
                return draw;

            return caster?.DrawPos ?? Vector3.zero;
        }

        public override bool CanHitTarget(LocalTargetInfo targ)
        {
            CompMultiTurretGun.TurretUnit unit = CompMultiTurretGun.VerbEvaluatingUnit;
            if (unit != null && ReferenceEquals(unit.verb, this))
                return CanHitTargetFromTurret(unit, targ);

            return base.CanHitTarget(targ);
        }

        private bool CanHitTargetFromTurret(CompMultiTurretGun.TurretUnit unit, LocalTargetInfo targ)
        {
            if (CompMultiTurretGun.TryGetTurretShootFromVerb(this, out IntVec3 root, out _))
                return CanHitTargetFrom(root, targ);

            return CanHitTargetFrom(caster.Position, targ);
        }

        private RuntimeState GetOrCreateState()
        {
            if (!StatesByVerb.TryGetValue(this, out RuntimeState state))
            {
                state = new RuntimeState();
                StatesByVerb[this] = state;
            }

            return state;
        }

        private void EnsureBeamMote(RuntimeState state, Map map)
        {
            if (state.beamMote != null && !state.beamMote.Destroyed)
                return;

            ThingDef moteDef = SweepProps?.beamMoteDef ?? verbProps.beamMoteDef;
            if (moteDef == null)
                return;

            Thing moteThing = ThingMaker.MakeThing(moteDef);
            MoteDualAttached dualMote = moteThing as MoteDualAttached;
            if (dualMote == null)
            {
                if (!warnedBadBeamMoteDef)
                {
                    warnedBadBeamMoteDef = true;
                    Log.Warning("[PMP_Persona] WarbringerSweepLaser beamMoteDef must use thingClass MoteDualAttached (e.g. PMPMote_ChronoBeam).");
                }

                moteThing.Destroy();
                return;
            }

            if (SweepProps != null)
                dualMote.instanceColor = SweepProps.beamMoteColor;

            GenSpawn.Spawn(dualMote, caster.Position, map);
            state.beamMote = dualMote;
        }

        private static void UpdateBeamMote(RuntimeState state, Vector3 muzzle, Vector3 aimWorld, Map map)
        {
            if (state.beamMote == null || state.beamMote.Destroyed)
                return;

            IntVec3 muzzleCell = muzzle.ToIntVec3();
            IntVec3 aimCell = aimWorld.ToIntVec3();
            Vector3 offsetA = muzzle - muzzleCell.ToVector3Shifted();
            Vector3 offsetB = aimWorld - aimCell.ToVector3Shifted();

            state.beamMote.UpdateTargets(
                new TargetInfo(muzzleCell, map),
                new TargetInfo(aimCell, map),
                offsetA,
                offsetB);
            state.beamMote.Maintain();
        }

        private void MaintainBeamSound(RuntimeState state, Vector3 muzzle, Map map)
        {
            SoundDef sound = SweepProps?.soundCastBeam ?? verbProps.soundCastBeam;
            if (sound == null)
                return;

            if (state.beamSustainer == null || state.beamSustainer.Ended)
            {
                SoundInfo info = SoundInfo.InMap(new TargetInfo(muzzle.ToIntVec3(), map), MaintenanceType.PerTick);
                state.beamSustainer = sound.TrySpawnSustainer(info);
            }

            state.beamSustainer?.Maintain();
        }

        private static void EndBeamSound(RuntimeState state)
        {
            if (state.beamSustainer != null && !state.beamSustainer.Ended)
                state.beamSustainer.End();
            state.beamSustainer = null;
        }

        private static void DestroyBeamMote(RuntimeState state)
        {
            if (state.beamMote != null && !state.beamMote.Destroyed)
                state.beamMote.Destroy();
            state.beamMote = null;
        }

        private void ApplyBeamDamage(Vector3 muzzle, Vector3 aimWorld, Map map)
        {
            if (SweepProps == null)
                return;

            DamageDef damageDef = verbProps.beamDamageDef
                ?? DefDatabase<DamageDef>.GetNamedSilentFail("Beam")
                ?? DamageDefOf.Burn;
            TmpLineCells.Clear();
            CollectLineCells(muzzle, aimWorld, TmpLineCells);
            TmpDamagedThings.Clear();

            for (int i = 0; i < TmpLineCells.Count; i++)
            {
                List<Thing> things = TmpLineCells[i].GetThingList(map);
                for (int t = 0; t < things.Count; t++)
                {
                    Thing thing = things[t];
                    if (thing == null || thing == caster || TmpDamagedThings.Contains(thing))
                        continue;

                    if (!thing.def.useHitPoints && !(thing is Pawn))
                        continue;

                    TmpDamagedThings.Add(thing);
                    DamageInfo dinfo = new DamageInfo(
                        damageDef,
                        SweepProps.damagePerTick,
                        SweepProps.armorPenetration,
                        -1f,
                        caster,
                        null,
                        EquipmentSource?.def);
                    thing.TakeDamage(dinfo);
                }
            }
        }

        private void SpawnBeamFlecks(Vector3 muzzle, Vector3 aimWorld, Map map)
        {
            if (SweepProps == null)
                return;

            FleckDef lineFleck = verbProps.beamLineFleckDef;
            if (lineFleck != null && Rand.Chance(SweepProps.beamLineFleckChance))
            {
                Vector3 pos = Vector3.Lerp(muzzle, aimWorld, Rand.Value);
                FleckMaker.Static(pos, map, lineFleck);
            }

            FleckDef groundFleck = verbProps.beamGroundFleckDef;
            if (groundFleck != null && Rand.Chance(SweepProps.beamGroundFleckChance))
            {
                IntVec3 cell = aimWorld.ToIntVec3();
                if (cell.InBounds(map))
                    FleckMaker.Static(cell, map, groundFleck);
            }
        }

        protected override bool TryCastShot()
        {
            return false;
        }

        private static void CollectLineCells(Vector3 start, Vector3 end, List<IntVec3> cells)
        {
            cells.Clear();
            float dist = Vector3.Distance(start, end);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist));
            IntVec3 last = IntVec3.Invalid;
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(start, end, i / (float)steps);
                IntVec3 cell = p.ToIntVec3();
                if (cell == last)
                    continue;
                last = cell;
                cells.Add(cell);
            }
        }
    }
}
