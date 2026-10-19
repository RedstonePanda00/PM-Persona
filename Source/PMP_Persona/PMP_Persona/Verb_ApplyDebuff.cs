using RimWorld;
using Verse;
using Verse.Sound;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PMP_Persona
{
    public class Verb_ApplyDebuff : Verb
    {
        private const string WarbringerTacticalHediffDefName = "PMP_TacticalDataWebWarbringer";
        private const string WarbringerBackupBodyDefName = "PM_ColossusPrototype";
        private const string PurificationBlastProjectileDefName = "PMPH_PurificationBlastProjectile";

        private CompHediffOnHit GetHediffComp()
        {
            return EquipmentSource?.GetComp<CompHediffOnHit>();
        }

        private static bool IsStructurePurificationTarget(Thing t)
        {
            if (t == null || t is Pawn || !t.Spawned || !t.def.useHitPoints) return false;
            return t.def.category == ThingCategory.Building;
        }

        private int GetPurificationBlastExtraHitsFromTacticalWeb(CompHediffOnHit comp)
        {
            if (!CasterIsPawn
                || comp.Props.projectileDefName.NullOrEmpty()
                || comp.Props.projectileDefName != PurificationBlastProjectileDefName)
                return 0;
            HediffDef wbWeb = DefDatabase<HediffDef>.GetNamedSilentFail(WarbringerTacticalHediffDefName);
            if (wbWeb == null
                || !CasterPawn.health.hediffSet.HasHediff(wbWeb)
                || CasterPawn.Map == null
                || CasterPawn.Faction == null)
                return 0;
            return SpawnedPawnCountCache.GetCountInFaction(CasterPawn.Map, WarbringerBackupBodyDefName, CasterPawn.Faction);
        }

        protected override bool TryCastShot()
        {
            Thing target = currentTarget.Thing;
            if (target == null || !target.Spawned) return false;

            CompHediffOnHit comp = GetHediffComp();
            if (comp == null) return false;

            if (target is Pawn targetPawn)
            {
                TryApplyBeamMote(comp, targetPawn);

                if (comp.Props.debuffHediff != null)
                    targetPawn.health.AddHediff(comp.Props.debuffHediff);

                if (targetPawn.RaceProps.IsFlesh && !comp.Props.biologicalHediffs.NullOrEmpty())
                {
                    foreach (HediffDef hediffDef in comp.Props.biologicalHediffs)
                        targetPawn.health.AddHediff(hediffDef);
                }

                float damageAmount = comp.Props.vitalPartDamage;
                DamageDef antiArmorShockDef = DefDatabase<DamageDef>.GetNamed("AntiArmorShock");

                if (damageAmount > 0)
                {
                    BodyPartRecord brainPart = targetPawn.health.hediffSet.GetBrain();
                    if (brainPart != null)
                    {
                        int purificationHits = 1 + GetPurificationBlastExtraHitsFromTacticalWeb(comp);
                        for (int h = 0; h < purificationHits; h++)
                        {
                            DamageInfo dinfo = new DamageInfo(
                                antiArmorShockDef,
                                damageAmount,
                                damageAmount * 0.015f,
                                -1f,
                                Caster,
                                brainPart,
                                EquipmentSource?.def
                            );
                            targetPawn.TakeDamage(dinfo);
                        }
                    }
                }
            }
            else if (IsStructurePurificationTarget(target))
            {
                TryApplyBeamMote(comp, target);

                float damageAmount = comp.Props.vitalPartDamage;
                DamageDef antiArmorShockDef = DefDatabase<DamageDef>.GetNamed("AntiArmorShock");

                if (damageAmount > 0 && target.Map != null)
                {
                    int purificationHits = 1 + GetPurificationBlastExtraHitsFromTacticalWeb(comp);
                    for (int h = 0; h < purificationHits; h++)
                    {
                        DamageInfo dinfo = new DamageInfo(
                            antiArmorShockDef,
                            damageAmount,
                            damageAmount * 0.015f,
                            -1f,
                            Caster,
                            null,
                            EquipmentSource?.def
                        );
                        target.TakeDamage(dinfo);
                    }
                }
            }
            else
            {
                return false;
            }

            if (CasterIsPawn)
                CasterPawn.records?.Increment(RecordDefOf.ShotsFired);

            return true;
        }

        private void TryApplyBeamMote(CompHediffOnHit comp, Thing beamTarget)
        {
            if (comp.Props.projectileDefName.NullOrEmpty()) return;
            ThingDef beamDef = DefDatabase<ThingDef>.GetNamedSilentFail(comp.Props.projectileDefName);
            if (beamDef != null && beamDef.projectile?.beamMoteDef != null)
            {
                MoteMaker.MakeInteractionOverlay(
                    beamDef.projectile.beamMoteDef,
                    Caster,
                    beamTarget,
                    Vector3.zero,
                    Vector3.zero
                );
            }
            else
            {
                Log.Warning($"[PMP_Persona] projectileDefName '{comp.Props.projectileDefName}' not found or it lacks a projectile.beamMoteDef.");
            }
        }

        protected override int ShotsPerBurst => verbProps.burstShotCount;
    }

    public class CompProperties_HediffOnHit : CompProperties
    {
        public HediffDef debuffHediff = null;
        public List<HediffDef> biologicalHediffs = null;
        public float vitalPartDamage = 0f;
        public string projectileDefName = null;

        public CompProperties_HediffOnHit()
        {
            this.compClass = typeof(CompHediffOnHit);
        }
    }

    public class CompHediffOnHit : ThingComp
    {
        public CompProperties_HediffOnHit Props => (CompProperties_HediffOnHit)this.props;
    }
}