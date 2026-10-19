using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.GestaltZero
{
    public class Thing_GestaltZeroBeacon : ThingWithComps
    {
        private int lifecycleToken;

        [Unsaved]
        private Pawn preparedPawn;

        [Unsaved]
        private PawnDrawParms preparedParms;

        [Unsaved]
        private bool renderPrepared;

        [Unsaved]
        private bool renderDiagnosticLogged;

        [Unsaved]
        private bool renderFailureLogged;

        [Unsaved]
        private bool coreMaterialsReady;

        [Unsaved]
        private float preparedScanPhase;

        public void Configure(int token)
        {
            lifecycleToken = token;
            preparedPawn = null;
            renderPrepared = false;
            renderDiagnosticLogged = false;
            renderFailureLogged = false;
            coreMaterialsReady = false;
            preparedScanPhase = 0f;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lifecycleToken, "gestaltZeroLifecycleToken", 0);
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            // EnsureInitialized and Draw run on the main thread. Cache the pawn
            // there so the parallel phase never touches GameComponents or their
            // persistence dictionaries from a worker thread.
            Pawn pawn = preparedPawn;
            GameComponent_GestaltZeroReturn component = null;
            if (phase != DrawPhase.ParallelPreDraw)
            {
                component =
                    Current.Game?.GetComponent<GameComponent_GestaltZeroReturn>();
                pawn = component?.GetPooledPawn(lifecycleToken);
            }
            if (pawn == null)
            {
                renderPrepared = false;
                preparedPawn = null;
                if (phase == DrawPhase.Draw && !renderFailureLogged)
                {
                    renderFailureLogged = true;
                    Log.Warning("[PMP_Persona] Gestalt Zero beacon draw skipped: no pooled pawn matched token " +
                                lifecycleToken + ".");
                }
                return;
            }

            Vector3 pawnLoc = drawLoc;
            pawnLoc.y = AltitudeLayer.Pawn.AltitudeFor();
            switch (phase)
            {
                case DrawPhase.EnsureInitialized:
                    renderPrepared = false;
                    preparedPawn = pawn;
                    preparedScanPhase = component?.ReconstructionPhase(lifecycleToken) ?? 0f;
                    GestaltZeroReconstructionRenderer.EnsureInitialized(
                        pawn, !coreMaterialsReady, preparedScanPhase);
                    break;

                case DrawPhase.ParallelPreDraw:
                    preparedPawn = pawn;
                    renderPrepared = GestaltZeroReconstructionRenderer.TryPrepare(
                        pawn, pawnLoc, out preparedParms, out _);
                    break;

                case DrawPhase.Draw:
                    string preparationError = null;
                    if (!renderPrepared || !ReferenceEquals(preparedPawn, pawn))
                    {
                        preparedPawn = pawn;
                        preparedScanPhase = component?.ReconstructionPhase(lifecycleToken) ?? 0f;
                        GestaltZeroReconstructionRenderer.EnsureInitialized(
                            pawn, !coreMaterialsReady, preparedScanPhase);
                        renderPrepared = GestaltZeroReconstructionRenderer.TryPrepare(
                            pawn, pawnLoc, out preparedParms, out preparationError);
                    }

                    if (!renderPrepared)
                    {
                        if (!renderFailureLogged)
                        {
                            renderFailureLogged = true;
                            Log.Warning("[PMP_Persona] Gestalt Zero beacon RenderTree preparation failed: " +
                                        (preparationError ?? "unknown reason"));
                        }
                        break;
                    }

                    bool drew = GestaltZeroReconstructionRenderer.DrawPrepared(
                        pawn, preparedParms, out int requestCount, out int drawnCount,
                        out string shaderName, out string drawError);
                    if (!drew)
                    {
                        if (!renderFailureLogged)
                        {
                            renderFailureLogged = true;
                            Log.Warning("[PMP_Persona] Gestalt Zero beacon RenderTree draw failed: " +
                                        (drawError ?? "unknown reason"));
                        }
                    }
                    else
                    {
                        if (shaderName.StartsWith(
                            GestaltZeroReconstructionRenderer.ReconstructionShaderName,
                            StringComparison.Ordinal))
                            coreMaterialsReady = true;

                        if (coreMaterialsReady && !renderDiagnosticLogged)
                        {
                            renderDiagnosticLogged = true;
                            Log.Message("[PMP_Persona] Gestalt Zero beacon render ready: pawn=" +
                                        pawn.ThingID + ", requests=" + requestCount +
                                        ", drawn=" + drawnCount + ", shader=" + shaderName + ".");
                        }
                    }

                    renderPrepared = false;
                    break;
            }
        }

        public override string GetInspectString()
        {
            string baseText = base.GetInspectString();
            GameComponent_GestaltZeroReturn component =
                Current.Game?.GetComponent<GameComponent_GestaltZeroReturn>();
            int seconds = Mathf.CeilToInt((component?.RemainingTicks(lifecycleToken) ?? 0) / 60f);
            string timer = "PMP.GestaltZero.BeaconInspect".Translate(seconds);
            return string.IsNullOrEmpty(baseText) ? timer : baseText + "\n" + timer;
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (Gizmo gizmo in base.GetGizmos())
                yield return gizmo;

            GameComponent_GestaltZeroReturn component =
                Current.Game?.GetComponent<GameComponent_GestaltZeroReturn>();
            if (!Prefs.DevMode || component == null || !component.MatchesToken(lifecycleToken))
                yield break;

            yield return new Command_Action
            {
                defaultLabel = "PMP.GestaltZero.DevComplete".Translate(),
                action = delegate { component.DevCompleteNow(lifecycleToken); }
            };
        }
    }

    [StaticConstructorOnStartup]
    internal static class GestaltZeroReconstructionRenderer
    {
        private const string CoreReconstructHediffDefName = "Hediff_Reconstruct";
        public const string ReconstructionShaderName = "PM17N_ReconstructPrintCutout";
        private const string ShaderName = ReconstructionShaderName;

        private static readonly AccessTools.FieldRef<PawnRenderTree, List<PawnGraphicDrawRequest>>
            DrawRequestsField =
                AccessTools.FieldRefAccess<PawnRenderTree, List<PawnGraphicDrawRequest>>("drawRequests");

        private static HediffDef coreReconstructHediffDef;

        private static PawnDrawParms BuildDrawParms(Pawn pawn, Vector3 drawLoc)
        {
            // This is ordinary map drawing, not portrait/cache rendering.
            // DrawNow uses Graphics.DrawMeshNow and drops the supplied
            // MaterialPropertyBlock, which is exactly where PMC writes every
            // reconstruction scan parameter.
            PawnRenderFlags flags = PawnRenderFlags.Headgear |
                                    PawnRenderFlags.Clothes | PawnRenderFlags.NeverAimWeapon;
            PawnDrawParms parms = PawnDrawParms.Default;
            parms.pawn = pawn;
            parms.matrix = Matrix4x4.TRS(drawLoc, Quaternion.identity, Vector3.one);
            parms.facing = Rot4.South;
            parms.rotDrawMode = RotDrawMode.Fresh;
            parms.posture = PawnPosture.Standing;
            parms.flags = flags;
            parms.tint = Color.white;
            parms.dead = false;
            parms.crawling = false;
            parms.swimming = false;
            return parms;
        }

        public static void EnsureInitialized(Pawn pawn, bool forceMaterialRecache,
            float scanPhase)
        {
            EnsureCoreReconstructionVisual(pawn, scanPhase);
            PawnRenderTree tree = pawn?.Drawer?.renderer?.renderTree;
            if (tree == null)
                return;

            if (forceMaterialRecache)
                tree.SetDirty();

            PawnRenderFlags flags = PawnRenderFlags.Headgear |
                                    PawnRenderFlags.Clothes | PawnRenderFlags.NeverAimWeapon;
            tree.EnsureInitialized(flags);
        }

        public static bool TryPrepare(Pawn pawn, Vector3 drawLoc, out PawnDrawParms parms,
            out string error)
        {
            parms = PawnDrawParms.Default;
            error = null;
            PawnRenderTree tree = pawn?.Drawer?.renderer?.renderTree;
            if (tree == null)
            {
                error = "pawn renderer or render tree is null";
                return false;
            }

            try
            {
                parms = BuildDrawParms(pawn, drawLoc);
                tree.ParallelPreDraw(parms);
                List<PawnGraphicDrawRequest> requests = DrawRequestsField(tree);
                if (requests == null || requests.Count == 0)
                {
                    error = "RenderTree produced no draw requests";
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                return false;
            }
        }

        public static bool DrawPrepared(Pawn pawn, PawnDrawParms parms, out int requestCount,
            out int drawnCount, out string shaderName, out string error)
        {
            requestCount = 0;
            drawnCount = 0;
            shaderName = "unresolved";
            error = null;
            PawnRenderTree tree = pawn?.Drawer?.renderer?.renderTree;
            if (tree == null)
            {
                error = "pawn renderer or render tree is null during Draw";
                return false;
            }

            try
            {
                List<PawnGraphicDrawRequest> requests = DrawRequestsField(tree);
                requestCount = requests?.Count ?? 0;
                if (requestCount == 0)
                {
                    error = "prepared RenderTree has no draw requests";
                    return false;
                }

                int reconstructedMaterialCount = 0;
                for (int i = 0; i < requests.Count; i++)
                {
                    PawnGraphicDrawRequest request = requests[i];
                    if (request.mesh != null && request.material != null)
                        drawnCount++;
                    if (request.material?.shader != null &&
                        string.Equals(request.material.shader.name, ShaderName,
                            StringComparison.Ordinal))
                        reconstructedMaterialCount++;
                }

                if (drawnCount == 0)
                {
                    error = "all RenderTree draw requests were empty";
                    return false;
                }

                // Use PMC's unmodified PawnRenderTree.Draw path. Its
                // GetFinalizedMaterial and GetMaterialPropertyBlock Harmony
                // patches now see Hediff_Reconstruct and own the entire shader
                // remap/property pipeline, exactly as they do for PMC pawns.
                tree.Draw(parms);
                shaderName = reconstructedMaterialCount > 0
                    ? ShaderName + " (PMC, " + reconstructedMaterialCount + " materials)"
                    : "PMC material remap pending";
                return true;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                return false;
            }
        }

        private static void EnsureCoreReconstructionVisual(Pawn pawn, float scanPhase)
        {
            if (pawn?.health?.hediffSet == null || pawn.Dead)
                return;

            coreReconstructHediffDef = coreReconstructHediffDef ??
                DefDatabase<HediffDef>.GetNamedSilentFail(CoreReconstructHediffDefName);
            if (coreReconstructHediffDef == null)
                return;

            Hediff hediff = pawn.health.hediffSet.GetFirstHediffOfDef(coreReconstructHediffDef);
            if (hediff == null)
            {
                hediff = HediffMaker.MakeHediff(coreReconstructHediffDef, pawn);
                pawn.health.AddHediff(hediff);
                pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            }

            // PMC maps ageTicks across the Hediff's 90-tick duration. The pawn
            // is pooled and therefore does not tick, so the lifecycle component
            // drives a one-way epoch and the final fully-visible hold here.
            hediff.ageTicks = Mathf.RoundToInt(Mathf.Clamp01(scanPhase) * 90f);
        }

        public static void RemoveCoreReconstructionVisual(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return;

            coreReconstructHediffDef = coreReconstructHediffDef ??
                DefDatabase<HediffDef>.GetNamedSilentFail(CoreReconstructHediffDefName);
            Hediff hediff = coreReconstructHediffDef != null
                ? pawn.health.hediffSet.GetFirstHediffOfDef(coreReconstructHediffDef)
                : null;
            if (hediff != null)
                pawn.health.RemoveHediff(hediff);
        }

    }
}
