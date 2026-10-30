using System.Collections.Generic;
using RimWorld;
using RsPandaLibrary;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    // 1x1 anchor building; draws carrier drone shadow meshes; does not physically move.
    [StaticConstructorOnStartup]
    public partial class Building_CarrierOrbiter : Building
    {
        private const int LifeTimeTicks = 60 * GenTicks.TicksPerRealSecond;

        private const int MaxLegionVariants = 8;
        private static Material[] variantShadowMats = new Material[MaxLegionVariants];

        private static int staticVariantCount;
        private static readonly List<Matrix4x4>[] CarrierShadowMatrixScratchByVariant = new List<Matrix4x4>[MaxLegionVariants];

        // Bound both transform and native Mote color arrays to the same batch capacity.
        private const int MaxInstancingBatch = 1023;
        // Shared batch scratch buffer; submissions remain single-threaded.
        private static readonly Matrix4x4[] CarrierSharedBatch = new Matrix4x4[MaxInstancingBatch];

        private const float DronePlaneSize = 5f;
        private const float ClusterPlaneSize = 1.1f;

        // Passive: every 60 ticks, bomb 4 times (targets can repeat).
        private const int PassiveIntervalTicks = 60;
        private const int PassiveBombsPerInterval = 4;

        private const int FadeBatchCount = 6;
        private const int FadeInStaggerTicks = 22;

        // Outbound: light ease-out only (no end-of-leg hover / crawl).
        private const float OutboundEasePower = 2.2f;
        private const float OutboundEaseBlend = 0.35f;
        private const float OutboundSpeedCellsPerTick = 1.5f;

        private const int TargetMaintenanceIntervalTicks = 240;
        private const int TargetFullRebuildIntervalTicks = 600;

        private static readonly DefModExtension_CarrierLegionSwarm FallbackLegionExt = BuildFallbackLegionExt();

        static Building_CarrierOrbiter()
        {
            for (int i = 0; i < MaxLegionVariants; i++)
            {
                CarrierShadowMatrixScratchByVariant[i] = new List<Matrix4x4>(2048);

            }

            EnsureVariantMaterialsInitialized();
        }

        private DefModExtension_CarrierLegionSwarm Ext =>
            def.GetModExtension<DefModExtension_CarrierLegionSwarm>() ?? FallbackLegionExt;

        public int VariantCount
        {
            get
            {
                int n = Ext.variants?.Count ?? 0;
                if (n < 1)
                    n = FallbackLegionExt.variants.Count;
                return Mathf.Clamp(n, 1, MaxLegionVariants);
            }
        }

        private IntVec3 orbitCenterCell;

        private int ageTicks;

        private List<ClusterState> clusters = new List<ClusterState>();

        // Cached hostile targets; key is thingIDNumber.
        private readonly Dictionary<int, Thing> hostileTargetsById = new Dictionary<int, Thing>(512);
        private readonly List<int> tmpTargetIds = new List<int>(512);
        private int lastTargetMaintenanceTick = -999999;
        private int lastTargetFullRebuildTick = -999999;
        private int lastPassiveTick = -999999;
        private List<PendingLegionExplosion> pendingLegionExplosions = new List<PendingLegionExplosion>(64);
        private readonly List<int> clusterOrderScratch = new List<int>(64);

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (respawningAfterLoad)
            {
                // PostLoadInit precedes map attachment; build map-sized buckets here.
                if (clusters == null || clusters.Count == 0 || flowNodes == null || flowNodes.Count == 0) InitializeFlock();
                RebuildFlockIndex();
                RebuildHostileTargetCache();
                return;
            }

            SetupOrbit(map.Center);
            RebuildHostileTargetCache();
            int now = Find.TickManager.TicksGame;
            lastPassiveTick = now;
            lastTargetMaintenanceTick = now;
            lastTargetFullRebuildTick = now;
        }

        public void SetupOrbit(IntVec3 centerCell)
        {
            orbitCenterCell = centerCell;
            ageTicks = 0;
            InitializeFlock();
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || Destroyed)
                return;

            ageTicks++;

            int now = Find.TickManager.TicksGame;
            TickFlock(now);
            if (now >= lastTargetMaintenanceTick + TargetMaintenanceIntervalTicks)
            {
                lastTargetMaintenanceTick = now;
                MaintainHostileTargetCache();
            }

            if (now >= lastTargetFullRebuildTick + TargetFullRebuildIntervalTicks)
            {
                lastTargetFullRebuildTick = now;
                RebuildHostileTargetCache();
            }

            ProcessPendingLegionExplosions(now);

            if (ageTicks < LifeTimeTicks && now >= lastPassiveTick + PassiveIntervalTicks)
            {
                lastPassiveTick = now;
                TriggerPassiveBombard();
            }

            if (ageTicks >= LifeTimeTicks && AllDronesDeparted && pendingLegionExplosions.Count == 0)
            {
                hostileTargetsById.Clear();
                tmpTargetIds.Clear();
                Destroy(DestroyMode.Vanish);
            }
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            hostileTargetsById.Clear();
            tmpTargetIds.Clear();
            pendingLegionExplosions.Clear();
            base.DeSpawn(mode);
        }

        public void EnqueueLegionStrike(int triggerTick, IntVec3 cell, float radius, int explosionDamage, int directDamage)
        {
            pendingLegionExplosions.Add(new PendingLegionExplosion
            {
                triggerTick = triggerTick,
                cell = cell,
                radius = radius,
                explosionDamage = explosionDamage,
                directDamage = directDamage
            });
        }

        private void ProcessPendingLegionExplosions(int now)
        {
            Map map = Map;
            if (map == null || pendingLegionExplosions.Count == 0)
                return;

            for (int i = pendingLegionExplosions.Count - 1; i >= 0; i--)
            {
                if (pendingLegionExplosions[i].triggerTick > now)
                    continue;

                PendingLegionExplosion strike = pendingLegionExplosions[i];
                LegionBombardmentUtility.ExecuteStrike(map, strike.cell, strike.radius, strike.explosionDamage,
                    strike.directDamage, this);
                pendingLegionExplosions.RemoveAt(i);
            }
        }

        private static float RotYDegreesFromVelocityXZ(Vector2 velXZ)
        {
            if (velXZ.sqrMagnitude < 1e-10f)
                return 0f;
            float facingRad = Mathf.Atan2(velXZ.y, velXZ.x);
            return -(facingRad + Mathf.PI / 2f) * Mathf.Rad2Deg;
        }

        private void RebuildHostileTargetCache()
        {
            hostileTargetsById.Clear();

            Map map = Map;
            if (map == null)
                return;

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null)
                return;

            // Pawns.
            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (!IsValidHostileTarget(p, playerFaction))
                    continue;
                hostileTargetsById[p.thingIDNumber] = p;
            }

            List<Thing> allBuildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < allBuildings.Count; i++)
            {
                Thing t = allBuildings[i];
                if (t == null || t.def == null || !t.def.useHitPoints)
                    continue;
                if (!IsValidHostileTarget(t, playerFaction))
                    continue;
                hostileTargetsById[t.thingIDNumber] = t;
            }
        }

        private void MaintainHostileTargetCache()
        {
            Map map = Map;
            if (map == null || hostileTargetsById.Count == 0)
                return;

            Faction playerFaction = Faction.OfPlayer;
            if (playerFaction == null)
                return;

            tmpTargetIds.Clear();
            foreach (var kv in hostileTargetsById)
            {
                Thing t = kv.Value;
                if (!IsValidHostileTarget(t, playerFaction))
                    tmpTargetIds.Add(kv.Key);
            }

            for (int i = 0; i < tmpTargetIds.Count; i++)
                hostileTargetsById.Remove(tmpTargetIds[i]);
            tmpTargetIds.Clear();

            var pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                if (!IsValidHostileTarget(p, playerFaction))
                    continue;
                hostileTargetsById[p.thingIDNumber] = p;
            }

            List<Thing> allBuildings = map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial);
            for (int i = 0; i < allBuildings.Count; i++)
            {
                Thing t = allBuildings[i];
                if (t == null || t.def == null || !t.def.useHitPoints)
                    continue;
                if (!IsValidHostileTarget(t, playerFaction))
                    continue;
                hostileTargetsById[t.thingIDNumber] = t;
            }
        }

        private void TriggerClusterSurgeToward(IntVec3 cell, int now)
        {
            var target = new Vector2(cell.x + .5f, cell.z + .5f);
            foreach (var cluster in clusters)
            {
                float distance = Vector2.Distance(cluster.position, target);
                if (distance > 85) continue;
                cluster.surgeTarget = target;
                cluster.surgeStartTick = now + Mathf.RoundToInt(distance * .8f);
                cluster.surgeEndTick = cluster.surgeStartTick + 100;
            }
        }

        private static bool IsValidHostileTarget(Thing t, Faction playerFaction)
        {
            if (t == null || t.Destroyed || !t.Spawned)
                return false;
            if (t.Faction == null || !t.Faction.HostileTo(playerFaction))
                return false;
            if (!t.Position.IsValid)
                return false;

            if (t is Pawn)
                return true;

            if (t is Building && t.def != null && t.def.category == ThingCategory.Building && t.def.useHitPoints)
                return true;

            return false;
        }

        public CarrierLegionVariantConfig GetVariantConfig(byte variantIndex)
        {
            List<CarrierLegionVariantConfig> list = Ext.variants;
            if (list == null || variantIndex >= list.Count)
                return null;
            return list[variantIndex];
        }

        public byte GetDispatchDroneVariantIndex(int clusterIndex)
        {
            if (clusterIndex < 0 || clusterIndex >= clusters.Count)
                return 0;
            ClusterState cluster = clusters[clusterIndex];
            for (int i = 0; i < cluster.drones.Length; i++)
            {
                if (cluster.drones[i].flightState == 0)
                    return cluster.drones[i].variantIndex;
            }

            return cluster.drones.Length > 0 ? cluster.drones[0].variantIndex : (byte)0;
        }

        private void TriggerPassiveBombard()
        {
            Map map = Map;
            if (map == null || hostileTargetsById.Count == 0)
                return;

            // Snapshot keys (cheap: at most a few hundred entries).
            tmpTargetIds.Clear();
            foreach (int id in hostileTargetsById.Keys)
                tmpTargetIds.Add(id);

            if (tmpTargetIds.Count == 0)
                return;

            int now = Find.TickManager.TicksGame;

            int shots = Mathf.Min(PassiveBombsPerInterval, clusters.Count);
            clusterOrderScratch.Clear();
            for (int i = 0; i < clusters.Count; i++)
                clusterOrderScratch.Add(i);
            clusterOrderScratch.Shuffle();

            for (int shot = 0; shot < shots; shot++)
            {
                IntVec3 cell;
                if (!TryPickValidTargetCell(map, out cell))
                    break;

                int clusterIndex = clusterOrderScratch[shot % clusterOrderScratch.Count];
                byte variantIndex = GetDispatchDroneVariantIndex(clusterIndex);
                ILegionBombardmentHandler handler = LegionBombardmentRegistry.GetHandler(this, variantIndex);
                int travelTicks;
                handler.ExecuteBombard(this, variantIndex, clusterIndex, cell, now, out travelTicks);
                TriggerClusterSurgeToward(cell, now);
            }

            clusterOrderScratch.Clear();
            tmpTargetIds.Clear();
        }

        public void DispatchOneDroneToTarget(int clusterIndex, IntVec3 targetCell, out int travelTicks,
            out Vector2 startXZ) => DispatchOneDroneToTarget(clusterIndex, targetCell, out travelTicks, out startXZ, 0);

        public void DispatchOneDroneToTarget(int clusterIndex, IntVec3 targetCell, out int travelTicks,
            out Vector2 startXZ, int bombardmentTailTicks)
        {
            travelTicks = 0;
            startXZ = Vector2.zero;
            Map map = Map;
            if (ageTicks >= LifeTimeTicks || map == null || clusterIndex < 0 || clusterIndex >= clusters.Count)
                return;
            if (!targetCell.InBounds(map))
                return;

            int now = Find.TickManager.TicksGame;
            ClusterState cluster = clusters[clusterIndex];

            int droneIndex = -1;
            for (int i = 0; i < cluster.drones.Length; i++)
            {
                if (cluster.drones[i].flightState == 0)
                {
                    droneIndex = i;
                    break;
                }
            }
            if (droneIndex < 0) return;

            DroneState drone = cluster.drones[droneIndex];
            startXZ = drone.position;
            Vector3 targetV = targetCell.ToVector3Shifted();
            Vector2 targetXZ = new Vector2(targetV.x, targetV.z);

            Vector2 delta = targetXZ - startXZ;
            Vector2 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector2.right;

            float dist = delta.magnitude;
            travelTicks = Mathf.Clamp(Mathf.CeilToInt(dist / OutboundSpeedCellsPerTick), 15, 280);

            drone.flightState = 1;
            drone.flightStartTick = now;
            drone.flightDurationTicks = travelTicks;
            drone.bombardmentEndTick = now + travelTicks + Mathf.Max(0, bombardmentTailTicks);
            drone.warpStartTick = 0;
            drone.flightTargetCell = targetCell;
            drone.flightStartXZ = startXZ;
            drone.flightTargetXZ = targetXZ;
            drone.flightDirXZ = dir;
            drone.flightControlXZ = KeepInside(startXZ + (drone.velocity.sqrMagnitude > .001f
                ? drone.velocity.normalized : dir) * Mathf.Min(12, dist * .25f));
            cluster.drones[droneIndex] = drone;

            clusters[clusterIndex] = cluster;
        }

        private bool TryPickValidTargetCell(Map map, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map == null || tmpTargetIds.Count == 0)
                return false;

            Faction playerFaction = Faction.OfPlayer;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int id = tmpTargetIds[Rand.Range(0, tmpTargetIds.Count)];
                Thing t;
                if (!hostileTargetsById.TryGetValue(id, out t) || t == null)
                {
                    hostileTargetsById.Remove(id);
                    continue;
                }

                if (!IsValidHostileTarget(t, playerFaction) || !t.Position.InBounds(map))
                {
                    hostileTargetsById.Remove(id);
                    continue;
                }

                cell = t.Position;
                return true;
            }

            return false;
        }

        private static float OutboundEaseT(float tLin)
        {
            float u = Mathf.Clamp01(tLin);
            float tEase = 1f - Mathf.Pow(1f - u, OutboundEasePower);
            return Mathf.Lerp(u, tEase, OutboundEaseBlend);
        }

        private static void DrawInstancedBatch(List<Matrix4x4> matrices, Material mat)
        {
            if (matrices.Count == 0 || mat == null)
                return;

            int offset = 0;
            while (offset < matrices.Count)
            {
                int n = Mathf.Min(MaxInstancingBatch, matrices.Count - offset);
                matrices.CopyTo(offset, CarrierSharedBatch, 0, n);
                Graphics.DrawMeshInstanced(MeshPool.plane10, 0, mat, CarrierSharedBatch, n);
                offset += n;
            }
        }

        public static void BeginCarrierDrawFrame()
        {
            for (int v = 0; v < staticVariantCount; v++)
            {
                CarrierShadowMatrixScratchByVariant[v].Clear();

            }
        }

        public static void FlushCarrierDraw()
        {
            for (int v = 0; v < staticVariantCount; v++)
            {
                DrawInstancedBatch(CarrierShadowMatrixScratchByVariant[v], variantShadowMats[v]);

            }
        }

        public static void DrawCarrierShadows(Building_CarrierOrbiter carrier)
        {
            BeginCarrierDrawFrame();
            CollectCarrierMatrices(carrier);
            FlushCarrierDraw();
        }

        public static void CollectCarrierMatrices(Building_CarrierOrbiter carrier)
        {
            if (carrier == null || carrier.Destroyed || carrier.Map == null)
                return;
            EnsureVariantMaterialsReady();
            if (!HasAnyVariantShadowMaterial())
                return;

            int maxVariant = Mathf.Min(carrier.VariantCount, staticVariantCount);

            float alt = AltitudeLayer.MoteOverhead.AltitudeFor();

            for (int c = 0; c < carrier.clusters.Count; c++)
            {
                ClusterState cluster = carrier.clusters[c];

                for (int d = 0; d < cluster.drones.Length; d++)
                {
                    DroneState drone = cluster.drones[d];
                    if (drone.flightState == 5 || drone.flightState == 7 || drone.flightState == 9) continue;
                    Vector3 pos = new Vector3(drone.position.x, alt, drone.position.y);
                    float rotY = RotYDegreesFromVelocityXZ(drone.velocity);

                    Vector3 scale = new Vector3(DronePlaneSize * drone.scale * ClusterPlaneSize, 1f,
                        DronePlaneSize * 0.75f * drone.scale * ClusterPlaneSize);

                    carrier.ApplyDroneWarpVisual(drone, ref scale, 1);

                    Quaternion rot = Quaternion.AngleAxis(rotY, Vector3.up);
                    int vi = drone.variantIndex;
                    if (vi >= maxVariant)
                        vi = 0;
                    CarrierShadowMatrixScratchByVariant[vi].Add(Matrix4x4.TRS(pos, rot, scale));

                }
            }
        }

        private static DefModExtension_CarrierLegionSwarm BuildFallbackLegionExt()
        {
            return new DefModExtension_CarrierLegionSwarm
            {
                variants = new List<CarrierLegionVariantConfig>
                {
                    new CarrierLegionVariantConfig
                    {
                        variantId = "AA",
                        droneTexPath = "Things/CAS/Legion/Legion_AA",
                        droneGlowDefName = "PMP_LegionShadow_AA",

                        bombardmentHandlerClass =
                            "PMP_Persona.CarrierAircraftLegion.LegionBombardmentHandler_AA"
                    },
                    new CarrierLegionVariantConfig
                    {
                        variantId = "AG",
                        droneTexPath = "Things/CAS/Legion/Legion_AG",
                        droneGlowDefName = "PMP_LegionShadow_AG",

                        bombardmentHandlerClass =
                            "PMP_Persona.CarrierAircraftLegion.LegionBombardmentHandler_AG"
                    },
                    new CarrierLegionVariantConfig
                    {
                        variantId = "LB",
                        droneTexPath = "Things/CAS/Legion/Legion_LB",
                        droneGlowDefName = "PMP_LegionShadow_LB",

                        bombardmentHandlerClass =
                            "PMP_Persona.CarrierAircraftLegion.LegionBombardmentHandler_LB"
                    }
                }
            };
        }

        private static void EnsureVariantMaterialsInitialized()
        {
            DefModExtension_CarrierLegionSwarm ext = DefDatabase<ThingDef>
                .GetNamedSilentFail("PMP_CarrierOrbiter")
                ?.GetModExtension<DefModExtension_CarrierLegionSwarm>();
            if (ext?.variants == null || ext.variants.Count == 0)
                ext = FallbackLegionExt;

            staticVariantCount = Mathf.Clamp(ext.variants.Count, 1, MaxLegionVariants);
            for (int i = 0; i < staticVariantCount; i++)
            {
                CarrierLegionVariantConfig cfg = ext.variants[i];
                variantShadowMats[i] = ResolveVariantBodyMat(cfg);

            }
        }

        private static void EnsureVariantMaterialsReady()
        {
            if (HasAnyVariantShadowMaterial())
                return;
            EnsureVariantMaterialsInitialized();
        }

        private static bool HasAnyVariantShadowMaterial()
        {
            for (int i = 0; i < staticVariantCount; i++)
            {
                if (variantShadowMats[i] != null)
                    return true;
            }

            return false;
        }

        private static Material ResolveVariantBodyMat(CarrierLegionVariantConfig cfg)
        {
            string logTag = cfg?.variantId ?? "?";
            if (cfg != null && !string.IsNullOrEmpty(cfg.droneGlowDefName))
            {
                Material fromDef = ResolveInstancedOverlayFromDef(cfg.droneGlowDefName, "LegionBody_" + logTag);
                if (fromDef != null)
                    return fromDef;
            }

            return ResolveInstancedOverlayFromTexPath(cfg?.droneTexPath, ShaderDatabase.Mote, "LegionBody_" + logTag);
        }

        private static Material ResolveInstancedOverlayFromDef(string defName, string logTag)
        {
            var thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (thingDef?.graphic == null)
            {
                Log.Warning("[PMP CarrierAircraftLegion] Missing glow ThingDef: " + defName);
                return null;
            }

            return CloneInstancedOverlay(thingDef.graphic.MatSingle, logTag);
        }

        private static Material ResolveInstancedOverlayFromTexPath(string texPath, Shader shader, string logTag)
        {
            if (string.IsNullOrEmpty(texPath))
                texPath = "Things/CAS/Legion/Legion_AA";

            if (ContentFinder<Texture2D>.Get(texPath, false) == null)
            {
                Log.Warning("[PMP CarrierAircraftLegion] Missing Legion texture at " + texPath
                    + "; check xml/PMPersona/Textures/" + texPath + ".png");
            }

            Material baseMat = MaterialPool.MatFrom(texPath, shader);
            return CloneInstancedOverlay(baseMat, logTag);
        }

        private static Material CloneInstancedOverlay(Material baseMat, string logTag)
        {
            if (baseMat == null)
                return null;

            Material overlay = new Material(baseMat);
            overlay.SetInt("_ZTest", (int)CompareFunction.Always);
            // Cache each material's _Color SSBO batch for gl_InstanceIndex-based Mote rendering.
            var colors = new Vector4[MaxInstancingBatch];
            Vector4 tint = overlay.color;
            for (int i = 0; i < colors.Length; i++) colors[i] = tint;
            overlay.SetVectorArray("_Color", colors);
            overlay.enableInstancing = true;
            if (!overlay.enableInstancing)
                Log.Warning("[PMP CarrierAircraftLegion] " + logTag
                    + ": enableInstancing is FALSE. Shader may lack #pragma multi_compile_instancing.");
            return overlay;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref orbitCenterCell, "orbitCenterCell");

            Scribe_Values.Look(ref ageTicks, "ageTicks", 0);
            Scribe_Values.Look(ref lastTargetMaintenanceTick, "lastTargetMaintenanceTick", -999999);
            Scribe_Values.Look(ref lastTargetFullRebuildTick, "lastTargetFullRebuildTick", -999999);
            Scribe_Values.Look(ref lastPassiveTick, "lastPassiveTick", -999999);
            Scribe_Collections.Look(ref clusters, "legionClusters", LookMode.Deep);
            Scribe_Collections.Look(ref flowNodes, "legionFlowNodes", LookMode.Deep);
            Scribe_Collections.Look(ref pendingLegionExplosions, "legionPendingStrikes", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingLegionExplosions ??= new List<PendingLegionExplosion>();
            }
        }
    }
}
