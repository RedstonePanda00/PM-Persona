using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace PMP_Persona.Prometheus
{
    // Reuse vanilla per-item apparel building, including modded apparel nodes, for this mech only.
    // Equipment admission, float menu, jobs and gear tab remain owned by RsPandaLibrary.
    public sealed class DynamicPawnRenderNodeSetup_PrometheusApparel : DynamicPawnRenderNodeSetup
    {
        private delegate IEnumerable<(PawnRenderNode node, PawnRenderNode parent)> ProcessDelegate(
            Pawn pawn, PawnRenderTree tree, Apparel apparel, PawnRenderNode head, PawnRenderNode body,
            Dictionary<PawnRenderNode, int> offsets);
        private static readonly ProcessDelegate Process = AccessTools.MethodDelegate<ProcessDelegate>(
            AccessTools.Method(typeof(DynamicPawnRenderNodeSetup_Apparel), "ProcessApparel"));
        [ThreadStatic] internal static PawnRenderTree BuildingFor;
        public override bool HumanlikeOnly => false;
        public override IEnumerable<(PawnRenderNode node, PawnRenderNode parent)> GetDynamicNodes(Pawn pawn, PawnRenderTree tree)
        {
            if (!(pawn is Pawn_Prometheus prometheus)) return Array.Empty<(PawnRenderNode, PawnRenderNode)>();
            if (pawn.apparel == null || pawn.apparel.WornApparelCount == 0)
                return Array.Empty<(PawnRenderNode, PawnRenderNode)>();
            prometheus.EnsureApparel();
            tree.TryGetNodeByTag(PawnRenderNodeTagDefOf.ApparelHead, out var head);
            tree.TryGetNodeByTag(PawnRenderNodeTagDefOf.ApparelBody, out var body);
            var offsets = new Dictionary<PawnRenderNode, int>();
            var result = new List<(PawnRenderNode, PawnRenderNode)>();
            var previous = BuildingFor;
            try
            {
                BuildingFor = tree;
                foreach (var apparel in pawn.apparel.WornApparel)
                {
                    if (apparel.def.IsWeapon) continue;
                    foreach (var entry in Process(pawn, tree, apparel, head, body, offsets))
                    {
                        if (entry.node != null) result.Add(entry);
                        if (entry.parent != null)
                        { offsets.TryGetValue(entry.parent, out int n); offsets[entry.parent] = n + 1; }
                    }
                }
            }
            finally { BuildingFor = previous; }
            return result;
        }
    }
    public sealed class PawnRenderNodeWorker_PrometheusHeadApparelRoot : ProtossMech.PawnRenderNodeWorker_UmbraHeadApparelRoot
    {
        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            var offset = base.OffsetFor(node, parms, out pivot);
            // The compact full-body sprite's face is lower than a separate human head sprite.
            return offset - parms.pawn.Drawer.renderer.BaseHeadOffsetAt(parms.facing) * .5f;
        }
    }
    public sealed class PawnRenderNodeWorker_PrometheusBodyApparelRoot : PawnRenderNodeWorker
    {
        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
            => base.OffsetFor(node, parms, out pivot) + new Vector3(0, 0, -.10f);
    }

    [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.ShouldAddNodeToTree))]
    internal static class PrometheusApparelNodeAdmission
    {
        static void Postfix(PawnRenderTree __instance, PawnRenderNodeProperties props, ref bool __result)
        {
            // Vanilla creates ordinary apparel nodes as HumanlikeOnly. Exempt only the scoped
            // Prometheus apparel build; never mutate shared apparel Def properties or other races.
            if (!__result && props != null && __instance == DynamicPawnRenderNodeSetup_PrometheusApparel.BuildingFor
                && props.pawnType == PawnRenderNodeProperties.RenderNodePawnType.HumanlikeOnly)
                __result = true;
        }
    }

}
