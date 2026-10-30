using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    public partial class Building_CarrierOrbiter
    {
        private const float BucketSize = 8f;
        private const int NeighborBudget = 48;
        private const float HomeSpeed = .48f;
        private const int ClusterCountMin = 55, ClusterCountMax = 70;
        private const float DroneScaleMin = .4f, DroneScaleMax = .7f;
        private const int WarpOutTicks = 12, WarpTransitTicks = 10, WarpInTicks = 16;
        private List<FlowNode> flowNodes = new List<FlowNode>();
        private DroneState[] flockIndex = Array.Empty<DroneState>();
        private Vector2[] positions = Array.Empty<Vector2>(), velocities = Array.Empty<Vector2>();
        private int[] bucketHeads = Array.Empty<int>(), bucketNext = Array.Empty<int>();
        private int bucketWidth, bucketHeight;
        internal int LastNeighborChecks { get; private set; }
        internal int FlockCount => flockIndex.Length;
        internal Vector2 FlockHead => flockIndex[0].position;
        internal int FlockAge => ageTicks;
        internal int PendingStrikeCount => pendingLegionExplosions.Count;

        private sealed class FlowNode : IExposable
        {
            public Vector2 position, velocity, target;
            public int nextTurn;
            public void ExposeData()
            {
                Scribe_Values.Look(ref position, "position"); Scribe_Values.Look(ref velocity, "velocity");
                Scribe_Values.Look(ref target, "target"); Scribe_Values.Look(ref nextTurn, "nextTurn");
            }
        }
        private sealed class ClusterState : IExposable
        {
            public Vector2 position, velocity, offset, surgeTarget;
            public float phase;
            public int lane, surgeStartTick, surgeEndTick;
            public DroneState[] drones;
            public void ExposeData()
            {
                Scribe_Values.Look(ref position, "position"); Scribe_Values.Look(ref velocity, "velocity");
                Scribe_Values.Look(ref offset, "offset"); Scribe_Values.Look(ref surgeTarget, "surgeTarget");
                Scribe_Values.Look(ref phase, "phase"); Scribe_Values.Look(ref lane, "lane");
                Scribe_Values.Look(ref surgeStartTick, "surgeStartTick"); Scribe_Values.Look(ref surgeEndTick, "surgeEndTick");
                var saved = drones == null ? null : new List<DroneState>(drones);
                Scribe_Collections.Look(ref saved, "drones", LookMode.Deep);
                if (Scribe.mode == LoadSaveMode.LoadingVars) drones = saved?.ToArray() ?? Array.Empty<DroneState>();
            }
        }
        private sealed class DroneState : IExposable
        {
            public Vector2 position, velocity, steering, flightStartXZ, flightTargetXZ, flightDirXZ, flightControlXZ;
            public float scale, phase, slotAngle, slotRadius;
            // 0 flock, 1 attack, 2 salvo, 3 rejoin, 4 return warp, 5 transit, 6 warp in,
            // 7 awaiting first arrival, 8 final departure, 9 departed.
            public byte fadeBatch, variantIndex, flightState;
            public int flightStartTick, flightDurationTicks, bombardmentEndTick, warpStartTick;
            public IntVec3 flightTargetCell;
            public void ExposeData()
            {
                Scribe_Values.Look(ref position, "position"); Scribe_Values.Look(ref velocity, "velocity");
                Scribe_Values.Look(ref steering, "steering"); Scribe_Values.Look(ref scale, "scale");
                Scribe_Values.Look(ref phase, "phase"); Scribe_Values.Look(ref slotAngle, "slotAngle");
                Scribe_Values.Look(ref slotRadius, "slotRadius"); Scribe_Values.Look(ref fadeBatch, "fadeBatch");
                Scribe_Values.Look(ref variantIndex, "variantIndex"); Scribe_Values.Look(ref flightState, "flightState");
                Scribe_Values.Look(ref flightStartTick, "flightStartTick"); Scribe_Values.Look(ref flightDurationTicks, "flightDurationTicks");
                Scribe_Values.Look(ref flightStartXZ, "flightStartXZ"); Scribe_Values.Look(ref flightTargetXZ, "flightTargetXZ");
                Scribe_Values.Look(ref flightDirXZ, "flightDirXZ"); Scribe_Values.Look(ref flightControlXZ, "flightControlXZ");
                Scribe_Values.Look(ref flightTargetCell, "flightTargetCell");
                Scribe_Values.Look(ref bombardmentEndTick, "bombardmentEndTick");
                Scribe_Values.Look(ref warpStartTick, "warpStartTick");
            }
        }
        private sealed class PendingLegionExplosion : IExposable
        {
            public int triggerTick, explosionDamage, directDamage;
            public IntVec3 cell;
            public float radius;
            public void ExposeData()
            {
                Scribe_Values.Look(ref triggerTick, "triggerTick"); Scribe_Values.Look(ref explosionDamage, "explosionDamage");
                Scribe_Values.Look(ref directDamage, "directDamage"); Scribe_Values.Look(ref cell, "cell");
                Scribe_Values.Look(ref radius, "radius");
            }
        }
        private Vector2 KeepInside(Vector2 p, float margin = 2)
        {
            return new Vector2(Mathf.Clamp(p.x, margin, Map.Size.x - margin), Mathf.Clamp(p.y, margin, Map.Size.z - margin));
        }
        private Vector2 WanderPoint()
        {
            return KeepInside(new Vector2(orbitCenterCell.x + .5f + Rand.Range(-Map.Size.x * .35f, Map.Size.x * .35f),
                orbitCenterCell.z + .5f + Rand.Range(-Map.Size.z * .35f, Map.Size.z * .35f)), 12);
        }
        private void InitializeFlock()
        {
            clusters = new List<ClusterState>(); flowNodes = new List<FlowNode>();
            for (int i = 0; i < 8; i++)
                flowNodes.Add(new FlowNode { position = WanderPoint(), target = WanderPoint(), nextTurn = Rand.Range(240, 640) });
            int count = Rand.RangeInclusive(ClusterCountMin, ClusterCountMax);
            for (int c = 0; c < count; c++)
            {
                float phase = Rand.Range(0, Mathf.PI * 2);
                var offset = new Vector2(Mathf.Cos(phase), Mathf.Sin(phase)) * Rand.Range(6f, 20f);
                var cluster = new ClusterState { lane = c % 8, phase = phase, offset = offset };
                cluster.position = KeepInside(flowNodes[cluster.lane].position + offset, 12);
                cluster.velocity = (flowNodes[cluster.lane].target - cluster.position).normalized * .16f;
                int drones = Rand.RangeInclusive(26, 49);
                cluster.drones = new DroneState[drones];
                for (int d = 0; d < drones; d++)
                {
                    var drone = new DroneState { phase = Rand.Range(0, Mathf.PI * 2), slotAngle = Rand.Range(0, Mathf.PI * 2),
                        slotRadius = Mathf.Sqrt(Rand.Value) * Rand.Range(10f, 22f), scale = Rand.Range(DroneScaleMin, DroneScaleMax),
                        fadeBatch = (byte)((c * 31 + d) % FadeBatchCount), variantIndex = (byte)Rand.Range(0, VariantCount), velocity = cluster.velocity };
                    drone.position = KeepInside(HomeTarget(cluster, drone));
                    drone.flightState = 7;
                    drone.warpStartTick = Find.TickManager.TicksGame + drone.fadeBatch * FadeInStaggerTicks + d % 9;
                    cluster.drones[d] = drone;
                }
                clusters.Add(cluster);
            }
            RebuildFlockIndex();
        }
        private void RebuildFlockIndex()
        {
            var all = new List<DroneState>(); foreach (var c in clusters) all.AddRange(c.drones);
            flockIndex = all.ToArray(); positions = new Vector2[all.Count]; velocities = new Vector2[all.Count]; bucketNext = new int[all.Count];
            bucketWidth = Mathf.CeilToInt(Map.Size.x / BucketSize) + 1; bucketHeight = Mathf.CeilToInt(Map.Size.z / BucketSize) + 1;
            bucketHeads = new int[bucketWidth * bucketHeight];
        }
        private Vector2 HomeTarget(ClusterState c, DroneState d)
        {
            // Delay the spacing pulse by member distance.
            float t = (ageTicks - d.slotRadius * 3) * .009f + c.phase;
            float spread = .85f + .28f * Mathf.Sin(t);
            float angle = d.slotAngle + .3f * Mathf.Sin(ageTicks * .004f + d.phase);
            Vector2 forward = c.velocity.sqrMagnitude > .001f ? c.velocity.normalized : Vector2.right;
            var side = new Vector2(-forward.y, forward.x);
            return KeepInside(c.position + (forward * (Mathf.Cos(angle) * 1.35f) + side * (Mathf.Sin(angle) * .8f)) * (d.slotRadius * spread));
        }
        private void TickFlock(int now)
        {
            foreach (var n in flowNodes)
            {
                if (ageTicks >= n.nextTurn || Vector2.Distance(n.position, n.target) < 8)
                { n.target = WanderPoint(); n.nextTurn = ageTicks + Rand.Range(320, 700); }
                n.velocity = Vector2.MoveTowards(n.velocity, (n.target - n.position).normalized * .18f, .003f);
                n.position = KeepInside(n.position + n.velocity, 12);
            }
            foreach (var c in clusters)
            {
                // Different subgroups gradually branch into the neighboring stream and rejoin.
                float branch = Mathf.Pow((1 + Mathf.Sin(ageTicks * .0027f + c.phase)) * .5f, 3) * .45f;
                Vector2 goal = Vector2.Lerp(flowNodes[c.lane].position, flowNodes[(c.lane + 1) % flowNodes.Count].position, branch) + c.offset;
                if (now >= c.surgeStartTick && now < c.surgeEndTick)
                {
                    float response = Mathf.Sin((now - c.surgeStartTick) / (float)(c.surgeEndTick - c.surgeStartTick) * Mathf.PI);
                    goal = Vector2.Lerp(goal, c.surgeTarget, response * .25f);
                }
                Vector2 desired = Vector2.ClampMagnitude((KeepInside(goal, 12) - c.position) * .025f, .24f);
                c.velocity = Vector2.MoveTowards(c.velocity, desired, .006f);
                c.position = KeepInside(c.position + c.velocity, 8);
            }
            // Snapshot positions before steering.
            Array.Fill(bucketHeads, -1);
            for (int i = 0; i < flockIndex.Length; i++)
            {
                var d = flockIndex[i]; positions[i] = d.position; velocities[i] = d.velocity;
                if (d.flightState != 0) continue;
                int bucket = Bucket(d.position); bucketNext[i] = bucketHeads[bucket]; bucketHeads[bucket] = i;
            }
            LastNeighborChecks = 0;
            int index = 0;
            foreach (var c in clusters)
            {
                foreach (var d in c.drones)
                {
                    TickFleetLifecycle(c, d, now);
                    if (d.flightState == 1) TickAttack(d, now);
                    else if (d.flightState == 3) TickReturn(c, d);
                    else if (d.flightState != 0) TickDroneWarp(c, d, now);
                    else
                    {
                        if ((index + ageTicks) % 3 == 0)
                        {
                            Vector2 desired = c.velocity + (HomeTarget(c, d) - d.position) * .026f;
                            Neighbors(index, out var alignment, out var separation, out var cohesion);
                            desired += alignment * .32f + separation * .13f + cohesion * .004f;
                            desired += new Vector2(Mathf.Sin(ageTicks * .027f + d.phase), Mathf.Cos(ageTicks * .019f + d.phase)) * .025f;
                            d.steering = Vector2.ClampMagnitude((Vector2.ClampMagnitude(desired, HomeSpeed) - d.velocity) * .14f, .025f);
                        }
                        d.velocity = Vector2.ClampMagnitude(d.velocity + d.steering, HomeSpeed);
                        d.position = KeepInside(d.position + d.velocity);
                    }
                    index++;
                }
            }
        }
        private int Bucket(Vector2 p) => Mathf.Clamp((int)(p.x / BucketSize), 0, bucketWidth - 1)
            + Mathf.Clamp((int)(p.y / BucketSize), 0, bucketHeight - 1) * bucketWidth;
        private void Neighbors(int index, out Vector2 alignment, out Vector2 separation, out Vector2 cohesion)
        {
            alignment = separation = cohesion = Vector2.zero;
            int bucket = Bucket(positions[index]), bx = bucket % bucketWidth, by = bucket / bucketWidth, checkedCount = 0, neighbors = 0;
            // Search the member's bucket first with a bounded neighbor count.
            for (int ring = 0; ring < 2 && checkedCount < NeighborBudget; ring++)
                for (int y = -ring; y <= ring && checkedCount < NeighborBudget; y++)
                    for (int x = -ring; x <= ring && checkedCount < NeighborBudget; x++)
                    {
                        if (ring == 1 && x == 0 && y == 0 || bx + x < 0 || by + y < 0 || bx + x >= bucketWidth || by + y >= bucketHeight) continue;
                        for (int j = bucketHeads[bx + x + (by + y) * bucketWidth]; j >= 0 && checkedCount < NeighborBudget; j = bucketNext[j])
                        {
                            checkedCount++; LastNeighborChecks++;
                            if (j == index) continue;
                            Vector2 delta = positions[index] - positions[j]; float distance2 = delta.sqrMagnitude;
                            if (distance2 > 49) continue;
                            neighbors++; alignment += velocities[j]; cohesion -= delta;
                            if (distance2 < 6.25f)
                                separation += distance2 < .0001f ? new Vector2(index < j ? -1 : 1, 0) : delta / Mathf.Max(.25f, distance2);
                        }
                    }
            if (neighbors > 0) { alignment = alignment / neighbors - velocities[index]; cohesion /= neighbors; }
            separation = Vector2.ClampMagnitude(separation, 2);
        }
        private void TickAttack(DroneState d, int now)
        {
            float progress = Mathf.Clamp01((now - d.flightStartTick) / (float)d.flightDurationTicks);
            float t = OutboundEaseT(progress), u = 1 - t;
            Vector2 endControl = KeepInside(d.flightTargetXZ - d.flightDirXZ * Mathf.Min(12, Vector2.Distance(d.flightStartXZ, d.flightTargetXZ) * .2f));
            Vector2 next = u * u * u * d.flightStartXZ + 3 * u * u * t * d.flightControlXZ
                + 3 * u * t * t * endControl + t * t * t * d.flightTargetXZ;
            d.velocity = next - d.position; d.position = next;
            if (progress >= 1)
            {
                d.flightState = 2; d.velocity = Vector2.ClampMagnitude(d.velocity, 1.5f);
                if (now >= d.bombardmentEndTick) BeginWarpOut(d, now);
            }
        }
        private void BeginWarpOut(DroneState d, int now)
        {
            d.flightState = (byte)(ageTicks >= LifeTimeTicks ? 8 : 4); d.warpStartTick = now;
            d.flightDirXZ = d.velocity.sqrMagnitude > .001f ? d.velocity.normalized : d.flightDirXZ;
        }
        private void TickDroneWarp(ClusterState c, DroneState d, int now)
        {
            if (d.flightState == 2)
            {
                d.velocity = Vector2.MoveTowards(d.velocity, d.flightDirXZ * .18f, .08f);
                d.position = KeepInside(d.position + d.velocity);
                if (now >= d.bombardmentEndTick) BeginWarpOut(d, now);
                return;
            }
            int age = now - d.warpStartTick;
            if (d.flightState == 4 || d.flightState == 8)
            {
                d.velocity = d.flightDirXZ * Mathf.Lerp(.3f, 2, Mathf.Clamp01(age / (float)WarpOutTicks));
                d.position = KeepInside(d.position + d.velocity);
                if (age >= WarpOutTicks) { d.flightState = (byte)(d.flightState == 8 ? 9 : 5); d.warpStartTick = now; }
            }
            else if (d.flightState == 5 && age >= WarpTransitTicks)
            {
                // Resolve the current formation slot.
                d.flightDirXZ = c.velocity.sqrMagnitude > .001f ? c.velocity.normalized : Vector2.right;
                d.position = KeepInside(HomeTarget(c, d) - d.flightDirXZ * 4);
                d.velocity = c.velocity + d.flightDirXZ * .25f;
                d.flightState = 6; d.warpStartTick = now;
            }
            else if (d.flightState == 6)
            {
                d.velocity = Vector2.MoveTowards(d.velocity, c.velocity, .02f);
                d.position = KeepInside(d.position + d.velocity);
                if (age >= WarpInTicks) { d.flightState = 3; d.steering = Vector2.zero; }
            }
        }
        private void TickReturn(ClusterState c, DroneState d)
        {
            Vector2 delta = HomeTarget(c, d) - d.position;
            float distance = delta.magnitude;
            Vector2 desired = c.velocity + (distance > .001f ? delta / distance : Vector2.zero) * Mathf.Min(1.25f, distance * .065f);
            d.velocity = Vector2.MoveTowards(d.velocity, desired, .06f);
            d.position = KeepInside(d.position + d.velocity);
            if (distance < 5 && d.velocity.magnitude <= HomeSpeed)
            { d.flightState = 0; d.steering = Vector2.zero; }
        }

        internal string AuditFlockMotion()
        {
            // Complete initial warp before measuring flock motion.
            int warmup = Verse.Find.TickManager.TicksGame;
            for (int t = 1; t <= 240; t++) { ageTicks++; TickFlock(warmup + t); }
            int baseline = Find.TickManager.TicksGame, count = FlockCount;
            float scale = flockIndex[0].scale, maximumStep = 0;
            long elapsed;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                for (int t = 0; t < 360; t++)
                {
                    ageTicks++; TickFlock(baseline + t);
                    if (LastNeighborChecks > ((count + 2) / 3) * NeighborBudget) throw new Exception("Neighbor work unbounded");
                    for (int i = 0; i < count; i++)
                    {
                        var d = flockIndex[i];
                        if (float.IsNaN(d.position.x) || float.IsNaN(d.position.y) || d.position != KeepInside(d.position)) throw new Exception("Invalid flock position");
                        maximumStep = Mathf.Max(maximumStep, Vector2.Distance(d.position, positions[i]));
                    }
                }
                elapsed = timer.ElapsedMilliseconds;
                if (maximumStep > HomeSpeed + .001f || flockIndex[0].scale != scale) throw new Exception("Home jump or periodic resizing");
                var drone = clusters[0].drones[0];
                Vector2 before = drone.position;
                var target = Map.Center;
                const int salvoTail = 38; // AA's twenty impacts, two ticks apart.
                DispatchOneDroneToTarget(0, target, out int travel, out var origin, salvoTail);
                if (origin != before || drone.flightStartXZ != before) throw new Exception("Dispatch discontinuity");
                int expected = Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(origin, new Vector2(target.x + .5f, target.z + .5f)) / OutboundSpeedCellsPerTick), 15, 280);
                if (travel != expected) throw new Exception("Attack travel time changed");
                bool arrived = false, returned = false, warpOut = false, hidden = false, warpIn = false;
                for (int t = 1; t <= travel + 800; t++)
                {
                    byte previousState = drone.flightState;
                    ageTicks++; before = drone.position; TickFlock(baseline + t);
                    bool hiddenRelocation = previousState == 5 && drone.flightState == 6;
                    if (Vector2.Distance(before, drone.position) > 7 && !hiddenRelocation) throw new Exception("Visible flight teleport");
                    if (hiddenRelocation && Vector2.Distance(drone.position, KeepInside(HomeTarget(clusters[0], drone) - drone.flightDirXZ * 4)) > .001f)
                        throw new Exception("Warp returned to a stale formation position");
                    if (t < travel + salvoTail && drone.flightState >= 4) throw new Exception("Warp started before last bomb");
                    warpOut |= drone.flightState == 4; hidden |= drone.flightState == 5; warpIn |= drone.flightState == 6;
                    if (t == travel)
                    {
                        arrived = drone.flightState == 2 && Vector2.Distance(drone.position, drone.flightTargetXZ) < .001f;
                        if (!arrived) throw new Exception("Missed scheduled impact tick");
                    }
                    if (arrived && drone.flightState == 0) { returned = true; break; }
                }
                if (!returned || !warpOut || !hidden || !warpIn) throw new Exception("Incomplete warp out/transit/in/rejoin cycle");
                if (FlockCount != count) throw new Exception("Drone count changed");
                return "PASS " + count + " members; bounded 48-candidate neighbor query staggered over 3 ticks; home maximum step "
                    + maximumStep + "; fixed individual sizes; original attack timing; warp after final bomb, invisible relocation, current formation slot, warp in and rejoin; 360 flock ticks " + elapsed + " ms (isolated test, not an FPS guarantee).";
            }
            finally { Find.TickManager.DebugSetTicksGame(baseline); }
        }
    }
}
