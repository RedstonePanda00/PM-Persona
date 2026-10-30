using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona.TacticalSprint
{
    public sealed class CompProperties_TacticalSprint : CompProperties
    {
        public float speedMultiplier = 1.4f;
        public float diveChance = .05f;
        public float leanDegrees = 12f;
        public float runBob = .035f;
        public float vaultHeight = .32f;
        public float vaultMinFill = .3f;
        public CompProperties_TacticalSprint() { compClass = typeof(CompTacticalSprint); }
    }

    // Human is shared by many PawnKinds; this extension opts an individual kind into the comp.
    public sealed class TacticalSprintExtension : DefModExtension
    {
        public CompProperties_TacticalSprint settings = new CompProperties_TacticalSprint();
    }

    public enum SprintTraversal { None, Slide, Dive, Vault }

    [StaticConstructorOnStartup]
    internal static class SprintTextures
    {
        internal static readonly Texture2D Toggle = ContentFinder<Texture2D>.Get("UI/Commands/TacticalSprint");
    }

    public sealed class CompTacticalSprint : ThingComp
    {
        private bool enabled;
        private IntVec3 stepFrom = IntVec3.Invalid, stepTo = IntVec3.Invalid;
        private SprintTraversal traversal;
        private bool extinguishingDive;
        private int strideTicks;
        public CompProperties_TacticalSprint Settings => (CompProperties_TacticalSprint)props;
        public Pawn Pawn => (Pawn)parent;
        public bool Enabled => enabled;
        public bool Active => enabled && Pawn.Spawned && Pawn.Drafted && !Pawn.Dead && !Pawn.Downed &&
            !Pawn.InMentalState && !Pawn.Swimming && Pawn.GetPosture() == PawnPosture.Standing &&
            Pawn.carryTracker?.CarriedThing == null && Pawn.CarriedBy == null;
        public bool Moving => Active && Pawn.pather?.MovingNow == true && Pawn.stances?.FullBodyBusy != true;
        public SprintTraversal Traversal => Moving && Pawn.Position == stepFrom && Pawn.pather.nextCell == stepTo
            ? traversal : SprintTraversal.None;

        public void SetEnabled(bool value)
        {
            enabled = value;
            if (!value) ClearStep();
        }

        private void ClearStep()
        {
            traversal = SprintTraversal.None;
            extinguishingDive = false;
            stepFrom = stepTo = IntVec3.Invalid;
        }

        public override void CompTick()
        {
            if (Moving) strideTicks = (strideTicks + 1) % 3600;
            else { strideTicks = 0; ClearStep(); }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            ClearStep();
            base.PostDeSpawn(map, mode);
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref enabled, "pmpTacticalSprintEnabled", false);
            Scribe_Values.Look(ref stepFrom, "pmpSprintFrom", IntVec3.Invalid);
            Scribe_Values.Look(ref stepTo, "pmpSprintTo", IntVec3.Invalid);
            Scribe_Values.Look(ref traversal, "pmpSprintTraversal", SprintTraversal.None);
            Scribe_Values.Look(ref extinguishingDive, "pmpSprintExtinguishingDive", false);
            Scribe_Values.Look(ref strideTicks, "pmpSprintStride", 0);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Pawn.Faction != Faction.OfPlayer || !Pawn.Drafted) yield break;
            yield return new Command_Toggle {
                defaultLabel = "PMP.Sprint.Label".Translate(),
                defaultDesc = "PMP.Sprint.Description".Translate((Settings.speedMultiplier - 1f).ToStringPercent()),
                icon = SprintTextures.Toggle,
                isActive = () => enabled,
                toggleAction = () => SetEnabled(!enabled)
            };
        }

        public Thing ObstacleAt(IntVec3 cell)
        {
            if (!cell.InBounds(Pawn.Map) || !cell.WalkableBy(Pawn.Map, Pawn) || cell.GetDoor(Pawn.Map) != null)
                return null;
            Thing best = null;
            foreach (Thing thing in cell.GetThingList(Pawn.Map))
            {
                if (thing.def.pathCost <= 0 || thing.def.passability == Traversability.Impassable || thing is Building_Door ||
                    !(thing is Building || thing is Plant || thing.def.category == ThingCategory.Item))
                    continue;
                if (best == null || thing.def.pathCost > best.def.pathCost) best = thing;
            }
            return best;
        }

        public void BeginStep()
        {
            ClearStep();
            if (!Moving || !Pawn.pather.nextCell.IsValid || Pawn.pather.nextCell == Pawn.Position) return;
            // Only the final moving step commits a fire dive; cancelling a path never extinguishes.
            if (Pawn.IsBurning() && Pawn.pather.nextCell == Pawn.pather.Destination.Cell) {
                stepFrom = Pawn.Position; stepTo = Pawn.pather.nextCell;
                traversal = SprintTraversal.Dive; extinguishingDive = true; return;
            }
            Thing obstacle = ObstacleAt(Pawn.pather.nextCell);
            if (obstacle == null) return;
            stepFrom = Pawn.Position;
            stepTo = Pawn.pather.nextCell;
            // Waist-high cover gets a vault; low obstructions get predominantly slides.
            traversal = obstacle is Building && obstacle.def.fillPercent >= Settings.vaultMinFill
                ? SprintTraversal.Vault
                : (Rand.Chance(Mathf.Clamp01(Settings.diveChance)) ? SprintTraversal.Dive : SprintTraversal.Slide);
        }

        public void CompleteStep(IntVec3 previousPosition) {
            if (!extinguishingDive || previousPosition != stepFrom || Pawn.Position != stepTo || !Active) return;
            var fire = Pawn.GetAttachment(ThingDefOf.Fire) as Fire;
            // Native extinguish damage removes only the attached fire, not fires in the destination cell.
            fire?.TakeDamage(new DamageInfo(DamageDefOf.Extinguish, 1000f, instigator: Pawn));
            extinguishingDive = false;
        }

        // Render-only pose. No downed state, health, hitbox, job or logical position is changed.
        public bool Pose(ref Vector3 location, ref float angle, ref Rot4 facing, out Vector3 scale)
        {
            scale = Vector3.one;
            if (!Moving) return false;
            IntVec3 delta = Pawn.pather.nextCell - Pawn.Position;
            if (delta.x == 0 && delta.z == 0) return false;
            float heading = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            Vector3 forward = new Vector3(delta.x, 0f, delta.z).normalized;
            float phase = strideTicks * .65f;
            float lean = delta.x == 0 ? 0f : Math.Sign(delta.x) * Settings.leanDegrees;
            angle += lean;
            location += forward * .035f;
            location.z += Mathf.Abs(Mathf.Sin(phase)) * Settings.runBob;
            scale.z = delta.x == 0 ? .96f : 1f;
            SprintTraversal action = Traversal;
            if (action == SprintTraversal.None) return true;
            float t = Mathf.Clamp01(Pawn.pather.MovePercentage);
            float envelope = Mathf.Sin(t * Mathf.PI);
            if (action == SprintTraversal.Vault)
            {
                location.z += envelope * Settings.vaultHeight;
                angle += (delta.x == 0 ? 1f : Math.Sign(delta.x)) * 30f * envelope;
                scale.z = 1f - .25f * envelope;
            }
            else
            {
                // Select directional body/head/apparel meshes before applying the prone rotation.
                // A rotated south-facing sprite is not an east/west-facing render tree.
                facing = delta.x > 0 ? Rot4.East : delta.x < 0 ? Rot4.West : delta.z > 0 ? Rot4.North : Rot4.South;
                float prone = action == SprintTraversal.Dive ? heading : heading + 180f;
                angle = Mathf.LerpAngle(lean, prone, Mathf.Clamp01(envelope * 2f));
                scale.z = 1f - .22f * envelope;
                location.z += action == SprintTraversal.Dive ? .16f * envelope : -.08f * envelope;
            }
            return true;
        }
    }

    public sealed class StatPart_TacticalSprint : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            var comp = (req.Thing as Pawn)?.GetComp<CompTacticalSprint>();
            if (comp?.Active == true) val *= comp.Settings.speedMultiplier;
        }
        public override string ExplanationPart(StatRequest req)
        {
            var comp = (req.Thing as Pawn)?.GetComp<CompTacticalSprint>();
            return comp?.Active == true ? "PMP.Sprint.Stat".Translate(comp.Settings.speedMultiplier.ToStringPercent()).ToString() : null;
        }
    }

    internal static class SprintAttachment
    {
        private static readonly AccessTools.FieldRef<ThingWithComps, List<ThingComp>> Comps =
            AccessTools.FieldRefAccess<ThingWithComps, List<ThingComp>>("comps");
        public static CompTacticalSprint Ensure(Pawn pawn)
        {
            if (pawn == null) return null;
            var existing = pawn.GetComp<CompTacticalSprint>();
            if (existing != null) return existing;
            var extension = pawn.kindDef?.GetModExtension<TacticalSprintExtension>();
            if (extension?.settings == null) return null;
            var list = Comps(pawn);
            if (list == null) Comps(pawn) = list = new List<ThingComp>();
            var comp = new CompTacticalSprint { parent = pawn };
            comp.Initialize(extension.settings);
            list.Add(comp);
            // Prime the native type index on the main thread before parallel pawn drawing.
            return pawn.GetComp<CompTacticalSprint>();
        }
    }

    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class SprintGeneration
    {
        static void Postfix(Pawn __result) => SprintAttachment.Ensure(__result);
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExposeData))]
    internal static class SprintSave
    {
        static void Postfix(Pawn __instance)
        {
            // Pawn loads kindDef after ThingWithComps has loaded ordinary race comps.
            // Read this kind-specific comp once, now that its configuration is available.
            if (Scribe.mode == LoadSaveMode.LoadingVars && __instance.GetComp<CompTacticalSprint>() == null)
                SprintAttachment.Ensure(__instance)?.PostExposeData();
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.SpawnSetup))]
    internal static class SprintSpawn
    {
        static void Postfix(Pawn __instance) => SprintAttachment.Ensure(__instance);
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "SetupMoveIntoNextCell")]
    internal static class SprintStep
    {
        static void Postfix(Pawn ___pawn) => ___pawn.GetComp<CompTacticalSprint>()?.BeginStep();
    }
    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    internal static class SprintLanding {
        static void Prefix(Pawn ___pawn,out IntVec3 __state)=>__state=___pawn.Position;
        static void Postfix(Pawn ___pawn,IntVec3 __state)=>___pawn.GetComp<CompTacticalSprint>()?.CompleteStep(__state);
    }
    [HarmonyPatch(typeof(PawnRenderer), "ParallelGetPreRenderResults")]
    internal static class SprintRenderCache
    {
        static void Prefix(Pawn ___pawn, ref bool disableCache)
        {
            if (___pawn.GetComp<CompTacticalSprint>()?.Moving == true) disableCache = true;
        }
    }
    [HarmonyPatch(typeof(PawnRenderer), "GetDrawParms")]
    internal static class SprintDrawing
    {
        static void Postfix(Pawn ___pawn, ref PawnDrawParms __result)
        {
            if (__result.flags.FlagSet(PawnRenderFlags.Portrait) || __result.flags.FlagSet(PawnRenderFlags.Cache)) return;
            var comp = ___pawn.GetComp<CompTacticalSprint>();
            if (comp?.Moving != true) return;
            Vector3 origin = __result.matrix.MultiplyPoint3x4(Vector3.zero);
            Vector3 moved = origin; float angle = 0f; Rot4 facing = __result.facing;
            if (!comp.Pose(ref moved, ref angle, ref facing, out Vector3 scale)) return;
            __result.facing = facing;
            __result.matrix = Matrix4x4.TRS(moved, Quaternion.AngleAxis(angle, Vector3.up), scale) *
                Matrix4x4.Translate(-origin) * __result.matrix;
        }
    }
}
