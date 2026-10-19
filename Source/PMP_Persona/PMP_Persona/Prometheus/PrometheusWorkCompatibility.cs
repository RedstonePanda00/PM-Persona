using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace PMP_Persona.Prometheus
{
    [HarmonyPatch(typeof(SkillRequirement), nameof(SkillRequirement.PawnSatisfies))]
    internal static class PrometheusMechanicalSkillRequirement
    {
        private static bool Prefix(SkillRequirement __instance, Pawn pawn, ref bool __result)
        {
            if (!(pawn is Pawn_Prometheus)) return true;
            // Vanilla's fast path requires an overseer-controlled mech. PMC mechs are
            // autonomous; use the same fixed race level without inventing skill records.
            __result = pawn.RaceProps.mechFixedSkillLevel >= __instance.minLevel;
            return false;
        }
    }

    [HarmonyPatch(typeof(StatWorker), nameof(StatWorker.IsDisabledFor))]
    internal static class PrometheusMechanicalStatAvailability
    {
        private static bool Prefix(Thing thing, ref bool __result)
        {
            if (!(thing is Pawn_Prometheus)) return true;
            // Vanilla uses story presence as a proxy for human skills. Here story is
            // apparel metadata only; mechanical skills have no disabled-skill records.
            __result = false;
            return false;
        }
    }

    // No per-pawn skills, fake SkillRecords, or global learning patches. Adapt only
    // the vanilla work/interaction paths that still assume a human skill tracker.
    // Research and scanner null guards are already provided by PMC.
    [HarmonyPatch]
    internal static class PrometheusWorkCompatibility
    {
        private static readonly FieldInfo Skills = AccessTools.Field(typeof(Pawn), nameof(Pawn.skills));
        private static readonly MethodInfo GetSkill = AccessTools.Method(typeof(Pawn_SkillTracker), nameof(Pawn_SkillTracker.GetSkill));
        private static readonly MethodInfo Level = AccessTools.PropertyGetter(typeof(SkillRecord), nameof(SkillRecord.Level));
        private static readonly MethodInfo Disabled = AccessTools.PropertyGetter(typeof(SkillRecord), nameof(SkillRecord.TotallyDisabled));
        private static readonly MethodInfo Learn = AccessTools.Method(typeof(Pawn_SkillTracker), nameof(Pawn_SkillTracker.Learn));
        internal static readonly List<string> PatchedMethods = new List<string>();

        private static readonly Type[] WorkTypes =
        {
            typeof(WorkGiver_InteractAnimal), typeof(JobDriver_GatherAnimalBodyResources),
            typeof(JobDriver_Hack), typeof(JobDriver_CreateXenogerm),
            typeof(JobDriver_StudyInteract), typeof(JobDriver_ActivitySuppression),
            typeof(JobDriver_Lessongiving), typeof(Toils_Misc),
            typeof(Pawn_InteractionsTracker), typeof(SocialInteractionUtility),
            typeof(InteractionWorker_ConvertIdeoAttempt)
        };

        private static IEnumerable<Type> WithClosures(Type type)
        {
            yield return type;
            foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var child in WithClosures(nested)) yield return child;
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (var type in WorkTypes.SelectMany(WithClosures))
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                             BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                    var code = PatchProcessor.GetOriginalInstructions(method).ToList();
                    if (!code.Any(c => c.Calls(Learn) || c.Calls(GetSkill))) continue;
                    // ChooseSkill enumerates a human skill list and is handled separately below.
                    if (method.DeclaringType == typeof(JobDriver_Lessongiving) && method.Name == "ChooseSkill") continue;
                    PatchedMethods.Add(type.FullName + "." + method.Name);
                    yield return method;
                }
        }

        public static int FixedLevel(Pawn pawn, SkillDef skill) => pawn is Pawn_Prometheus
            ? pawn.RaceProps.mechFixedSkillLevel : pawn.skills.GetSkill(skill).Level;

        public static bool SkillDisabled(Pawn pawn, SkillDef skill) => !(pawn is Pawn_Prometheus)
            && pawn.skills.GetSkill(skill).TotallyDisabled;

        public static void LearnIfPresent(Pawn_SkillTracker tracker, SkillDef skill, float xp,
            bool direct, bool ignoreLearnRate) => tracker?.Learn(skill, xp, direct, ignoreLearnRate);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(Learn))
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = AccessTools.Method(typeof(PrometheusWorkCompatibility), nameof(LearnIfPresent));
                }
                if (!code[i].LoadsField(Skills)) continue;
                // Match only a direct pawn.skills.GetSkill(...).Level/TotallyDisabled chain.
                // Keep labels/exception boundaries in place by replacing instructions with nop.
                for (int j = i + 1; j < Math.Min(i + 10, code.Count - 1); j++)
                {
                    if (!code[j].Calls(GetSkill)) continue;
                    bool level = code[j + 1].Calls(Level);
                    if (!level && !code[j + 1].Calls(Disabled)) break;
                    code[i].opcode = OpCodes.Nop; code[i].operand = null;
                    code[j].opcode = OpCodes.Call;
                    code[j].operand = AccessTools.Method(typeof(PrometheusWorkCompatibility),
                        level ? nameof(FixedLevel) : nameof(SkillDisabled));
                    code[j + 1].opcode = OpCodes.Nop; code[j + 1].operand = null;
                    break;
                }
            }
            return code;
        }
    }

    [HarmonyPatch(typeof(WorkGiver_Teach), nameof(WorkGiver_Teach.HasJobOnThing))]
    internal static class PrometheusTeachingAdmission
    {
        private static bool Prefix(Pawn pawn, Thing t, ref bool __result)
        {
            if (!(pawn is Pawn_Prometheus)) return true;
            __result = false;
            if (!ModsConfig.BiotechActive || !(t is Pawn student) || student.skills == null
                || !SchoolUtility.NeedsTeacher(student)) return false;
            var desk = student.CurJob.GetTarget(TargetIndex.A).Thing;
            __result = desk != null && pawn.CanReserveSittableOrSpot(SchoolUtility.DeskSpotTeacher(desk));
            return false;
        }
    }

    [HarmonyPatch(typeof(JobDriver_Lessongiving), "ChooseSkill")]
    internal static class PrometheusLessonSkill
    {
        private static bool Prefix(JobDriver_Lessongiving __instance, Pawn student, ref SkillDef __result)
        {
            if (!(__instance.pawn is Pawn_Prometheus)) return true;
            // All mechanical skills have the same fixed level. Reservoir sampling avoids
            // allocating/sorting a pretend human skill list each time a lesson begins.
            __result = null;
            int eligible = 0;
            foreach (var skill in DefDatabase<SkillDef>.AllDefsListForReading)
                if (skill.lessonInteraction != null && !student.skills.GetSkill(skill).TotallyDisabled
                    && Rand.Range(0, ++eligible) == 0) __result = skill;
            return false;
        }
    }
}
