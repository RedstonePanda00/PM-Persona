using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    // Vanilla: drawRequests order follows AppendRequests; LayerFor only affects small world-Y in TryGetMatrix, not queue order.
    // For selected cardinals, move specific Kaldalis blade draw requests before the body mesh, then re-run vanilla's matrix refresh.
    // North: both blades. East: left (mirrored) only. West: right only.
    [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.ParallelPreDraw))]
    public static class Patch_KaldalisBladeDrawOrderUnderBodyNorth
    {
        private static readonly AccessTools.FieldRef<PawnRenderTree, List<PawnGraphicDrawRequest>> DrawRequestsField =
            AccessTools.FieldRefAccess<PawnRenderTree, List<PawnGraphicDrawRequest>>("drawRequests");

        public static void Postfix(PawnDrawParms parms, PawnRenderTree __instance)
        {
            if (__instance?.pawn == null)
                return;
            if (__instance.pawn.TryGetComp<CompKaldalisDualBladeRenderer>() == null)
                return;

            Rot4 f = parms.facing;
            if (f != Rot4.North && f != Rot4.East && f != Rot4.West)
                return;

            List<PawnGraphicDrawRequest> req = DrawRequestsField(__instance);
            if (req == null || req.Count == 0)
                return;

            List<PawnGraphicDrawRequest> toPull = new List<PawnGraphicDrawRequest>();
            List<PawnGraphicDrawRequest> remainder = new List<PawnGraphicDrawRequest>();

            foreach (PawnGraphicDrawRequest r in req)
            {
                if (r.node is PawnRenderNode_KaldalisBlade b && ShouldPullBladeBeforeBody(b, f))
                    toPull.Add(r);
                else
                    remainder.Add(r);
            }

            if (toPull.Count == 0)
                return;

            int bodyIdx = FindBodyDrawInsertIndex(__instance, remainder);

            req.Clear();
            req.AddRange(remainder.Take(bodyIdx));
            req.AddRange(toPull);
            req.AddRange(remainder.Skip(bodyIdx));

            RefreshDrawRequestMatrices(__instance, parms, req);
        }

        private static bool ShouldPullBladeBeforeBody(PawnRenderNode_KaldalisBlade b, Rot4 drawFacing)
        {
            if (drawFacing == Rot4.North)
                return true;
            if (drawFacing == Rot4.East)
                return !b.isRightSide;
            if (drawFacing == Rot4.West)
                return b.isRightSide;
            return false;
        }

        private static int FindBodyDrawInsertIndex(PawnRenderTree tree, List<PawnGraphicDrawRequest> others)
        {
            if (tree != null && tree.TryGetNodeByTag(PawnRenderNodeTagDefOf.Body, out PawnRenderNode bodyNode)
                && bodyNode != null)
            {
                for (int i = 0; i < others.Count; i++)
                {
                    if (others[i].node == bodyNode)
                        return i;
                }
            }

            for (int i = 0; i < others.Count; i++)
            {
                PawnRenderNode n = others[i].node;
                if (n?.Props != null && n.Props.tagDef == PawnRenderNodeTagDefOf.Body)
                    return i;
            }

            for (int i = 0; i < others.Count; i++)
            {
                if (others[i].node?.Worker != null && others[i].node.Worker.GetType() == typeof(PawnRenderNodeWorker_Body))
                    return i;
            }

            for (int i = 0; i < others.Count; i++)
            {
                string tn = others[i].node?.Worker?.GetType().Name ?? string.Empty;
                if (tn.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0
                    && tn.IndexOf("Apparel", StringComparison.OrdinalIgnoreCase) < 0)
                    return i;
            }

            return 0;
        }

        private static void RefreshDrawRequestMatrices(PawnRenderTree tree, PawnDrawParms parms, List<PawnGraphicDrawRequest> list)
        {
            for (int num = 0; num < list.Count; num++)
            {
                PawnGraphicDrawRequest value = list[num];
                if (!tree.TryGetMatrix(value.node, parms, out Matrix4x4 matrix))
                    break;
                if (value.node.CheckMaterialEveryDrawRequest)
                    value.material = value.node.Worker.GetFinalizedMaterial(value.node, parms);
                value.preDrawnComputedMatrix = matrix;
                list[num] = value;
            }
        }
    }
}
