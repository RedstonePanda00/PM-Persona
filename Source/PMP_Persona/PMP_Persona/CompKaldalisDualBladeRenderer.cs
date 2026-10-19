using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using ProtossMech;

namespace PMP_Persona
{
    // Reuses Umbra blade XML tuning fields from ProtossMech; adds dual-blade layout and two swings per melee.
    public class CompProperties_KaldalisDualBladeRenderer : CompProperties_UmbraWarpBladeRenderer
    {
        // Pawn-local lateral offset magnitude (along right of facing); left = -, right = +.
        public float twinBladeLateralOffset = 0.1f;

        // Constant offset along pawn render facing (same axis as forward swing push); applies idle + attacking.
        public float twinBladeFacingOffset = 0f;

        // Max world slide along diagonal-back while swing curve f<0 (windup); f>=0 (strike/cooldown) stays at origin.
        public float attackDiagonalBackSlideDistance = 0.3f;

        // Degrees from pure lateral (x±) toward backward (+y in pawn frame: facing = -y, so back = -forward world).
        public float attackDiagonalBackSlideAngleDeg = 60f;

        // North/South only: added to base combo yaw; right blade +this, mirrored left blade -this (90° relative between hands vs same-sign).
        public float bladeComboExtraNorthSouthYawDeg = 45f;

        // East/West: final offset for both blades along -bodyForward (behind the pawn in facing space).
        public float eastWestRearDrawOffset = 0.08f;

        public CompProperties_KaldalisDualBladeRenderer()
        {
            compClass = typeof(CompKaldalisDualBladeRenderer);
            bladeTexPath = "Things/Pawn/Mechanoid/KaldalisBlade";
            drawSize = 2.2f;
            baseLayer = 26f;
        }
    }

    public struct KaldalisDualBladePose
    {
        public bool attacking;
        public float swingFactor;
    }

    public static class KaldalisDualBladePoseUtility
    {
        public static bool TryGetPose(Pawn pawn, out KaldalisDualBladePose pose)
        {
            pose = default;
            if (pawn == null)
                return false;

            CompKaldalisDualBladeRenderer comp = pawn.TryGetComp<CompKaldalisDualBladeRenderer>();
            if (comp == null || !comp.IsSwingingNow)
                return false;

            pose.attacking = true;
            pose.swingFactor = comp.SwingFactor01;
            return true;
        }

        public static Vector3 GetAttackDirFlat(Pawn pawn)
        {
            if (pawn == null)
                return Vector3.forward;
            CompKaldalisDualBladeRenderer comp = pawn.TryGetComp<CompKaldalisDualBladeRenderer>();
            if (comp != null && comp.LastAttackDirFlat.sqrMagnitude > 0.0001f)
                return comp.LastAttackDirFlat.normalized;
            Vector3 f = pawn.Rotation.AsIntVec3.ToVector3();
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }
    }

    public class PawnRenderNode_KaldalisBlade : PawnRenderNode
    {
        public Vector3 bladeLocalOffset;
        public float impactYawAngle;
        public float forwardOffsetImpact;
        public float idleSwayYawAngle;
        public float idleSwaySpeed;
        public float lateralOffset;
        public float facingForwardOffset;
        public bool isRightSide;
        public bool mirrorGraphicX;

        public PawnRenderNode_KaldalisBlade(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override Mesh GetMesh(PawnDrawParms parms)
        {
            Mesh mesh = base.GetMesh(parms);
            if (mesh == null)
                return null;
            // Mirror via flipped grid mesh (negative scale inverts winding and can cull one blade).
            if (PawnRenderNodeWorker_KaldalisBlade.EffectiveMirrorGraphicX(this, parms))
                mesh = MeshPool.GridPlaneFlip(mesh);
            return mesh;
        }

        public override Graphic GraphicFor(Pawn pawn)
        {
            string path = Props.texPath;
            if (path.NullOrEmpty())
                return null;
            Shader shader = ShaderFor(pawn);
            if (shader == null)
                return null;
            Vector2 size = Props.overrideMeshSize ?? Props.drawSize;
            return GraphicDatabase.Get<Graphic_Single>(path, shader, size, ColorFor(pawn));
        }
    }

    public class PawnRenderNodeWorker_KaldalisBlade : PawnRenderNodeWorker
    {
        // West: center the left (mirrored) blade; East: center the right blade. Lateral offset suppressed only for that hand.
        internal static bool EastWestBladeUsesBodyCenter(PawnRenderNode_KaldalisBlade blade, Rot4 bodyRot)
        {
            if (!bodyRot.IsHorizontal || blade == null)
                return false;
            if (bodyRot == Rot4.West)
                return !blade.isRightSide;
            if (bodyRot == Rot4.East)
                return blade.isRightSide;
            return false;
        }

        // East: centered (right) hand flips mesh once. West: only the right hand flips once; west left stays mirrored
        // (centering cancels lateral only — do not XOR left mirror or the mesh matches the right asset and reads "facing right").
        internal static bool EffectiveMirrorGraphicX(PawnRenderNode_KaldalisBlade blade, PawnDrawParms parms)
        {
            if (blade == null)
                return false;
            if (parms.pawn == null || !parms.pawn.Rotation.IsHorizontal)
                return blade.mirrorGraphicX;
            Rot4 bodyRot = parms.pawn.Rotation;
            if (bodyRot == Rot4.West)
                return blade.isRightSide ? !blade.mirrorGraphicX : blade.mirrorGraphicX;
            if (EastWestBladeUsesBodyCenter(blade, bodyRot))
                return !blade.mirrorGraphicX;
            return blade.mirrorGraphicX;
        }

        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
                return false;
            return node is PawnRenderNode_KaldalisBlade;
        }

        // Body mesh uses ~0; tuck blades behind torso via small negative layer (see PawnRenderer cached North path).
        // North: both blades; East: left only; West: right only (paired with draw-order patch).
        public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
        {
            if (node is PawnRenderNode_KaldalisBlade blade)
            {
                if (parms.facing == Rot4.North)
                    return -10f;
                if (parms.facing == Rot4.East && !blade.isRightSide)
                    return -10f;
                if (parms.facing == Rot4.West && blade.isRightSide)
                    return -10f;
            }

            return base.LayerFor(node, parms);
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 result = base.OffsetFor(node, parms, out pivot);
            if (!(node is PawnRenderNode_KaldalisBlade blade))
                return result;
            if (parms.pawn == null)
                return result;

            result += blade.bladeLocalOffset;

            // Body rotation for attack / vertical logic; East/West idle lateral + twin facing offset use South-fixed axes
            // so both blades share the same map Z as when facing South (lateral along map X, depth along map Z).
            Vector3 bodyFwd = BodyRotationForwardFlat(parms.pawn);
            Vector3 bodyRight = FlatRight(bodyFwd);
            float side = blade.isRightSide ? 1f : -1f;
            Rot4 bodyRot = parms.pawn.Rotation;
            bool horiz = bodyRot.IsHorizontal;
            Vector3 lateralAxis = horiz ? SouthRightFlat() : bodyRight;
            // North: keep map-south depth rule; South/other vertical: same as body forward; East/West: South forward (map Z).
            Vector3 idleFacingDir = horiz ? SouthForwardFlat() : FacingOffsetWorldDirForPawn(parms.pawn, bodyFwd);
            float lateralMul = EastWestBladeUsesBodyCenter(blade, bodyRot) ? 0f : 1f;
            result += lateralAxis * (blade.lateralOffset * side * lateralMul);
            result += idleFacingDir * blade.facingForwardOffset;

            if (KaldalisDualBladePoseUtility.TryGetPose(parms.pawn, out KaldalisDualBladePose pose) && pose.attacking)
            {
                CompKaldalisDualBladeRenderer comp = parms.pawn.TryGetComp<CompKaldalisDualBladeRenderer>();

                if (bodyRot.IsHorizontal)
                {
                    // East/West: windup opposite facing axis, strike along facing; no diagonal slide / no facing-dir lunge here.
                    if (comp != null)
                    {
                        float windF = comp.DiagonalBackSlideFactorForBlade(blade.isRightSide);
                        float strikeF = comp.ForwardSwingFactorForBlade(blade.isRightSide);
                        result += (-bodyFwd) * (windF * comp.Props.attackDiagonalBackSlideDistance);
                        result += bodyFwd * (strikeF * blade.forwardOffsetImpact);
                    }
                }
                else
                {
                    float fwd = comp != null
                        ? comp.ForwardSwingFactorForBlade(blade.isRightSide)
                        : Mathf.Max(0f, pose.swingFactor);
                    Vector3 strikeFacingDir = FacingOffsetWorldDirForPawn(parms.pawn, bodyFwd);
                    result += strikeFacingDir * (blade.forwardOffsetImpact * fwd);

                    if (comp != null)
                    {
                        float slideF = comp.DiagonalBackSlideFactorForBlade(blade.isRightSide);
                        if (slideF > 0.0001f)
                        {
                            Vector3 slideDir = CompKaldalisDualBladeRenderer.DiagonalBackWorldDir(
                                blade.isRightSide,
                                bodyFwd,
                                bodyRight,
                                comp.Props.attackDiagonalBackSlideAngleDeg);
                            result += slideDir * (slideF * comp.Props.attackDiagonalBackSlideDistance);
                        }
                    }
                }
            }

            // East/West: both hands shift slightly behind the pawn along -facing (after lateral / swing offsets).
            if (horiz)
            {
                CompKaldalisDualBladeRenderer compRear = parms.pawn.TryGetComp<CompKaldalisDualBladeRenderer>();
                float rear = compRear != null ? compRear.Props.eastWestRearDrawOffset : 0f;
                if (rear > 0.0001f)
                    result += (-bodyFwd) * rear;
            }

            return result;
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            Quaternion rotation = base.RotationFor(node, parms);
            if (!(node is PawnRenderNode_KaldalisBlade blade))
                return rotation;
            if (parms.pawn == null)
                return rotation;

            // Per-hand yaw from map facing: only North/South so East/West blades share the same map Z height (no orbit around pawn).
            if (parms.pawn != null && parms.pawn.Rotation.IsVertical)
            {
                CompKaldalisDualBladeRenderer comboComp = parms.pawn.TryGetComp<CompKaldalisDualBladeRenderer>();
                float mag = comboComp != null ? comboComp.Props.bladeComboExtraNorthSouthYawDeg : 45f;
                float extraNs = blade.isRightSide ? mag : -mag;
                rotation *= Quaternion.AngleAxis(-BladeComboClockwiseYawFromSouth(parms.pawn.Rotation, extraNs), Vector3.up);
            }

            if (KaldalisDualBladePoseUtility.TryGetPose(parms.pawn, out KaldalisDualBladePose pose) && pose.attacking)
            {
                // East/West: no impactYaw spin; translation-only swing is handled in OffsetFor.
                if (parms.pawn != null && !parms.pawn.Rotation.IsHorizontal)
                {
                    CompKaldalisDualBladeRenderer comp = parms.pawn.TryGetComp<CompKaldalisDualBladeRenderer>();
                    float f = comp != null ? comp.SignedSwingFactorForBlade(blade.isRightSide) : pose.swingFactor;
                    // Body-relative only: no attackDir / mapNorth / cross-target sign (avoids stale LastAttackDir flipping EW vs NS).
                    // Use -f for yaw so the blade arcs read as slash (outward) not elbow-in; slide still uses raw f<0 in DiagonalBackSlideFactorForBlade.
                    float handSign = blade.isRightSide ? 1f : -1f;
                    float angle = blade.impactYawAngle * (-f) * handSign;
                    rotation *= Quaternion.AngleAxis(angle, Vector3.up);
                }

                return rotation;
            }

            int ticks = Find.TickManager.TicksGame;
            float phase = (parms.pawn.thingIDNumber % 1000) * 0.01f;
            float time = (ticks + phase) * blade.idleSwaySpeed;
            float idleAngle = Mathf.Sin(time) * blade.idleSwayYawAngle;
            rotation *= Quaternion.AngleAxis(idleAngle, Vector3.up);
            return rotation;
        }

        // South (map -Z) 0°, East (+X) 90°, North (+Z) 180°, West (-X) 270° clockwise, plus optional extra (e.g. N/S perpendicular tweak).
        private static float BladeComboClockwiseYawFromSouth(Rot4 bodyRot, float extraClockwiseDeg = 0f)
        {
            if (!bodyRot.IsValid)
                return 0f;
            return GenMath.PositiveMod(2 - bodyRot.AsInt, 4) * 90f + extraClockwiseDeg;
        }

        private static Vector3 BodyRotationForwardFlat(Pawn pawn)
        {
            if (pawn == null)
                return Vector3.forward;
            Vector3 f = pawn.Rotation.AsIntVec3.ToVector3();
            f.y = 0f;
            if (f.sqrMagnitude < 0.0001f)
                f = Vector3.forward;
            return f.normalized;
        }

        // Fixed world axes as if the pawn faced South: used for East/West lateral + idle forward offset so both blades
        // stay on the same map Z as in the South-facing layout (offsets no longer rotate with East/West body forward).
        private static Vector3 SouthForwardFlat()
        {
            Vector3 f = Rot4.South.AsIntVec3.ToVector3();
            f.y = 0f;
            if (f.sqrMagnitude < 0.0001f)
                return new Vector3(0f, 0f, -1f);
            return f.normalized;
        }

        private static Vector3 SouthRightFlat()
        {
            return FlatRight(SouthForwardFlat());
        }

        // twinBladeFacingOffset / forward lunge: map South (-Z) when pawn faces North; else along body forward.
        private static Vector3 FacingOffsetWorldDirForPawn(Pawn pawn, Vector3 bodyFwdFlat)
        {
            if (pawn != null && pawn.Rotation == Rot4.North)
            {
                Vector3 south = Rot4.South.AsIntVec3.ToVector3();
                south.y = 0f;
                if (south.sqrMagnitude > 0.0001f)
                    return south.normalized;
                return new Vector3(0f, 0f, -1f);
            }

            return bodyFwdFlat;
        }

        private static Vector3 FlatRight(Vector3 flatForward)
        {
            return Vector3.Cross(Vector3.up, flatForward).normalized;
        }
    }

    [HarmonyPatch]
    public static class Patch_KaldalisDualBlade_OnMeleeStart
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.FirstMethod(typeof(Verb), m =>
                m.Name == "TryStartCastOn"
                && m.ReturnType == typeof(bool)
                && m.GetParameters().Length == 5
                && m.GetParameters()[0].ParameterType == typeof(LocalTargetInfo)
                && m.GetParameters()[1].ParameterType == typeof(bool)
                && m.GetParameters()[2].ParameterType == typeof(bool)
                && m.GetParameters()[3].ParameterType == typeof(bool)
                && m.GetParameters()[4].ParameterType == typeof(bool)
            );
        }

        public static void Postfix(Verb __instance,
            LocalTargetInfo castTarg,
            bool surpriseAttack,
            bool canHitNonTargetPawns,
            bool preventFriendlyFire,
            bool nonInterruptingSelfCast,
            bool __result)
        {
            if (!__result || __instance == null || !(__instance is Verb_MeleeAttack melee))
                return;
            if (melee.verbProps == null || !melee.verbProps.IsMeleeAttack)
                return;

            Pawn pawn = melee.CasterPawn;
            if (pawn == null)
                return;

            CompKaldalisDualBladeRenderer comp = pawn.TryGetComp<CompKaldalisDualBladeRenderer>();
            if (comp == null)
                return;

            comp.Notify_MeleeAttackStarted(melee, castTarg);
        }
    }

    public class CompKaldalisDualBladeRenderer : ThingComp
    {
        public CompProperties_KaldalisDualBladeRenderer Props => (CompProperties_KaldalisDualBladeRenderer)props;

        private int swingTicksLeft;
        private int swingTotalTicks = 1;

        private bool bladeDrawOrderRotCached;
        private byte bladeDrawOrderRotByte = byte.MaxValue;

        public bool IsSwingingNow => swingTicksLeft > 0;

        public Vector3 LastAttackDirFlat { get; private set; } = Vector3.forward;

        // 0..1 strike intensity (whichever blade is striking this tick).
        public float SwingFactor01 => ForwardSwingFactor();

        // Left blade (isRightSide false) animates in the first half of the cycle; right blade in the second half.
        public float SignedSwingFactorForBlade(bool isRightSide)
        {
            if (swingTotalTicks <= 0 || swingTicksLeft <= 0)
                return 0f;

            int elapsed = swingTotalTicks - swingTicksLeft;
            int leftSegmentTicks = (swingTotalTicks + 1) / 2;
            int rightSegmentTicks = swingTotalTicks - leftSegmentTicks;

            if (!isRightSide)
            {
                if (elapsed < leftSegmentTicks)
                    return SignedSwingFactorSingleSegment(elapsed, leftSegmentTicks, Props);
                return 0f;
            }

            if (elapsed < leftSegmentTicks)
                return 0f;
            return SignedSwingFactorSingleSegment(elapsed - leftSegmentTicks, rightSegmentTicks, Props);
        }

        public float ForwardSwingFactorForBlade(bool isRightSide)
        {
            return Mathf.Max(0f, SignedSwingFactorForBlade(isRightSide));
        }

        // 0..1 from swing curve: full slide at deepest windup (f == -swingPrewindAngleFactor); 0 once f>=0 (strike/cooldown).
        public float DiagonalBackSlideFactorForBlade(bool isRightSide)
        {
            if (swingTotalTicks <= 0 || swingTicksLeft <= 0)
                return 0f;

            float f = SignedSwingFactorForBlade(isRightSide);
            if (f >= 0f)
                return 0f;

            float preAmt = Mathf.Clamp01(Props.swingPrewindAngleFactor);
            if (preAmt < 0.0001f)
                return 0f;

            return Mathf.Clamp01((-f) / preAmt);
        }

        // Pawn frame: facing = -y, so backward = +y -> world -forward. Left: -x toward +y at angleDeg; right: +x toward +y.
        public static Vector3 DiagonalBackWorldDir(bool isRightSide, Vector3 bodyFwdFlat, Vector3 bodyRightFlat, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad);
            float s = Mathf.Sin(rad);
            Vector3 lateral = isRightSide ? bodyRightFlat : -bodyRightFlat;
            Vector3 back = -bodyFwdFlat;
            Vector3 v = lateral * c + back * s;
            return v.sqrMagnitude > 0.0001f ? v.normalized : back;
        }

        private static float SignedSwingFactorSingleSegment(int elapsed, int segmentTotalTicks, CompProperties_UmbraWarpBladeRenderer p)
        {
            if (segmentTotalTicks <= 0)
                return 0f;

            float ratio = Mathf.Clamp(p.swingWindupRatio, 0.15f, 0.85f);
            int warmupTicks = Mathf.Clamp(Mathf.RoundToInt(segmentTotalTicks * ratio), 1, Mathf.Max(1, segmentTotalTicks - 1));
            int cooldownTicks = Mathf.Max(1, segmentTotalTicks - warmupTicks);

            if (elapsed <= warmupTicks)
            {
                int warm = Mathf.Max(1, warmupTicks);
                float t = Mathf.Clamp01((float)elapsed / warm);

                float preR = Mathf.Clamp(p.swingPrewindRatioOfWarmup, 0.05f, 0.8f);
                float holdR = Mathf.Clamp(p.swingHoldRatioOfWarmup, 0f, 0.5f);
                float preEnd = Mathf.Clamp01(preR);
                float holdEnd = Mathf.Clamp01(preEnd + holdR);
                float preAmt = Mathf.Clamp01(p.swingPrewindAngleFactor);

                if (t <= preEnd)
                {
                    float u = preEnd > 0.0001f ? t / preEnd : 1f;
                    u = Mathf.SmoothStep(0f, 1f, u);
                    return -preAmt * u;
                }

                if (t <= holdEnd)
                    return -preAmt;

                float strikeSpan = Mathf.Max(0.0001f, 1f - holdEnd);
                float v = (t - holdEnd) / strikeSpan;
                float exp = Mathf.Max(0.3f, p.swingStrikeExponent);
                float eased = 1f - Mathf.Pow(1f - v, exp);
                return Mathf.Lerp(-preAmt, 1f, Mathf.Clamp01(eased));
            }

            int cdElapsed = elapsed - warmupTicks;
            int cd = Mathf.Max(1, cooldownTicks);
            float cdt = Mathf.Clamp01((float)cdElapsed / cd);
            return Mathf.SmoothStep(1f, 0f, cdt);
        }

        public float ForwardSwingFactor()
        {
            return Mathf.Max(ForwardSwingFactorForBlade(false), ForwardSwingFactorForBlade(true));
        }

        public override List<PawnRenderNode> CompRenderNodes()
        {
            if (!(parent is Pawn pawn) || pawn.Drawer?.renderer?.renderTree == null || !pawn.Spawned)
                return null;

            PawnRenderNodeProperties p = new PawnRenderNodeProperties
            {
                workerClass = typeof(PawnRenderNodeWorker_KaldalisBlade),
                nodeClass = typeof(PawnRenderNode_KaldalisBlade),
                texPath = Props.bladeTexPath,
                overrideMeshSize = new Vector2(Props.drawSize, Props.drawSize),
                baseLayer = Props.baseLayer,
                pawnType = PawnRenderNodeProperties.RenderNodePawnType.Any
            };

            PawnRenderNode_KaldalisBlade right = new PawnRenderNode_KaldalisBlade(pawn, p, pawn.Drawer.renderer.renderTree);
            right.bladeLocalOffset = Props.bladeLocalOffset;
            right.impactYawAngle = Props.impactYawAngle;
            right.forwardOffsetImpact = Props.forwardOffsetImpact;
            right.idleSwayYawAngle = Props.idleSwayYawAngle;
            right.idleSwaySpeed = Props.idleSwaySpeed;
            right.lateralOffset = Props.twinBladeLateralOffset;
            right.facingForwardOffset = Props.twinBladeFacingOffset;
            right.isRightSide = true;
            right.mirrorGraphicX = false;

            PawnRenderNode_KaldalisBlade left = new PawnRenderNode_KaldalisBlade(pawn, p, pawn.Drawer.renderer.renderTree);
            left.bladeLocalOffset = Props.bladeLocalOffset;
            left.impactYawAngle = Props.impactYawAngle;
            left.forwardOffsetImpact = Props.forwardOffsetImpact;
            left.idleSwayYawAngle = Props.idleSwayYawAngle;
            left.idleSwaySpeed = Props.idleSwaySpeed;
            left.lateralOffset = Props.twinBladeLateralOffset;
            left.facingForwardOffset = Props.twinBladeFacingOffset;
            left.isRightSide = false;
            left.mirrorGraphicX = true;
            left.debugLayerOffset = 0.01f;

            return new List<PawnRenderNode> { right, left };
        }

        public override void CompTick()
        {
            base.CompTick();
            if (!(parent is Pawn pawn) || !pawn.Spawned)
                return;

            byte rotB = pawn.Rotation.AsByte;
            if (bladeDrawOrderRotCached && bladeDrawOrderRotByte != rotB)
                pawn.Drawer?.renderer?.renderTree?.SetDirty();
            bladeDrawOrderRotCached = true;
            bladeDrawOrderRotByte = rotB;

            if (swingTicksLeft > 0)
            {
                swingTicksLeft--;
                pawn.Drawer?.renderer?.renderTree?.SetDirty();
            }
        }

        public void Notify_MeleeAttackStarted(Verb verb, LocalTargetInfo castTarg)
        {
            Pawn pawn = parent as Pawn;
            float fullCycleSeconds = 0f;
            if (pawn != null && verb?.verbProps != null)
                fullCycleSeconds = verb.verbProps.AdjustedFullCycleTime(verb, pawn);

            int totalTicks = fullCycleSeconds > 0.001f ? fullCycleSeconds.SecondsToTicks() : 0;
            totalTicks = Mathf.Max(Props.minSwingTicks, totalTicks);
            swingTotalTicks = Mathf.Max(1, totalTicks);
            swingTicksLeft = swingTotalTicks;

            if (pawn != null)
            {
                Vector3 targetPos = castTarg.HasThing ? castTarg.Thing.DrawPos : castTarg.Cell.ToVector3Shifted();
                Vector3 dir = targetPos - pawn.DrawPos;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0001f)
                    LastAttackDirFlat = dir.normalized;
                else
                {
                    Vector3 f = pawn.Rotation.AsIntVec3.ToVector3();
                    f.y = 0f;
                    LastAttackDirFlat = f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
                }
            }

            (parent as Pawn)?.Drawer?.renderer?.renderTree?.SetDirty();
        }
    }
}
