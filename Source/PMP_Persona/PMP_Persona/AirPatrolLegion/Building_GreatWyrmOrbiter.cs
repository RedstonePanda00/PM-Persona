using System.Collections.Generic;
using PMP_Persona;
using RimWorld;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;
using Verse.Sound;

namespace PMP_Persona.AirPatrolLegion
{
    // 1x1 anchor; 5 Great Wyrm sprites sweep the map twice (L→R then R→L) and eliminate hostile flying pawns + XML-listed defs.
    [StaticConstructorOnStartup]
    public class Building_GreatWyrmOrbiter : Building
    {
        static Building_GreatWyrmOrbiter()
        {
            ResolveWingTrailRibbonMaterial();
            ResolveMissileMaterial();
        }

        private const string PlaneDrawTexPath = "Things/CAS/GreatWyrm";
        private const string MissileDrawTexPath = "Things/Weapon/transparent";
        private const int GreatWyrmCount = 5;
        private const int KillIntervalTicks = 12;
        private const int WingTrailRingHardMax = 64;
        private const int MissileTrailRingHardMax = 32;
        private const int MaxConcurrentMissiles = 96;
        private static readonly Dictionary<string, Material> MatByTexPath = new Dictionary<string, Material>();
        private static Material cachedWingTrailRibbonMat;
        private static Material cachedMissileMat;
        private static readonly MaterialPropertyBlock WingTrailMpb = new MaterialPropertyBlock();
        private static readonly MaterialPropertyBlock OrbFxMpb = new MaterialPropertyBlock();
        private static readonly MaterialPropertyBlock TrailFxMpb = new MaterialPropertyBlock();
        private static readonly int CoreSizeId = Shader.PropertyToID("_CoreSize");
        private static readonly int GlowFalloffId = Shader.PropertyToID("_GlowFalloff");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly Color WingOrbColor = new Color(0.18f, 0.95f, 1f, 1f);
        private static readonly Color WingTrailColor = new Color(0.12f, 0.85f, 1f, 1f);
        private static readonly Color MissileOrbColorFx = new Color(1f, 0.5f, 0.1f, 1f);
        private static readonly Color MissileTrailColorFx = new Color(0.12f, 0.85f, 1f, 1f);

        private Vector3[][] wingTrailLeftRing;
        private Vector3[][] wingTrailRightRing;
        private int[] wingTrailWriteIndex;
        private int[] wingTrailFillCount;
        private int wingTrailCap;
        private int wingTrailSampleAccumulator;
        private Mesh[] wingTrailLeftMeshes;
        private Mesh[] wingTrailRightMeshes;
        private readonly List<AirPatrolMissile> activeMissiles = new List<AirPatrolMissile>();
        private readonly HashSet<int> lockedTargetIds = new HashSet<int>();
        private int nextShooterIndex;
        private int missileLaunchSerial;

        // Fleck-based missile trail.
        private const int MissileTrailFleckBudgetPerTick = 220;
        private const int MissileTrailFleckEmitEveryPoints = 2;
        private const float MissileTrailSmokeSizeMin = 1.0f;
        // Electric-cyan trail color.
        private static readonly Color MissileTrailColor = new Color(0.15f, 1f, 1f);
        private const float MissileImpactSmokeSizeMin = 1.0f;

        // Trail visibility parameters.
        private const float MissileTrailSolidTimeOverride = 1.0f;
        private const bool MissileTrailDebugForceEmit = true;

        private class AirPatrolMissile
        {
            public Thing target;
            public int targetId;
            public Vector3 pos;
            public Vector3 dir;
            public int shooterIndex;
            public bool launchedFromLeftWing;
            public Vector3[] trailRing;
            public int trailWrite;
            public int trailFill;
            public Mesh trailMesh;
        }

        private enum AirPatrolPass
        {
            LeftToRight,
            RightToLeft,
            Done,
            // Serialized values 0-2 are reserved for existing states.
            PausingBetweenPasses
        }

        private AirPatrolPass pass = AirPatrolPass.LeftToRight;
        private int betweenPassPauseTicks;
        private float leaderX;
        private float leaderZ;
        private int lastKillTick = -999999;
        private List<ThingDef> extraTargetDefs = new List<ThingDef>();

        private DefModExtension_GreatWyrmOrbiter Ext =>
            def.GetModExtension<DefModExtension_GreatWyrmOrbiter>() ?? fallbackExt;

        private static readonly DefModExtension_GreatWyrmOrbiter fallbackExt = new DefModExtension_GreatWyrmOrbiter();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            RebuildExtraTargetDefs();
            EnsureWingTrailRingAllocated();
        }

        public void SetupRun(Map map)
        {
            if (map == null)
                return;
            CellRect rect = CellRect.WholeMap(map);
            leaderZ = (rect.minZ + rect.maxZ) * 0.5f;
            pass = AirPatrolPass.LeftToRight;
            betweenPassPauseTicks = 0;
            leaderX = -Ext.mapEdgeMarginCells;
            lastKillTick = Find.TickManager.TicksGame;
            nextShooterIndex = 0;
            missileLaunchSerial = 0;
            ClearActiveMissiles();
            lockedTargetIds.Clear();
            EnsureWingTrailRingAllocated();
            ResetWingTrailBuffers();
        }

        private void RebuildExtraTargetDefs()
        {
            extraTargetDefs.Clear();
            HashSet<ThingDef> seen = new HashSet<ThingDef>();
            foreach (AirPatrolExtraTargetsDef extDef in DefDatabase<AirPatrolExtraTargetsDef>.AllDefsListForReading)
            {
                if (extDef.extraHostileTargetDefNames == null)
                    continue;
                for (int i = 0; i < extDef.extraHostileTargetDefNames.Count; i++)
                {
                    string name = extDef.extraHostileTargetDefNames[i];
                    if (string.IsNullOrEmpty(name))
                        continue;
                    ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (td != null && seen.Add(td))
                        extraTargetDefs.Add(td);
                }
            }

            if (PMP_PersonaMod.Instance?.Settings != null)
            {
                foreach (string name in PMP_PersonaMod.Instance.Settings.EnumerateSelectedExtraTargetDefNames())
                {
                    if (string.IsNullOrEmpty(name))
                        continue;
                    ThingDef td = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (td != null && seen.Add(td))
                        extraTargetDefs.Add(td);
                }
            }
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || Destroyed || Map == null)
                return;
            UpdateMissiles();
            if (pass == AirPatrolPass.Done)
                return;

            float speed = Ext.speedCellsPerTick;
            float margin = Ext.mapEdgeMarginCells;
            float fwd = Ext.formationForwardSpacing;
            float halfWidth = Ext.planeDrawScale * 0.5f;
            int w = Map.Size.x;

            if (pass == AirPatrolPass.PausingBetweenPasses)
            {
                betweenPassPauseTicks--;
                if (betweenPassPauseTicks > 0)
                {
                    int nowPause = Find.TickManager.TicksGame;
                    if (nowPause >= lastKillTick + KillIntervalTicks)
                    {
                        lastKillTick = nowPause;
                        ProcessEliminations();
                    }

                    RecordWingTrailSamples();
                    return;
                }

                pass = AirPatrolPass.RightToLeft;
                // Place the right-to-left formation fully off-map.
                leaderX = w + margin + 2f * fwd + halfWidth;
            }

            if (pass == AirPatrolPass.LeftToRight)
            {
                leaderX += speed;
                // Wait for the left-to-right formation's tail to exit.
                if (leaderX > w + margin + 2f * fwd + halfWidth)
                {
                    leaderX = w + margin + 2f * fwd + halfWidth;
                    pass = AirPatrolPass.PausingBetweenPasses;
                    betweenPassPauseTicks = Rand.RangeInclusive(60, 180);
                }
            }
            else if (pass == AirPatrolPass.RightToLeft)
            {
                leaderX -= speed;
                // Wait for the right-to-left formation's tail to exit.
                if (leaderX < -margin - 2f * fwd - halfWidth)
                {
                    pass = AirPatrolPass.Done;
                    DisposeAllFxMeshes();
                    Destroy(DestroyMode.Vanish);
                    return;
                }
            }

            int now = Find.TickManager.TicksGame;
            if (now >= lastKillTick + KillIntervalTicks)
            {
                lastKillTick = now;
                ProcessEliminations();
            }

            RecordWingTrailSamples();
        }

        private void ProcessEliminations()
        {
            RebuildExtraTargetDefs();
            Map map = Map;
            if (map == null)
                return;
            Faction player = Faction.OfPlayer;
            if (player == null)
                return;
            List<Thing> candidates = SimplePool<List<Thing>>.Get();
            candidates.Clear();
            try
            {
                // Hostile pawns using vanilla flight tracker (Pawn.Flying).
                IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < pawns.Count; i++)
                {
                    Pawn p = pawns[i];
                    if (p == null || p.Destroyed || !p.Spawned || p.Dead)
                        continue;
                    if (!IsHostileToPlayer(p) || !p.Flying)
                        continue;
                    candidates.Add(p);
                }

                // XML extra defs (e.g. PawnFlyer, projectiles).
                for (int d = 0; d < extraTargetDefs.Count; d++)
                {
                    ThingDef tdef = extraTargetDefs[d];
                    if (tdef == null)
                        continue;
                    List<Thing> list = map.listerThings.ThingsOfDef(tdef);
                    for (int j = 0; j < list.Count; j++)
                    {
                        Thing t = list[j];
                        if (t == null || t.Destroyed || !t.Spawned)
                            continue;
                        if (!IsHostileToPlayer(t))
                            continue;
                        candidates.Add(t);
                    }
                }

                LaunchMissiles(candidates);
            }
            finally
            {
                candidates.Clear();
                SimplePool<List<Thing>>.Return(candidates);
            }
        }

        private static bool IsHostileToPlayer(Thing t)
        {
            if (t == null)
                return false;
            Faction player = Faction.OfPlayer;
            if (player == null)
                return false;
            if (t.Faction != null)
                return t.Faction.HostileTo(player);
            return false;
        }

        private static void TryEliminate(Thing t)
        {
            if (t == null || t.Destroyed)
                return;
            if (t is Pawn p)
            {
                if (!p.Dead)
                    p.Kill(null);
                return;
            }

            t.Destroy(DestroyMode.Vanish);
        }

        private static void GetWingTipOffsets(float moveSign, DefModExtension_GreatWyrmOrbiter ext, out Vector3 span, out float halfSpan, out float aftOffset)
        {
            Vector3 forward = new Vector3(moveSign, 0f, 0f);
            span = Vector3.Cross(Vector3.up, forward).normalized;
            halfSpan = ext.planeDrawScale * ext.wingTrailSpanFactor;
            aftOffset = ext.planeDrawScale * ext.planeDrawDepthFactor * ext.wingTrailAftFactor;
        }

        private static Vector3 GetWingTipWorldPos(Vector3 center, bool leftWing, Vector3 span, float halfSpan, float aftOffset, Vector3 forward, float yOff)
        {
            Vector3 tip = center + (leftWing ? span : -span) * halfSpan - forward * aftOffset;
            tip.y = center.y - yOff;
            return tip;
        }

        private void LaunchMissiles(List<Thing> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return;
            if (activeMissiles.Count >= MaxConcurrentMissiles)
                return;

            Vector3[] centers = GetGreatWyrmWorldPositions();
            float moveSign = pass == AirPatrolPass.LeftToRight || pass == AirPatrolPass.PausingBetweenPasses ? 1f : -1f;
            Vector3 forward = new Vector3(moveSign, 0f, 0f);
            GetWingTipOffsets(moveSign, Ext, out Vector3 span, out float halfSpan, out float aftOffset);

            for (int i = 0; i < candidates.Count; i++)
            {
                if (activeMissiles.Count >= MaxConcurrentMissiles)
                    break;
                Thing target = candidates[i];
                if (target == null || target.Destroyed || !target.Spawned)
                    continue;
                int targetId = target.thingIDNumber;
                if (lockedTargetIds.Contains(targetId))
                    continue;

                int shooter = nextShooterIndex % GreatWyrmCount;
                nextShooterIndex = (nextShooterIndex + 1) % GreatWyrmCount;
                bool leftWing = (missileLaunchSerial++ & 1) == 0;
                Vector3 start = GetWingTipWorldPos(centers[shooter], leftWing, span, halfSpan, aftOffset, forward, Ext.wingTrailYOffset);

                Vector3 targetStartPos = target.TrueCenter();
                targetStartPos.y = 0f;
                Vector3 desiredDir = targetStartPos - start;
                desiredDir.y = 0f;
                if (desiredDir.sqrMagnitude < 0.0001f)
                    desiredDir = forward;
                Vector3 missileDir = Vector3.Lerp(forward, desiredDir.normalized, Ext.missileInitialForwardBias);
                if (missileDir.sqrMagnitude < 0.0001f)
                    missileDir = forward;
                missileDir.Normalize();

                AirPatrolMissile missile = new AirPatrolMissile
                {
                    target = target,
                    targetId = targetId,
                    pos = start,
                    dir = missileDir,
                    shooterIndex = shooter,
                    launchedFromLeftWing = leftWing
                };
                InitMissileTrail(missile, start);
                activeMissiles.Add(missile);
                lockedTargetIds.Add(targetId);

                // Smoke fallback when orbandtail materials are unavailable.
                if (MissileTrailDebugForceEmit && !AirPatrolOrbAndTailFx.TryGetMaterials(out _, out _))
                {
                    // Emit smoke puffs around the wing tip.
                    int n = 4;
                    float wingSmokeScale = Mathf.Max(1.6f, Ext.planeDrawScale * 0.2f);
                    for (int k = 0; k < n; k++)
                    {
                        Vector3 offset = new Vector3(Rand.Range(-0.2f, 0.2f), 0f, Rand.Range(-0.2f, 0.2f));
                        Vector3 loc = start + offset;
                        FleckCreationData dataStatic = FleckMaker.GetDataStatic(loc, Map, FleckDefOf.Smoke, wingSmokeScale);
                        dataStatic.rotationRate = Rand.Range(-20f, 20f);
                        dataStatic.velocity = Vector3.zero;
                        dataStatic.velocitySpeed = 0f;
                        dataStatic.instanceColor = MissileTrailColor;
                        dataStatic.solidTimeOverride = MissileTrailSolidTimeOverride;
                        Map.flecks.CreateFleck(dataStatic);
                    }
                }
            }
        }

        private void InitMissileTrail(AirPatrolMissile m, Vector3 start)
        {
            int cap = Mathf.Clamp(Ext.wingTrailRingCapacity, 8, MissileTrailRingHardMax);
            m.trailRing = new Vector3[cap];
            m.trailWrite = 0;
            m.trailFill = 1;
            m.trailRing[0] = start;
            m.trailMesh = CreateDynamicTrailMesh("PMP_AirPatrol_MissileTrail_" + m.targetId);
        }

        private static Mesh CreateDynamicTrailMesh(string meshName)
        {
            Mesh mesh = new Mesh
            {
                name = meshName
            };
            mesh.MarkDynamic();
            return mesh;
        }

        private static void DisposeMesh(ref Mesh mesh)
        {
            if (mesh == null)
                return;
            Object.Destroy(mesh);
            mesh = null;
        }

        private void ClearActiveMissiles()
        {
            for (int i = 0; i < activeMissiles.Count; i++)
            {
                AirPatrolMissile missile = activeMissiles[i];
                if (missile != null)
                    DisposeMesh(ref missile.trailMesh);
            }

            activeMissiles.Clear();
        }

        private void DisposeWingTrailMeshes()
        {
            if (wingTrailLeftMeshes != null)
            {
                for (int i = 0; i < wingTrailLeftMeshes.Length; i++)
                    DisposeMesh(ref wingTrailLeftMeshes[i]);
                wingTrailLeftMeshes = null;
            }

            if (wingTrailRightMeshes != null)
            {
                for (int i = 0; i < wingTrailRightMeshes.Length; i++)
                    DisposeMesh(ref wingTrailRightMeshes[i]);
                wingTrailRightMeshes = null;
            }
        }

        private void DisposeAllFxMeshes()
        {
            ClearActiveMissiles();
            DisposeWingTrailMeshes();
        }

        private void UpdateMissiles()
        {
            if (activeMissiles.Count == 0)
                return;
            float speed = Mathf.Max(0.2f, Ext.speedCellsPerTick * 1.9f);
            bool useLegacyFleckTrail = !AirPatrolOrbAndTailFx.TryGetMaterials(out _, out _);
            int flecksEmitted = 0;
            for (int i = activeMissiles.Count - 1; i >= 0; i--)
            {
                AirPatrolMissile m = activeMissiles[i];
                Thing t = m.target;
                if (t == null || t.Destroyed || !t.Spawned)
                {
                    DisposeMesh(ref m.trailMesh);
                    lockedTargetIds.Remove(m.targetId);
                    activeMissiles.RemoveAt(i);
                    continue;
                }

                Vector3 targetPos = t.TrueCenter();
                targetPos.y = AltitudeLayer.MoteOverhead.AltitudeFor() - Ext.wingTrailYOffset;
                Vector3 delta = targetPos - m.pos;
                delta.y = 0f;
                float dist = delta.magnitude;
                if (dist <= speed || dist < 0.15f)
                {
                    if (useLegacyFleckTrail)
                        EmitMissileImpactFleck(targetPos);
                    TryEliminate(t);
                    DisposeMesh(ref m.trailMesh);
                    lockedTargetIds.Remove(m.targetId);
                    activeMissiles.RemoveAt(i);
                    continue;
                }
                Vector3 desiredDir = delta / dist;
                if (m.dir.sqrMagnitude < 0.0001f)
                    m.dir = desiredDir;

                float turn = Mathf.Clamp01(Ext.missileTurnLerp);
                m.dir = Vector3.Lerp(m.dir, desiredDir, turn);
                if (m.dir.sqrMagnitude < 0.0001f)
                    m.dir = desiredDir;
                m.dir.Normalize();

                Vector3 from = m.pos;
                Vector3 to = m.pos + m.dir * speed;

                float maxSeg = Mathf.Max(0.01f, Ext.missileTrailMaxSegmentCells);
                float moved = (to - from).magnitude;
                int substeps = Mathf.Clamp(Mathf.CeilToInt(moved / maxSeg), 1, Mathf.Max(1, Ext.missileTrailMaxSubsteps));

                for (int s = 1; s <= substeps; s++)
                {
                    float lerpT = s / (float)substeps;
                    Vector3 p = Vector3.Lerp(from, to, lerpT);
                    p.y = targetPos.y;
                    m.pos = p;
                    PushMissileTrailPoint(m, m.pos);
                    if (useLegacyFleckTrail)
                        TryEmitMissileTrailFleck(m.pos, ref flecksEmitted);
                }

                // Resolve impacts within the steering hit threshold.
                Vector3 deltaEnd = targetPos - m.pos;
                deltaEnd.y = 0f;
                if (deltaEnd.magnitude <= speed || deltaEnd.magnitude < 0.15f)
                {
                    if (useLegacyFleckTrail)
                        EmitMissileImpactFleck(targetPos);
                    TryEliminate(t);
                    DisposeMesh(ref m.trailMesh);
                    lockedTargetIds.Remove(m.targetId);
                    activeMissiles.RemoveAt(i);
                    continue;
                }
            }
        }

        private static void PushMissileTrailPoint(AirPatrolMissile m, Vector3 p)
        {
            int cap = m.trailRing.Length;
            m.trailRing[m.trailWrite] = p;
            m.trailWrite = (m.trailWrite + 1) % cap;
            if (m.trailFill < cap)
                m.trailFill++;
        }

        private void TryEmitMissileTrailFleck(Vector3 p, ref int flecksEmitted)
        {
            if (Map == null || Destroyed)
                return;
            if (flecksEmitted >= MissileTrailFleckBudgetPerTick)
                return;

            // Limit missile trail emission density.
            if (!MissileTrailDebugForceEmit && (flecksEmitted & 1) == 1)
                return;

            float size = Mathf.Max(MissileTrailSmokeSizeMin, Ext.planeDrawScale * 0.08f);
            // Emit smoke near the missile sample point.
            float scale = Rand.Range(0.9f, 1.3f) * size;
            FleckCreationData dataStatic = FleckMaker.GetDataStatic(p, Map, FleckDefOf.Smoke, scale);
            dataStatic.rotationRate = Rand.Range(-20f, 20f);
            dataStatic.velocity = Vector3.zero;
            dataStatic.velocitySpeed = 0f;
            dataStatic.instanceColor = MissileTrailColor;
            dataStatic.solidTimeOverride = MissileTrailSolidTimeOverride;
            Map.flecks.CreateFleck(dataStatic);
            flecksEmitted++;
        }

        private void EmitMissileImpactFleck(Vector3 p)
        {
            if (Map == null || Destroyed)
                return;
            float size = Mathf.Max(MissileImpactSmokeSizeMin, Ext.planeDrawScale * 0.08f);
            float scale = Rand.Range(0.8f, 1.2f) * size;
            FleckCreationData dataStatic = FleckMaker.GetDataStatic(p, Map, FleckDefOf.Smoke, scale);
            dataStatic.rotationRate = Rand.Range(-20f, 20f);
            dataStatic.velocity = Vector3.zero;
            dataStatic.velocitySpeed = 0f;
            dataStatic.instanceColor = MissileTrailColor;
            dataStatic.solidTimeOverride = 0.45f;
            Map.flecks.CreateFleck(dataStatic);
        }

        private Vector3[] GetGreatWyrmWorldPositions()
        {
            float alt = AltitudeLayer.MoteOverhead.AltitudeFor();
            float fwd = Ext.formationForwardSpacing;
            float lat = Ext.formationLateralSpacing;
            Vector3[] result = new Vector3[GreatWyrmCount];
            if (pass == AirPatrolPass.LeftToRight || pass == AirPatrolPass.PausingBetweenPasses)
            {
                // Tip of ">" leads to the right (largest X).
                result[0] = new Vector3(leaderX, alt, leaderZ);
                result[1] = new Vector3(leaderX - fwd, alt, leaderZ + lat);
                result[2] = new Vector3(leaderX - 2f * fwd, alt, leaderZ + 2f * lat);
                result[3] = new Vector3(leaderX - fwd, alt, leaderZ - lat);
                result[4] = new Vector3(leaderX - 2f * fwd, alt, leaderZ - 2f * lat);
            }
            else
            {
                // Tip of "<" leads to the left (smallest X).
                result[0] = new Vector3(leaderX, alt, leaderZ);
                result[1] = new Vector3(leaderX + fwd, alt, leaderZ + lat);
                result[2] = new Vector3(leaderX + 2f * fwd, alt, leaderZ + 2f * lat);
                result[3] = new Vector3(leaderX + fwd, alt, leaderZ - lat);
                result[4] = new Vector3(leaderX + 2f * fwd, alt, leaderZ - 2f * lat);
            }

            return result;
        }

        private void EnsureWingTrailRingAllocated()
        {
            int want = Mathf.Clamp(Ext.wingTrailRingCapacity, 8, WingTrailRingHardMax);
            if (wingTrailLeftRing != null && wingTrailCap == want)
            {
                EnsureWingTrailMeshAllocated();
                return;
            }
            wingTrailCap = want;
            wingTrailLeftRing = new Vector3[GreatWyrmCount][];
            wingTrailRightRing = new Vector3[GreatWyrmCount][];
            for (int i = 0; i < GreatWyrmCount; i++)
            {
                wingTrailLeftRing[i] = new Vector3[wingTrailCap];
                wingTrailRightRing[i] = new Vector3[wingTrailCap];
            }

            wingTrailWriteIndex = new int[GreatWyrmCount];
            wingTrailFillCount = new int[GreatWyrmCount];
            wingTrailSampleAccumulator = 0;
            EnsureWingTrailMeshAllocated();
        }

        private void EnsureWingTrailMeshAllocated()
        {
            if (wingTrailLeftMeshes == null || wingTrailLeftMeshes.Length != GreatWyrmCount)
                wingTrailLeftMeshes = new Mesh[GreatWyrmCount];
            if (wingTrailRightMeshes == null || wingTrailRightMeshes.Length != GreatWyrmCount)
                wingTrailRightMeshes = new Mesh[GreatWyrmCount];

            for (int i = 0; i < GreatWyrmCount; i++)
            {
                if (wingTrailLeftMeshes[i] == null)
                    wingTrailLeftMeshes[i] = CreateDynamicTrailMesh("PMP_AirPatrol_LeftTrail_" + i);
                if (wingTrailRightMeshes[i] == null)
                    wingTrailRightMeshes[i] = CreateDynamicTrailMesh("PMP_AirPatrol_RightTrail_" + i);
            }
        }

        private void ResetWingTrailBuffers()
        {
            if (wingTrailWriteIndex == null)
                return;
            for (int i = 0; i < GreatWyrmCount; i++)
            {
                wingTrailWriteIndex[i] = 0;
                wingTrailFillCount[i] = 0;
            }

            wingTrailSampleAccumulator = 0;
        }

        private void RecordWingTrailSamples()
        {
            if (!Ext.wingTrailEnabled)
                return;
            EnsureWingTrailRingAllocated();
            int interval = Mathf.Max(0, Ext.wingTrailSampleIntervalTicks);
            if (interval > 0)
            {
                wingTrailSampleAccumulator++;
                if (wingTrailSampleAccumulator <= interval)
                    return;
                wingTrailSampleAccumulator = 0;
            }

            Vector3[] centers = GetGreatWyrmWorldPositions();
            float moveSign = pass == AirPatrolPass.LeftToRight || pass == AirPatrolPass.PausingBetweenPasses ? 1f : -1f;
            Vector3 forward = new Vector3(moveSign, 0f, 0f);
            GetWingTipOffsets(moveSign, Ext, out Vector3 span, out float halfSpan, out float aftOffset);
            float yOff = Ext.wingTrailYOffset;

            for (int p = 0; p < GreatWyrmCount; p++)
            {
                Vector3 c = centers[p];
                Vector3 leftTip = GetWingTipWorldPos(c, true, span, halfSpan, aftOffset, forward, yOff);
                Vector3 rightTip = GetWingTipWorldPos(c, false, span, halfSpan, aftOffset, forward, yOff);
                int w = wingTrailWriteIndex[p];
                wingTrailLeftRing[p][w] = leftTip;
                wingTrailRightRing[p][w] = rightTip;
                wingTrailWriteIndex[p] = (w + 1) % wingTrailCap;
                if (wingTrailFillCount[p] < wingTrailCap)
                    wingTrailFillCount[p]++;
            }
        }

        private static void DrawWingTrails(Building_GreatWyrmOrbiter o)
        {
            if (o == null || !o.Ext.wingTrailEnabled || o.wingTrailLeftRing == null)
                return;

            int cap = o.wingTrailCap;
            float width = Mathf.Max(0.02f, o.Ext.planeDrawScale * o.Ext.wingTrailWidthFactor * Mathf.Max(0.2f, o.Ext.trailWidthFactor));
            float lenFactor = Mathf.Max(1f, o.Ext.wingTrailSegmentLengthFactor);
            float aMin = o.Ext.wingTrailAlphaMin;
            float aMax = o.Ext.wingTrailAlphaMax;
            float orbSize = Mathf.Max(0.08f, o.Ext.planeDrawScale * 0.08f * Mathf.Max(0.2f, o.Ext.orbSize));

            bool hasOrbandtail = AirPatrolOrbAndTailFx.TryGetMaterials(out Material orbMat, out Material trailMat);
            Material fallbackRibbonMat = hasOrbandtail ? null : ResolveWingTrailRibbonMaterial();
            if (!hasOrbandtail && fallbackRibbonMat == null)
                return;

            for (int p = 0; p < GreatWyrmCount; p++)
            {
                int fill = o.wingTrailFillCount[p];
                if (fill < 2)
                    continue;

                int oldest = (o.wingTrailWriteIndex[p] - fill + cap) % cap;
                int newest = (o.wingTrailWriteIndex[p] - 1 + cap) % cap;

                if (hasOrbandtail)
                {
                    DrawOrbTrailMesh(
                        o.wingTrailLeftRing[p],
                        oldest,
                        fill,
                        cap,
                        width,
                        lenFactor,
                        1f,
                        aMin,
                        aMax,
                        WingTrailColor,
                        o.Ext.trailIntensity,
                        trailMat,
                        o.wingTrailLeftMeshes?[p]
                    );
                    DrawOrbTrailMesh(
                        o.wingTrailRightRing[p],
                        oldest,
                        fill,
                        cap,
                        width,
                        lenFactor,
                        1f,
                        aMin,
                        aMax,
                        WingTrailColor,
                        o.Ext.trailIntensity,
                        trailMat,
                        o.wingTrailRightMeshes?[p]
                    );
                    DrawGlowOrb(o.wingTrailLeftRing[p][newest], orbSize, WingOrbColor, o.Ext.orbIntensity, orbMat);
                    DrawGlowOrb(o.wingTrailRightRing[p][newest], orbSize, WingOrbColor, o.Ext.orbIntensity, orbMat);
                }
                else
                {
                    DrawLegacyWingTrailChain(o.wingTrailLeftRing[p], oldest, fill, cap, width, lenFactor, 1f, aMin, aMax, fallbackRibbonMat);
                    DrawLegacyWingTrailChain(o.wingTrailRightRing[p], oldest, fill, cap, width, lenFactor, 1f, aMin, aMax, fallbackRibbonMat);
                }
            }
        }

        private static void DrawLegacyWingTrailChain(Vector3[] ring, int oldest, int fill, int cap, float width, float lenFactor, float tipWidthFactor, float aMin, float aMax, Material mat)
        {
            int segments = fill - 1;
            for (int i = 0; i < segments; i++)
            {
                int ia = (oldest + i) % cap;
                int ib = (oldest + i + 1) % cap;
                float ta = segments > 1 ? i / (float)segments : 0f;
                float tb = segments > 1 ? (i + 1) / (float)segments : 1f;
                float alpha = (Mathf.Lerp(aMin, aMax, ta) + Mathf.Lerp(aMin, aMax, tb)) * 0.5f;
                float segWidth = width * Mathf.Lerp(1f, tipWidthFactor, tb);
                DrawLegacyTrailRibbon(ring[ia], ring[ib], segWidth, lenFactor, alpha, mat);
            }
        }

        private static void DrawLegacyTrailRibbon(Vector3 p0, Vector3 p1, float width, float lenFactor, float alpha, Material mat)
        {
            Vector3 d = p1 - p0;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.002f)
                return;
            Vector3 dir = d / len;
            Vector3 mid = (p0 + p1) * 0.5f;
            mid.y = (p0.y + p1.y) * 0.5f;
            float yawDeg = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.AngleAxis(yawDeg, Vector3.up);
            Vector3 scale = new Vector3(len * lenFactor, 1f, width);
            Matrix4x4 matrix = Matrix4x4.TRS(mid, rot, scale);
            Color color = new Color(0.0f, 0.85f, 1f, Mathf.Clamp01(alpha));
            WingTrailMpb.Clear();
            WingTrailMpb.SetColor(ShaderPropertyIDs.Color, color);
            Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0, null, 0, WingTrailMpb);
        }

        private static void DrawOrbTrailMesh(
            Vector3[] ring,
            int oldest,
            int fill,
            int cap,
            float width,
            float lenFactor,
            float tipWidthFactor,
            float aMin,
            float aMax,
            Color baseColor,
            float intensity,
            Material mat,
            Mesh trailMesh
        )
        {
            if (trailMesh == null || fill < 2)
                return;
            if (!UpdateTrailMeshFromRing(trailMesh, ring, oldest, fill, cap, width, lenFactor, tipWidthFactor))
                return;

            Color c = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(aMax));
            TrailFxMpb.Clear();
            TrailFxMpb.SetColor(ShaderPropertyIDs.Color, c);
            TrailFxMpb.SetFloat(IntensityId, Mathf.Max(0.05f, intensity));
            Graphics.DrawMesh(trailMesh, Matrix4x4.identity, mat, 0, null, 0, TrailFxMpb);
        }

        private static bool UpdateTrailMeshFromRing(
            Mesh trailMesh,
            Vector3[] ring,
            int oldest,
            int fill,
            int cap,
            float width,
            float lenFactor,
            float tipWidthFactor
        )
        {
            int pointCount = Mathf.Min(fill, cap);
            if (pointCount < 2)
                return false;

            Vector3[] points = new Vector3[pointCount];
            for (int i = 0; i < pointCount; i++)
                points[i] = ring[(oldest + i) % cap];

            int vertCount = pointCount * 2;
            int triCount = (pointCount - 1) * 6;
            Vector3[] verts = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];
            Color[] colors = new Color[vertCount];
            int[] tris = new int[triCount];

            for (int i = 0; i < pointCount; i++)
            {
                float t = pointCount > 1 ? i / (float)(pointCount - 1) : 0f;
                float widthAt = width * Mathf.Lerp(1f, tipWidthFactor, t);

                Vector3 dir;
                if (i == 0)
                    dir = points[1] - points[0];
                else if (i == pointCount - 1)
                    dir = points[i] - points[i - 1];
                else
                    dir = points[i + 1] - points[i - 1];

                dir.y = 0f;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = Vector3.right;
                dir.Normalize();

                Vector3 right = Vector3.Cross(dir, Vector3.up);
                if (right.sqrMagnitude < 0.0001f)
                    right = Vector3.forward;
                right.Normalize();

                int vi = i * 2;
                float age = 1f - t;
                verts[vi] = points[i] - right * (widthAt * 0.5f);
                verts[vi + 1] = points[i] + right * (widthAt * 0.5f);
                uvs[vi] = new Vector2(0f, t * lenFactor);
                uvs[vi + 1] = new Vector2(1f, t * lenFactor);
                Color vc = new Color(age, 0f, 0f, 1f);
                colors[vi] = vc;
                colors[vi + 1] = vc;
            }

            int ti = 0;
            for (int i = 0; i < pointCount - 1; i++)
            {
                int v0 = i * 2;
                int v1 = v0 + 1;
                int v2 = v0 + 2;
                int v3 = v0 + 3;
                tris[ti++] = v0;
                tris[ti++] = v2;
                tris[ti++] = v1;
                tris[ti++] = v1;
                tris[ti++] = v2;
                tris[ti++] = v3;
            }

            trailMesh.Clear();
            trailMesh.vertices = verts;
            trailMesh.uv = uvs;
            trailMesh.colors = colors;
            trailMesh.triangles = tris;
            trailMesh.RecalculateBounds();
            return true;
        }

        private static void DrawGlowOrb(Vector3 pos, float size, Color color, float intensity, Material orbMat)
        {
            OrbFxMpb.Clear();
            OrbFxMpb.SetColor(ShaderPropertyIDs.Color, color);
            OrbFxMpb.SetFloat(CoreSizeId, 0.25f);
            OrbFxMpb.SetFloat(GlowFalloffId, 2.5f);
            OrbFxMpb.SetFloat(IntensityId, Mathf.Max(0.2f, intensity));
            Graphics.DrawMesh(
                MeshPool.plane10,
                Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(size, 1f, size)),
                orbMat,
                0,
                null,
                0,
                OrbFxMpb
            );
        }

        private static Material ResolveWingTrailRibbonMaterial()
        {
            if (cachedWingTrailRibbonMat != null && cachedWingTrailRibbonMat.shader != null)
                return cachedWingTrailRibbonMat;
            cachedWingTrailRibbonMat = MaterialPool.MatFrom(GenDraw.LineTexPath, ShaderDatabase.Transparent, Color.white);
            cachedWingTrailRibbonMat.SetInt("_ZTest", (int)CompareFunction.Always);
            return cachedWingTrailRibbonMat;
        }

        public static void DrawGreatWyrmFormation(Building_GreatWyrmOrbiter orbiter)
        {
            if (orbiter == null || orbiter.Destroyed || orbiter.Map == null)
                return;
            if (orbiter.pass == AirPatrolPass.Done)
                return;

            DrawWingTrails(orbiter);
            DrawMissiles(orbiter);

            Material mat = ResolveGreatWyrmMaterial();
            if (mat == null)
                return;

            Vector3[] positions = orbiter.GetGreatWyrmWorldPositions();
            float scale = orbiter.Ext.planeDrawScale;
            float moveSign = orbiter.pass == AirPatrolPass.LeftToRight || orbiter.pass == AirPatrolPass.PausingBetweenPasses
                ? 1f
                : -1f;
            float facingRad = Mathf.Atan2(0f, moveSign);
            float rotY = -(facingRad + Mathf.PI / 2f) * Mathf.Rad2Deg;
            Quaternion rot = Quaternion.AngleAxis(rotY, Vector3.up);

            int n = Mathf.Min(positions.Length, GreatWyrmCount);
            float depth = orbiter.Ext.planeDrawDepthFactor;
            for (int i = 0; i < n; i++)
            {
                Vector3 s = new Vector3(scale, 1f, scale * depth);
                Matrix4x4 m = Matrix4x4.TRS(positions[i], rot, s);
                Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);
            }
        }

        private static Material ResolveGreatWyrmMaterial()
        {
            // Anchor uses transparent graphic; formation uses GreatWyrm texture.
            string texPath = PlaneDrawTexPath;
            if (!MatByTexPath.TryGetValue(texPath, out Material mat) || mat == null)
            {
                mat = MaterialPool.MatFrom(texPath, ShaderDatabase.Cutout);
                mat.SetInt("_ZTest", (int)CompareFunction.Always);
                MatByTexPath[texPath] = mat;
            }

            return mat;
        }

        private static void DrawMissiles(Building_GreatWyrmOrbiter o)
        {
            if (o == null || o.activeMissiles.Count == 0)
                return;

            bool hasOrbandtail = AirPatrolOrbAndTailFx.TryGetMaterials(out Material orbMat, out Material trailMat);
            Material missileMat = hasOrbandtail ? null : ResolveMissileMaterial();
            if (!hasOrbandtail && missileMat == null)
                return;

            float missileScale = Mathf.Max(0.08f, o.Ext.planeDrawScale * 0.16f * Mathf.Max(0.2f, o.Ext.missileOrbSize));
            float orbSize = Mathf.Max(0.06f, o.Ext.planeDrawScale * 0.06f * Mathf.Max(0.2f, o.Ext.missileOrbSize));
            float trailWidth = Mathf.Max(
                0.015f,
                o.Ext.planeDrawScale
                    * o.Ext.missileTrailTipWidthFactor
                    * o.Ext.missileTrailWidthFactor
                    * Mathf.Max(0.2f, o.Ext.trailWidthFactor));
            float trailLenFactor = Mathf.Max(1f, o.Ext.missileTrailSegmentLengthFactor);
            int capLimit = Mathf.Clamp(o.Ext.wingTrailRingCapacity, 8, MissileTrailRingHardMax);

            for (int i = 0; i < o.activeMissiles.Count; i++)
            {
                AirPatrolMissile m = o.activeMissiles[i];
                if (hasOrbandtail)
                {
                    int cap = Mathf.Min(capLimit, m.trailRing.Length);
                    int fill = Mathf.Min(m.trailFill, cap);
                    if (fill >= 2)
                    {
                        int oldest = (m.trailWrite - fill + cap) % cap;
                        DrawOrbTrailMesh(
                            m.trailRing,
                            oldest,
                            fill,
                            cap,
                            trailWidth,
                            trailLenFactor,
                            Mathf.Clamp01(o.Ext.missileTrailTipWidthFactor),
                            0.12f,
                            0.95f,
                            MissileTrailColorFx,
                            o.Ext.trailIntensity,
                            trailMat,
                            m.trailMesh
                        );
                    }

                    DrawGlowOrb(m.pos, orbSize, MissileOrbColorFx, o.Ext.orbIntensity, orbMat);
                }
                else
                {
                    Graphics.DrawMesh(
                        MeshPool.plane10,
                        Matrix4x4.TRS(
                            m.pos,
                            Quaternion.AngleAxis(Mathf.Atan2(m.dir.z, m.dir.x) * Mathf.Rad2Deg, Vector3.up),
                            new Vector3(missileScale, 1f, missileScale)
                        ),
                        missileMat,
                        0
                    );
                }
            }
        }

        private static Material ResolveMissileMaterial()
        {
            if (cachedMissileMat != null && cachedMissileMat.shader != null)
                return cachedMissileMat;
            cachedMissileMat = MaterialPool.MatFrom(MissileDrawTexPath, ShaderDatabase.Transparent, Color.white);
            cachedMissileMat.SetInt("_ZTest", (int)CompareFunction.Always);
            return cachedMissileMat;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref leaderX, "leaderX", 0f);
            Scribe_Values.Look(ref leaderZ, "leaderZ", 0f);
            Scribe_Values.Look(ref pass, "pass", AirPatrolPass.LeftToRight);
            Scribe_Values.Look(ref betweenPassPauseTicks, "betweenPassPauseTicks", 0);
            Scribe_Values.Look(ref lastKillTick, "lastKillTick", -999999);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                RebuildExtraTargetDefs();
                ClearActiveMissiles();
                lockedTargetIds.Clear();
            }
        }
    }
}

