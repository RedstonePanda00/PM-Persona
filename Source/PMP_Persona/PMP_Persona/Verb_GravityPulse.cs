using RimWorld;
using UnityEngine;
using Verse;
using System.Collections.Generic;
using System.Linq;

namespace PMP_Persona
{
    public class Verb_GravityPulse : Verb
    {
        // Tactical network: backup bodies = Purifier Immortals (PM_PurifierImmortal) in same faction. Uses cache.
        private const string TaldarinBackupBodyDefName = "PM_PurifierImmortal";

        private int GetTacticalNetworkBackupBodyCount(Map map)
        {
            if (map == null || CasterPawn == null || CasterPawn.Faction == null) return 0;
            return SpawnedPawnCountCache.GetCountInFaction(map, TaldarinBackupBodyDefName, CasterPawn.Faction);
        }

        private DamageDef GetConfiguredDamageDef()
        {
            if (EquipmentSource != null)
            {
                var comp = EquipmentSource.GetComp<CompDamageConfig>();
                if (comp != null && comp.ConfiguredDamage != null)
                    return comp.ConfiguredDamage;
            }
            return DefDatabase<DamageDef>.GetNamed("AntiArmorShock", true);
        }

        public override void WarmupComplete()
        {
            base.WarmupComplete();

            if (currentTarget.Thing == null)
            {
                return;
            }

            IntVec3 targetOriginalPos = currentTarget.Thing.Position;
            Map targetMap = currentTarget.Thing.Map;
            DamageDef usedDef = GetConfiguredDamageDef();
            int backupBodyCount = GetTacticalNetworkBackupBodyCount(targetMap);

            // Step 1: initial damage to primary target
            if (currentTarget.Thing is Pawn targetPawn)
            {
                float totalArmor = 0f;
                if (targetPawn.apparel?.WornApparel != null)
                {
                    foreach (var app in targetPawn.apparel.WornApparel)
                    {
                        totalArmor += app.GetStatValue(StatDefOf.ArmorRating_Sharp, true);
                    }
                }
                
                float baseArmor = targetPawn.def?.statBases?.FirstOrDefault(s => s.stat == StatDefOf.ArmorRating_Sharp)?.value ?? 0f;
                float finalArmor = totalArmor + baseArmor;
                float log2Armor = Mathf.Log(1f + finalArmor, 2f);
                float finalDamage = 20f + 80f * log2Armor;
                
                DamageInfo dinfo = new DamageInfo(
                    def: usedDef,
                    amount: finalDamage,
                    armorPenetration: 0f,
                    angle: CasterPawn?.Rotation.AsAngle ?? 0f,
                    instigator: Caster,
                    hitPart: null,
                    weapon: EquipmentSource?.def
                );
                targetPawn.TakeDamage(dinfo);
            }
            else if (currentTarget.Thing != null)
            {
                DamageInfo dinfo = new DamageInfo(
                    usedDef,
                    200f,
                    0f,
                    angle: CasterPawn?.Rotation.AsAngle ?? 0f,
                    instigator: Caster,
                    null,
                    weapon: EquipmentSource?.def
                );
                currentTarget.Thing.TakeDamage(dinfo);
            }

            // Tactical network: extra attack damage per backup body when target is building
            if (backupBodyCount > 0 && currentTarget.Thing != null && !(currentTarget.Thing is Pawn))
            {
                for (int i = 0; i < backupBodyCount; i++)
                {
                    DamageInfo dinfo = new DamageInfo(
                        usedDef, 200f, 0f,
                        angle: CasterPawn?.Rotation.AsAngle ?? 0f,
                        instigator: Caster, null, weapon: EquipmentSource?.def
                    );
                    currentTarget.Thing.TakeDamage(dinfo);
                }
            }

            if (CasterIsPawn)
            {
                CasterPawn.records?.Increment(RecordDefOf.ShotsFired);
            }

            float pullRadius = 5f;
            SoundDef jumpLandSound = DefDatabase<SoundDef>.GetNamedSilentFail("JumpPackLand");

            List<Pawn> pawnsToPull = GenRadial.RadialDistinctThingsAround(targetOriginalPos, targetMap, pullRadius, true)
                .OfType<Pawn>()
                .Where(p => p != currentTarget.Thing && p.HostileTo(Caster))
                .ToList();

            // Step 2 (AoE) MUST run before Step 3 (pull): damaging pawns inside PawnFlyer can empty the container and crash DestinationPos on draw/tick (vanilla PawnFlyer).
            float secondaryDamageAmount = 100f;

            HashSet<Pawn> pawnsToDamageSet = new HashSet<Pawn>(
                GenRadial.RadialDistinctThingsAround(targetOriginalPos, targetMap, pullRadius, true)
                    .OfType<Pawn>()
                    .Where(p => p.HostileTo(Caster))
            );
            if (currentTarget.Thing is Pawn mainPawn && mainPawn.HostileTo(Caster))
                pawnsToDamageSet.Add(mainPawn);

            foreach (Pawn p in pawnsToDamageSet)
            {
                if (p.Dead) continue;
                DamageInfo areaDamageInfo = new DamageInfo(
                    usedDef,
                    secondaryDamageAmount,
                    0f,
                    -1f,
                    Caster,
                    null,
                    EquipmentSource?.def
                );
                p.TakeDamage(areaDamageInfo);
                if (p.Spawned)
                    MoteMaker.MakeStaticMote(p.Position.ToVector3Shifted(), p.Map, ThingDefOf.Mote_Stun, 1f);
            }

            // Tactical network: extra Step2 AOE per backup body when target is pawn.
            if (backupBodyCount > 0 && currentTarget.Thing is Pawn)
            {
                for (int i = 0; i < backupBodyCount; i++)
                {
                    foreach (Pawn p in pawnsToDamageSet)
                    {
                        if (p.Dead) continue;
                        DamageInfo areaDamageInfo = new DamageInfo(
                            usedDef, secondaryDamageAmount, 0f, -1f, Caster, null, EquipmentSource?.def
                        );
                        p.TakeDamage(areaDamageInfo);
                        if (p.Spawned)
                            MoteMaker.MakeStaticMote(p.Position.ToVector3Shifted(), p.Map, ThingDefOf.Mote_Stun, 1f);
                    }
                }
            }

            // Step 3: pull survivors to target (after AoE so flyers are not damaged mid-flight — avoids empty PawnFlyer / DestinationPos NRE).
            foreach (Pawn p in pawnsToPull)
            {
                if (p.Position == targetOriginalPos) continue;
                if (!p.Spawned || p.Map != targetMap || p.Dead) continue;

                Vector3 dustAt = p.TrueCenter() + Gen.RandomHorizontalVector(0.5f);
                PawnFlyer flyer = PawnFlyer.MakeFlyer(
                    ThingDefOf.PawnFlyer,
                    p,
                    targetOriginalPos,
                    null,
                    jumpLandSound,
                    flyWithCarriedThing: false,
                    null,
                    null,
                    default
                );
                if (flyer != null && flyer.FlyingPawn != null)
                {
                    FleckMaker.ThrowDustPuff(dustAt, targetMap, 2f);
                    GenSpawn.Spawn(flyer, targetOriginalPos, targetMap);
                }
                else if (flyer != null)
                {
                    flyer.Destroy();
                }
            }
        }

        protected override bool TryCastShot()
        {
            if (currentTarget.HasThing && currentTarget.Thing.Map != caster.Map)
                return false;
            // Same as Verb_LaunchProjectile / ProtossMech.Verb_AntiArmor: walk-a & zero-warmup facing use LastShotTick.
            if (Find.TickManager != null)
                lastShotTick = Find.TickManager.TicksGame;
            return true;
        }

        protected override int ShotsPerBurst => verbProps.burstShotCount;
    }

    // Damage config comp for weapon
    public class CompProperties_DamageConfig : CompProperties
    {
        public DamageDef damageDef;
        public CompProperties_DamageConfig()
        {
            this.compClass = typeof(CompDamageConfig);
        }
    }

    public class CompDamageConfig : ThingComp
    {
        public DamageDef ConfiguredDamage => Props.damageDef;
        public CompProperties_DamageConfig Props => (CompProperties_DamageConfig)this.props;
    }
}