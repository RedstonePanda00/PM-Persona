using System.Collections.Generic;
using RimWorld;
using RsPandaLibrary;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    // XML options: ground halo, radius override. Beam line color in CompOrbitalBeam comps.
    public class MobileBeamExtension : DefModExtension
    {
        public Color groundHaloColor = new Color(1f, 0.95f, 0.85f, 0.85f);
        public bool drawGroundHalo = true;
        public int beamRadiusOverride; // If > 0, damage/effect radius uses this instead of default 7.
    }

    [StaticConstructorOnStartup]
    public partial class Thing_MobileBeam : Thing_Mobile
    {
        private int tickCounter;
        private const int DamageIntervalTicks = 15;
        private const int HeatDamagePerCell = 200;
        private const int DefaultBeamRadius = 7;
        private int BeamRadius => (BeamExt?.beamRadiusOverride ?? 0) > 0 ? BeamExt.beamRadiusOverride : DefaultBeamRadius;
        private const int ForgetUnseenAfterTicks = 120;

        private static readonly List<Thing> tmpThingsInCell = new List<Thing>();

        private MobileBeamExtension BeamExt => def.GetModExtension<MobileBeamExtension>();

        private readonly Dictionary<int, TrackedPos> lastSeenPosByThingId = new Dictionary<int, TrackedPos>(256);
        private readonly HashSet<int> processedThisTick = new HashSet<int>();
        private static readonly List<int> tmpIdsToRemove = new List<int>(256);

        private struct TrackedPos
        {
            public IntVec3 Pos;
            public int LastSeenTick;
        }

        private static DamageDef BeamDamageDef => DefDatabase<DamageDef>.GetNamedSilentFail("PMP_PurificationBeamDamage") ?? DamageDefOf.Crush;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (!respawningAfterLoad)
            {
                tickCounter = 0;
                lastSeenPosByThingId.Clear();
                visualStartTick = Find.TickManager.TicksGame;
            }
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            base.DrawAt(drawLoc, flip);
            DrawPurification();
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || Destroyed)
                return;
            if (VisualAge < GuidanceTicks) return;
            ActivatePurification();
            TickPurificationVisuals();
            tickCounter++;
            DoMoveDamageForMovingTargets();
            if (tickCounter >= DamageIntervalTicks)
            {
                tickCounter = 0;
                DoBeamDamageAndIgnite();
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            ReleasePurificationMaterials();
            lastSeenPosByThingId.Clear();
            processedThisTick.Clear();
            base.DeSpawn(mode);
        }

        private void DoBeamDamageAndIgnite()
        {
            Map map = Map;
            IntVec3 center = DrawPos.Yto0().ToIntVec3();
            if (!center.InBounds(map))
                return;
            CellRect rect = CellRect.CenteredOn(center, 2 * BeamRadius + 1, 2 * BeamRadius + 1);
            int r2 = BeamRadius * BeamRadius;

            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map))
                    continue;
                int dx = cell.x - center.x;
                int dz = cell.z - center.z;
                if (dx * dx + dz * dz > r2)
                    continue;

                FireUtility.TryStartFireIn(cell, map, Rand.Range(0.1f, 0.5f), this);

                tmpThingsInCell.Clear();
                tmpThingsInCell.AddRange(cell.GetThingList(map));
                for (int i = 0; i < tmpThingsInCell.Count; i++)
                {
                    Thing t = tmpThingsInCell[i];
                    if (t == this || t.Destroyed)
                        continue;
                    if (!IsValidBeamTarget(t))
                        continue;

                    var dinfo = new DamageInfo(BeamDamageDef, HeatDamagePerCell, 0f, -1f, this, null, null);
                    t.TakeDamage(dinfo);
                }
                tmpThingsInCell.Clear();
            }
        }

        private void DoMoveDamageForMovingTargets()
        {
            Map map = Map;
            if (map == null)
                return;

            int now = Find.TickManager.TicksGame;
            IntVec3 center = DrawPos.Yto0().ToIntVec3();
            if (!center.InBounds(map))
                return;

            processedThisTick.Clear();
            CellRect rect = CellRect.CenteredOn(center, 2 * BeamRadius + 1, 2 * BeamRadius + 1);
            int r2 = BeamRadius * BeamRadius;

            foreach (IntVec3 cell in rect)
            {
                if (!cell.InBounds(map))
                    continue;
                int dx = cell.x - center.x;
                int dz = cell.z - center.z;
                if (dx * dx + dz * dz > r2)
                    continue;

                tmpThingsInCell.Clear();
                tmpThingsInCell.AddRange(cell.GetThingList(map));
                for (int i = 0; i < tmpThingsInCell.Count; i++)
                {
                    Thing t = tmpThingsInCell[i];
                    if (t == this || t.Destroyed)
                        continue;
                    if (!IsValidBeamTarget(t))
                        continue;

                    int id = t.thingIDNumber;
                    if (!processedThisTick.Add(id))
                        continue;

                    TrackedPos tracked;
                    if (lastSeenPosByThingId.TryGetValue(id, out tracked))
                    {
                        if (tracked.Pos != t.Position)
                        {
                            var dinfo = new DamageInfo(BeamDamageDef, HeatDamagePerCell, 0f, -1f, this, null, null);
                            t.TakeDamage(dinfo);
                            tracked.Pos = t.Position;
                        }
                        tracked.LastSeenTick = now;
                        lastSeenPosByThingId[id] = tracked;
                    }
                    else
                    {
                        tracked = new TrackedPos { Pos = t.Position, LastSeenTick = now };
                        lastSeenPosByThingId.Add(id, tracked);
                    }
                }
                tmpThingsInCell.Clear();
            }

            if (lastSeenPosByThingId.Count == 0)
                return;
            tmpIdsToRemove.Clear();
            foreach (var kv in lastSeenPosByThingId)
            {
                if (now - kv.Value.LastSeenTick > ForgetUnseenAfterTicks)
                    tmpIdsToRemove.Add(kv.Key);
            }
            for (int i = 0; i < tmpIdsToRemove.Count; i++)
                lastSeenPosByThingId.Remove(tmpIdsToRemove[i]);
            tmpIdsToRemove.Clear();
        }

        // Damage hostile, neutral, and natural (non-colony). Exclude only own faction and colony buildings.
        private bool IsValidBeamTarget(Thing t)
        {
            if (Faction == null)
                return true;
            if (t.def.category == ThingCategory.Pawn)
            {
                if (t is Pawn p)
                    return p.Faction != Faction;
                return false;
            }
            if (t.def.category == ThingCategory.Building && t.def.useHitPoints)
                return t.Faction != Faction;
            return false;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tickCounter, "beamTickCounter", 0);
            Scribe_Values.Look(ref visualStartTick, "purificationVisualStart", 0);
            Scribe_Values.Look(ref beamActivated, "purificationActivated", false);
            Scribe_Values.Look(ref queuedDestination, "purificationQueuedDestination", IntVec3.Invalid);
        }
    }
}
