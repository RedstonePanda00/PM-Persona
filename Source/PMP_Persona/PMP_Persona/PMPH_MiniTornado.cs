using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona
{
    // Ink-green tornado visuals (vanilla-style mesh); pull hostiles + vanilla TornadoScratch on pawns/buildings/plants/items every 30 ticks.
    [StaticConstructorOnStartup]
    public class PMPH_MiniTornado : ThingWithComps
    {
        public const int EffectIntervalTicks = 30;

        public float effectRadius = 4.2f;

        public int remainingDurationTicks = 120;

        public Pawn instigator;

        private int spawnTick;

        private int leftFadeOutTicks = -1;

        private const float InkR = 0.14f;

        private const float InkG = 0.42f;

        private const float InkB = 0.30f;

        private static readonly Material TornadoMaterial =
            MaterialPool.MatFrom("Things/Ethereal/Tornado", ShaderDatabase.Transparent, MapMaterialRenderQueues.Tornado);

        private static readonly FloatRange PartsDistanceFromCenter = new FloatRange(1f, 10f);

        private static readonly float ZOffsetBias = -4f * PartsDistanceFromCenter.min;

        private static readonly MaterialPropertyBlock MatBlock = new MaterialPropertyBlock();

        private static readonly List<Pawn> PawnsTmp = new List<Pawn>();

        private static readonly HashSet<Thing> ThingsHitThisTick = new HashSet<Thing>();

        public override Vector2 DrawSize
        {
            get
            {
                float s = Mathf.Clamp(effectRadius / 4.2f, 0.15f, 2.5f);
                return new Vector2(45f * s, 100f * s);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref effectRadius, "effectRadius", 4.2f);
            Scribe_Values.Look(ref remainingDurationTicks, "remainingDurationTicks", 60);
            Scribe_Values.Look(ref spawnTick, "spawnTick", 0);
            Scribe_Values.Look(ref leftFadeOutTicks, "leftFadeOutTicks", -1);
            Scribe_References.Look(ref instigator, "instigator");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                spawnTick = Find.TickManager.TicksGame;
                leftFadeOutTicks = -1;
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned)
            {
                return;
            }

            if (leftFadeOutTicks > 0)
            {
                leftFadeOutTicks--;
                if (leftFadeOutTicks == 0)
                {
                    Destroy();
                }

                return;
            }

            if (remainingDurationTicks <= 0)
            {
                return;
            }

            if (this.IsHashIntervalTick(EffectIntervalTicks))
            {
                PullHostilesThenApplyTornadoDamageToAll();
            }

            remainingDurationTicks--;
            if (remainingDurationTicks == 0)
            {
                leftFadeOutTicks = 120;
            }
        }

        private void PullHostilesThenApplyTornadoDamageToAll()
        {
            Map map = Map;
            if (map == null || instigator == null)
            {
                return;
            }

            IntVec3 center = Position;

            PawnsTmp.Clear();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, effectRadius, useCenter: true))
            {
                if (!cell.InBounds(map) || CellImmuneToDamage(cell, map))
                {
                    continue;
                }

                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    if (things[i] is Pawn pawn && !pawn.Dead && pawn.HostileTo(instigator) && !PawnsTmp.Contains(pawn))
                    {
                        PawnsTmp.Add(pawn);
                    }
                }
            }

            for (int p = 0; p < PawnsTmp.Count; p++)
            {
                Pawn pawn = PawnsTmp[p];
                if (pawn == null || !pawn.Spawned || pawn.Map != map)
                {
                    continue;
                }

                TryPullOneStepToward(pawn, center, map);
            }

            PawnsTmp.Clear();

            ThingsHitThisTick.Clear();
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(center, effectRadius, useCenter: true))
            {
                if (!cell.InBounds(map) || CellImmuneToDamage(cell, map))
                {
                    continue;
                }

                float dist = cell.DistanceTo(center);
                float baseFactor = GenMath.LerpDouble(0f, effectRadius, 1f, 0.2f, dist);
                List<Thing> things = cell.GetThingList(map);
                for (int i = 0; i < things.Count; i++)
                {
                    Thing t = things[i];
                    if (t.Destroyed || !ThingsHitThisTick.Add(t))
                    {
                        continue;
                    }

                    if (t is Pawn pawn)
                    {
                        if (pawn.Dead || !pawn.HostileTo(instigator))
                        {
                            continue;
                        }

                        ApplyVanillaTornadoScratchToPawn(pawn, baseFactor);
                    }
                    else if (t.def.category == ThingCategory.Building)
                    {
                        ApplyVanillaTornadoScratchToNonPawn(t, baseFactor * 0.8f);
                    }
                    else if (t.def.category == ThingCategory.Plant)
                    {
                        ApplyVanillaTornadoScratchToNonPawn(t, baseFactor * 1.7f);
                    }
                    else if (t.def.category == ThingCategory.Item)
                    {
                        ApplyVanillaTornadoScratchToNonPawn(t, baseFactor * 0.68f);
                    }
                }
            }

            ThingsHitThisTick.Clear();
        }

        private static bool CellImmuneToDamage(IntVec3 c, Map map)
        {
            if (c.Roofed(map) && c.GetRoof(map).isThickRoof)
            {
                return true;
            }

            Building edifice = c.GetEdifice(map);
            if (edifice != null && edifice.def.category == ThingCategory.Building &&
                (edifice.def.building.isNaturalRock || (edifice.def == ThingDefOf.Wall && edifice.Faction == null)))
            {
                return true;
            }

            return false;
        }

        private static void TryPullOneStepToward(Pawn pawn, IntVec3 center, Map map)
        {
            IntVec3 cur = pawn.Position;
            if (cur == center)
            {
                return;
            }

            IntVec3 next = NextCellToward(cur, center);
            if (next == cur || !next.InBounds(map))
            {
                return;
            }

            if (!next.Standable(map) || !pawn.CanReach(next, PathEndMode.OnCell, Danger.Deadly))
            {
                return;
            }

            pawn.pather?.StopDead();
            pawn.Position = next;
            pawn.Notify_Teleported();
        }

        private static IntVec3 NextCellToward(IntVec3 from, IntVec3 to)
        {
            int dx = to.x - from.x;
            int dz = to.z - from.z;
            if (dx == 0 && dz == 0)
            {
                return from;
            }

            if (Mathf.Abs(dx) > Mathf.Abs(dz))
            {
                return new IntVec3(from.x + Mathf.Clamp(dx, -1, 1), 0, from.z);
            }

            if (Mathf.Abs(dz) > Mathf.Abs(dx))
            {
                return new IntVec3(from.x, 0, from.z + Mathf.Clamp(dz, -1, 1));
            }

            return new IntVec3(from.x + Mathf.Clamp(dx, -1, 1), 0, from.z + Mathf.Clamp(dz, -1, 1));
        }

        private void ApplyVanillaTornadoScratchToPawn(Pawn pawn, float damageFactor)
        {
            BattleLogEntry_DamageTaken battleLog = new BattleLogEntry_DamageTaken(pawn, RulePackDefOf.DamageEvent_Tornado);
            Find.BattleLog.Add(battleLog);
            if (pawn.RaceProps.baseHealthScale < 1f)
            {
                damageFactor *= pawn.RaceProps.baseHealthScale;
            }

            if (pawn.RaceProps.Animal)
            {
                damageFactor *= 0.75f;
            }

            if (pawn.Downed)
            {
                damageFactor *= 0.2f;
            }

            int dmg = Mathf.Max(GenMath.RoundRandom(30f * damageFactor), 1);
            float angle = TornadoScratchAngleFrom(pawn.Position);
            pawn.TakeDamage(new DamageInfo(DamageDefOf.TornadoScratch, dmg, 0f, angle, this)).AssociateWithLog(battleLog);
        }

        private void ApplyVanillaTornadoScratchToNonPawn(Thing thing, float damageFactor)
        {
            int dmg = Mathf.Max(GenMath.RoundRandom(30f * damageFactor), 1);
            float angle = TornadoScratchAngleFrom(thing.Position);
            thing.TakeDamage(new DamageInfo(DamageDefOf.TornadoScratch, dmg, 0f, angle, this));
        }

        private float TornadoScratchAngleFrom(IntVec3 victimCell)
        {
            Vector3 v = victimCell.ToVector3Shifted();
            Vector2 victim = new Vector2(v.x, v.z);
            Vector2 rp = new Vector2(DrawPos.x, DrawPos.z);
            return 0f - Vector2Utility.AngleTo(victim, rp) + 180f;
        }

        private float FadeInOutFactorDraw()
        {
            float fadeIn = Mathf.Clamp01((float)(Find.TickManager.TicksGame - spawnTick) / 120f);
            float fadeOut = (leftFadeOutTicks < 0) ? 1f : Mathf.Min(leftFadeOutTicks / 120f, 1f);
            return Mathf.Min(fadeIn, fadeOut);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            Vector2 realPosition = Position.ToVector3Shifted().ToVector2();
            float fade = FadeInOutFactorDraw();
            Rand.PushState();
            Rand.Seed = thingIDNumber;
            for (int i = 0; i < 180; i++)
            {
                DrawTornadoPart(realPosition, fade, PartsDistanceFromCenter.RandomInRange, Rand.Range(0f, 360f), Rand.Range(0.9f, 1.1f),
                    Rand.Range(0.52f, 0.88f));
            }

            Rand.PopState();
        }

        private static float AdjustedDistanceFromCenter(float distanceFromCenter)
        {
            float num = Mathf.Min(distanceFromCenter / 8f, 1f);
            num *= num;
            return distanceFromCenter * num;
        }

        private void DrawTornadoPart(Vector2 realPosition, float fadeInOutFactor, float distanceFromCenter, float initialAngle, float speedMultiplier,
            float colorMultiplier)
        {
            int ticksGame = Find.TickManager.TicksGame;
            float num = 1f / distanceFromCenter;
            float num2 = 25f * speedMultiplier * num;
            float num3 = (initialAngle + (float)ticksGame * num2) % 360f;
            Vector2 vector = realPosition.Moved(num3, AdjustedDistanceFromCenter(distanceFromCenter));
            vector.y += distanceFromCenter * 4f;
            vector.y += ZOffsetBias;
            Vector3 vector2 = new Vector3(vector.x, AltitudeLayer.Weather.AltitudeFor() + 0.03658537f * Rand.Range(0f, 1f), vector.y);
            float num4 = distanceFromCenter * 3f;
            float num5 = 1f;
            if (num3 > 270f)
            {
                num5 = GenMath.LerpDouble(270f, 360f, 0f, 1f, num3);
            }
            else if (num3 > 180f)
            {
                num5 = GenMath.LerpDouble(180f, 270f, 1f, 0f, num3);
            }

            float num6 = Mathf.Min(distanceFromCenter / (PartsDistanceFromCenter.max + 2f), 1f);
            float num7 = Mathf.InverseLerp(0.18f, 0.4f, num6);
            Vector3 vector3 = new Vector3(Mathf.Sin((float)ticksGame / 1000f + (float)(thingIDNumber * 10)) * 2f, 0f, 0f);
            Vector3 pos = vector2 + vector3 * num7;
            float a = Mathf.Max(1f - num6, 0f) * num5 * fadeInOutFactor;
            Color value = new Color(colorMultiplier * InkR, colorMultiplier * InkG, colorMultiplier * InkB, a);
            MatBlock.SetColor(ShaderPropertyIDs.Color, value);
            Matrix4x4 matrix = Matrix4x4.TRS(pos, Quaternion.Euler(0f, num3, 0f), new Vector3(num4, 1f, num4));
            Graphics.DrawMesh(MeshPool.plane10, matrix, TornadoMaterial, 0, null, 0, MatBlock);
        }
    }
}
