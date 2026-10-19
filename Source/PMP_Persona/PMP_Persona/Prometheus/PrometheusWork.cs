using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using ProtossMech;
using RimWorld;
using Verse;
using Verse.AI;

namespace PMP_Persona.Prometheus
{
    [HarmonyPatch(typeof(JobGiver_Work), "PawnCanUseWorkGiver")]
    public static class PrometheusWorkAdmission
    {
        public static bool CanDo(WorkGiverDef giver, Pawn pawn) => giver.canBeDoneByMechs || pawn is Pawn_Prometheus;
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var field = AccessTools.Field(typeof(WorkGiverDef), nameof(WorkGiverDef.canBeDoneByMechs));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.LoadsField(field))
                {
                    var loadPawn = new CodeInstruction(OpCodes.Ldarg_1);
                    loadPawn.MoveLabelsFrom(instruction);
                    loadPawn.MoveBlocksFrom(instruction);
                    yield return loadPawn;
                    yield return CodeInstruction.Call(typeof(PrometheusWorkAdmission), nameof(CanDo));
                }
                else yield return instruction;
            }
        }
    }
    [HarmonyPatch(typeof(ProtossMechWorkUtility), nameof(ProtossMechWorkUtility.AllowsWorkGiver))]
    public static class PrometheusManualWorkAdmission
    {
        public static bool Prefix(Pawn pawn, WorkGiverDef giver, ref bool __result)
        {
            if (!(pawn is Pawn_Prometheus)) return true;
            __result = ProtossMechWorkUtility.CanManage(pawn) && giver?.workType != null
                && !pawn.WorkTypeIsDisabled(giver.workType) && !pawn.WorkTagIsDisabled(giver.workTags);
            return false;
        }
    }
}
