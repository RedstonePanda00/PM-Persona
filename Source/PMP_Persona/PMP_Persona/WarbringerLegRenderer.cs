using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    public class CompProperties_WarbringerLegRenderer : CompProperties
    {
        public float drawSize = 5f;
        public float baseLayer = -4f;
        public int gaitCycleTicks = 48;
        public float swingRatio = 0.38f;
        public float strideLength = 0.34f;
        public float swingOutwardOffset = 0.08f;
        public float toeSwingAngle = 10f;

        public CompProperties_WarbringerLegRenderer()
        {
            compClass = typeof(CompWarbringerLegRenderer);
        }
    }

    internal enum WarbringerLegSegment
    {
        Main,
        Joint,
        Strut
    }

    internal sealed class WarbringerLegChain
    {
        public readonly string debugName;
        public readonly bool front;
        public readonly bool right;
        public readonly float phaseOffset;
        public readonly Vector2 hipPixel;
        public readonly Vector2 kneePixel;
        public readonly Vector2 anklePixel;
        public readonly Vector2 footPixel;

        public WarbringerLegChain(
            string debugName,
            bool front,
            bool right,
            float phaseOffset,
            Vector2 hipPixel,
            Vector2 kneePixel,
            Vector2 anklePixel,
            Vector2 footPixel)
        {
            this.debugName = debugName;
            this.front = front;
            this.right = right;
            this.phaseOffset = phaseOffset;
            this.hipPixel = hipPixel;
            this.kneePixel = kneePixel;
            this.anklePixel = anklePixel;
            this.footPixel = footPixel;
        }

        public Vector3 Hip(float drawSize)
        {
            return WarbringerLegKinematics.PixelToLocal(hipPixel, drawSize);
        }

        public Vector3 Knee(float drawSize)
        {
            return WarbringerLegKinematics.PixelToLocal(kneePixel, drawSize);
        }

        public Vector3 Ankle(float drawSize)
        {
            return WarbringerLegKinematics.PixelToLocal(anklePixel, drawSize);
        }

        public Vector3 Foot(float drawSize)
        {
            return WarbringerLegKinematics.PixelToLocal(footPixel, drawSize);
        }
    }

    internal sealed class WarbringerLegPartSpec
    {
        public readonly WarbringerLegChain leg;
        public readonly WarbringerLegSegment segment;
        public readonly string texPath;
        public readonly float layerOffset;
        public readonly string debugLabel;

        public WarbringerLegPartSpec(
            WarbringerLegChain leg,
            WarbringerLegSegment segment,
            string texPath,
            float layerOffset)
        {
            this.leg = leg;
            this.segment = segment;
            this.texPath = texPath;
            this.layerOffset = layerOffset;
            debugLabel = "WarbringerLeg." + leg.debugName + "." + segment;
        }

        public Vector3 SourceStart(float drawSize)
        {
            switch (segment)
            {
                case WarbringerLegSegment.Joint:
                    return leg.Hip(drawSize);
                case WarbringerLegSegment.Main:
                    return leg.Knee(drawSize);
                default:
                    return leg.Ankle(drawSize);
            }
        }

        public Vector3 SourceEnd(float drawSize)
        {
            switch (segment)
            {
                case WarbringerLegSegment.Joint:
                    return leg.Knee(drawSize);
                case WarbringerLegSegment.Main:
                    return leg.Ankle(drawSize);
                default:
                    return leg.Foot(drawSize);
            }
        }

        public Vector3 TargetStart(WarbringerLegPose pose)
        {
            switch (segment)
            {
                case WarbringerLegSegment.Joint:
                    return pose.hip;
                case WarbringerLegSegment.Main:
                    return pose.knee;
                default:
                    return pose.ankle;
            }
        }

        public Vector3 TargetEnd(WarbringerLegPose pose)
        {
            switch (segment)
            {
                case WarbringerLegSegment.Joint:
                    return pose.knee;
                case WarbringerLegSegment.Main:
                    return pose.ankle;
                default:
                    return pose.foot;
            }
        }
    }

    internal struct WarbringerLegPose
    {
        public Vector3 hip;
        public Vector3 knee;
        public Vector3 ankle;
        public Vector3 foot;
    }

    internal static class WarbringerLegCatalog
    {
        public static readonly WarbringerLegChain FrontRight = new WarbringerLegChain(
            "FrontRight",
            true,
            true,
            0.5f,
            new Vector2(376f, 442f),
            new Vector2(469f, 414f),
            new Vector2(492f, 599f),
            new Vector2(444f, 665f));

        public static readonly WarbringerLegChain FrontLeft = new WarbringerLegChain(
            "FrontLeft",
            true,
            false,
            0f,
            new Vector2(324f, 442f),
            new Vector2(231f, 414f),
            new Vector2(208f, 599f),
            new Vector2(256f, 665f));

        public static readonly WarbringerLegChain BackRight = new WarbringerLegChain(
            "BackRight",
            false,
            true,
            0f,
            new Vector2(376f, 400f),
            new Vector2(467f, 373f),
            new Vector2(462f, 576f),
            new Vector2(416f, 650f));

        public static readonly WarbringerLegChain BackLeft = new WarbringerLegChain(
            "BackLeft",
            false,
            false,
            0.5f,
            new Vector2(324f, 400f),
            new Vector2(233f, 373f),
            new Vector2(238f, 576f),
            new Vector2(284f, 650f));

        public static readonly List<WarbringerLegPartSpec> Parts = new List<WarbringerLegPartSpec>
        {
            new WarbringerLegPartSpec(
                FrontRight,
                WarbringerLegSegment.Main,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Front/Front_01_RightMain",
                0.03f),
            new WarbringerLegPartSpec(
                FrontRight,
                WarbringerLegSegment.Joint,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Front/Front_05_RightJoint",
                0.04f),
            new WarbringerLegPartSpec(
                FrontRight,
                WarbringerLegSegment.Strut,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Front/Front_06_RightStrut",
                0.05f),

            new WarbringerLegPartSpec(
                FrontLeft,
                WarbringerLegSegment.Main,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Front/Front_04_LeftMain",
                0.03f),
            new WarbringerLegPartSpec(
                FrontLeft,
                WarbringerLegSegment.Joint,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Front/Front_02_LeftJoint",
                0.04f),
            new WarbringerLegPartSpec(
                FrontLeft,
                WarbringerLegSegment.Strut,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Front/Front_03_LeftStrut",
                0.05f),

            new WarbringerLegPartSpec(
                BackRight,
                WarbringerLegSegment.Main,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Back/Back_01_RightMain",
                0f),
            new WarbringerLegPartSpec(
                BackRight,
                WarbringerLegSegment.Joint,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Back/Back_05_RightJoint",
                0.01f),
            new WarbringerLegPartSpec(
                BackRight,
                WarbringerLegSegment.Strut,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Back/Back_06_RightStrut",
                0.02f),

            new WarbringerLegPartSpec(
                BackLeft,
                WarbringerLegSegment.Main,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Back/Back_04_LeftMain",
                0f),
            new WarbringerLegPartSpec(
                BackLeft,
                WarbringerLegSegment.Joint,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Back/Back_02_LeftJoint",
                0.01f),
            new WarbringerLegPartSpec(
                BackLeft,
                WarbringerLegSegment.Strut,
                "Things/Pawn/Mechanoid/WarBringer/Legs/Back/Back_03_LeftStrut",
                0.02f)
        };
    }

    internal static class WarbringerLegKinematics
    {
        private const float CanvasPixels = 700f;

        public static Vector3 PixelToLocal(Vector2 pixel, float drawSize)
        {
            return new Vector3(
                (pixel.x - CanvasPixels * 0.5f) / CanvasPixels * drawSize,
                0f,
                (CanvasPixels * 0.5f - pixel.y) / CanvasPixels * drawSize);
        }

        public static WarbringerLegPose PoseFor(
            Pawn pawn,
            WarbringerLegChain leg,
            CompProperties_WarbringerLegRenderer props,
            PawnDrawParms parms)
        {
            float drawSize = SafeDrawSize(props);
            WarbringerLegPose pose = new WarbringerLegPose
            {
                hip = leg.Hip(drawSize),
                knee = leg.Knee(drawSize),
                ankle = leg.Ankle(drawSize),
                foot = leg.Foot(drawSize)
            };

            if (parms.flags.FlagSet(PawnRenderFlags.Portrait) || !IsMovingNow(pawn))
                return pose;

            Vector3 moveDir = MovementDirection(pawn);
            if (moveDir.sqrMagnitude < 0.0001f)
                return pose;

            moveDir.Normalize();
            Vector3 sideDir = new Vector3(moveDir.z, 0f, -moveDir.x);
            float sideSign = leg.right ? 1f : -1f;
            int cycleTicks = Mathf.Max(1, props != null ? props.gaitCycleTicks : 48);
            float swingRatio = Mathf.Clamp(props != null ? props.swingRatio : 0.38f, 0.1f, 0.9f);
            float strideLength = Mathf.Max(0f, props != null ? props.strideLength : 0.34f);
            float outwardOffset = Mathf.Max(0f, props != null ? props.swingOutwardOffset : 0.08f);
            float phase = GaitPhase01(pawn, leg, cycleTicks);
            bool swing = phase < swingRatio;
            float along;
            float outward;
            float toeAngle;

            if (swing)
            {
                float t = Mathf.Clamp01(phase / swingRatio);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                along = Mathf.Lerp(-strideLength * 0.5f, strideLength * 0.5f, eased);
                outward = Mathf.Sin(t * Mathf.PI) * outwardOffset * sideSign;
                toeAngle = Mathf.Sin(t * Mathf.PI) * (props != null ? props.toeSwingAngle : 10f) * sideSign;
            }
            else
            {
                float t = Mathf.Clamp01((phase - swingRatio) / Mathf.Max(0.001f, 1f - swingRatio));
                float eased = Mathf.SmoothStep(0f, 1f, t);
                along = Mathf.Lerp(strideLength * 0.5f, -strideLength * 0.5f, eased);
                outward = 0f;
                toeAngle = 0f;
            }

            Vector3 desiredFoot = pose.foot + moveDir * along + sideDir * outward;
            Vector3 defaultStrut = pose.foot - pose.ankle;
            float strutLength = defaultStrut.magnitude;
            Vector3 strutDir = RotateAroundY(NormalizedOrFallback(defaultStrut, moveDir), toeAngle);
            Vector3 desiredAnkleTarget = desiredFoot - strutDir * strutLength;

            SolveTwoBone(
                pose.hip,
                desiredAnkleTarget,
                (pose.knee - pose.hip).magnitude,
                (pose.ankle - pose.knee).magnitude,
                pose.knee,
                out pose.knee,
                out pose.ankle);

            pose.foot = pose.ankle + strutDir * strutLength;
            return pose;
        }

        public static float SegmentRotationDeltaDeg(
            Vector3 sourceStart,
            Vector3 sourceEnd,
            Vector3 targetStart,
            Vector3 targetEnd)
        {
            Vector3 source = sourceEnd - sourceStart;
            Vector3 target = targetEnd - targetStart;
            source.y = 0f;
            target.y = 0f;
            if (source.sqrMagnitude < 0.0001f || target.sqrMagnitude < 0.0001f)
                return 0f;

            source.Normalize();
            target.Normalize();
            float dot = Mathf.Clamp(Vector3.Dot(source, target), -1f, 1f);
            float cross = source.z * target.x - source.x * target.z;
            return Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
        }

        private static bool IsMovingNow(Pawn pawn)
        {
            return pawn != null && pawn.pather != null && pawn.pather.MovingNow;
        }

        private static float SafeDrawSize(CompProperties_WarbringerLegRenderer props)
        {
            return Mathf.Max(0.01f, props != null ? props.drawSize : 5f);
        }

        private static float GaitPhase01(Pawn pawn, WarbringerLegChain leg, int cycleTicks)
        {
            int ticks = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            float desync = pawn != null ? (pawn.thingIDNumber % cycleTicks) : 0f;
            float raw = (ticks + desync) / cycleTicks + leg.phaseOffset;
            return raw - Mathf.Floor(raw);
        }

        private static Vector3 MovementDirection(Pawn pawn)
        {
            if (pawn == null || pawn.pather == null)
                return Vector3.zero;

            Vector3 dir = pawn.pather.nextCell.ToVector3Shifted() - pawn.DrawPos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                return dir;

            dir = pawn.pather.Destination.Cell.ToVector3Shifted() - pawn.DrawPos;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                return dir;

            dir = pawn.Rotation.AsIntVec3.ToVector3();
            dir.y = 0f;
            return dir;
        }

        private static Vector3 NormalizedOrFallback(Vector3 vector, Vector3 fallback)
        {
            vector.y = 0f;
            if (vector.sqrMagnitude > 0.0001f)
                return vector.normalized;
            fallback.y = 0f;
            if (fallback.sqrMagnitude > 0.0001f)
                return fallback.normalized;
            return Vector3.forward;
        }

        private static Vector3 RotateAroundY(Vector3 vector, float angleDeg)
        {
            return Quaternion.AngleAxis(angleDeg, Vector3.up) * vector;
        }

        private static void SolveTwoBone(
            Vector3 root,
            Vector3 target,
            float lengthA,
            float lengthB,
            Vector3 defaultJoint,
            out Vector3 joint,
            out Vector3 adjustedTarget)
        {
            Vector3 toTarget = target - root;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;
            if (distance < 0.0001f)
            {
                toTarget = defaultJoint - root;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f)
                    toTarget = Vector3.forward;
                distance = toTarget.magnitude;
            }

            Vector3 dir = toTarget / distance;
            float maxReach = Mathf.Max(0.001f, lengthA + lengthB - 0.001f);
            float minReach = Mathf.Max(0.001f, Mathf.Abs(lengthA - lengthB) + 0.001f);
            float clampedDistance = Mathf.Clamp(distance, minReach, maxReach);
            adjustedTarget = root + dir * clampedDistance;

            float a = (lengthA * lengthA - lengthB * lengthB + clampedDistance * clampedDistance) /
                (2f * clampedDistance);
            float hSq = Mathf.Max(0f, lengthA * lengthA - a * a);
            float h = Mathf.Sqrt(hSq);
            Vector3 mid = root + dir * a;
            Vector3 perp = new Vector3(dir.z, 0f, -dir.x);
            Vector3 candidateA = mid + perp * h;
            Vector3 candidateB = mid - perp * h;
            joint = (candidateA - defaultJoint).sqrMagnitude <= (candidateB - defaultJoint).sqrMagnitude
                ? candidateA
                : candidateB;
        }
    }

    public class PawnRenderNode_WarbringerLegPart : PawnRenderNode
    {
        internal WarbringerLegPartSpec spec;

        public PawnRenderNode_WarbringerLegPart(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
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

    public class PawnRenderNodeWorker_WarbringerLegPart : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
                return false;
            return node is PawnRenderNode_WarbringerLegPart;
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 result = base.OffsetFor(node, parms, out pivot);
            PawnRenderNode_WarbringerLegPart part = node as PawnRenderNode_WarbringerLegPart;
            if (part == null || part.spec == null || parms.pawn == null)
                return result;

            CompWarbringerLegRenderer comp = parms.pawn.TryGetComp<CompWarbringerLegRenderer>();
            CompProperties_WarbringerLegRenderer compProps = comp != null ? comp.Props : null;
            float drawSize = Mathf.Max(0.01f, compProps != null ? compProps.drawSize : 5f);
            Vector3 sourceStart = part.spec.SourceStart(drawSize);
            WarbringerLegPose pose = WarbringerLegKinematics.PoseFor(parms.pawn, part.spec.leg, compProps, parms);

            pivot = sourceStart;
            return result + (part.spec.TargetStart(pose) - sourceStart);
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            Quaternion rotation = base.RotationFor(node, parms);
            PawnRenderNode_WarbringerLegPart part = node as PawnRenderNode_WarbringerLegPart;
            if (part == null || part.spec == null || parms.pawn == null)
                return rotation;

            CompWarbringerLegRenderer comp = parms.pawn.TryGetComp<CompWarbringerLegRenderer>();
            CompProperties_WarbringerLegRenderer compProps = comp != null ? comp.Props : null;
            float drawSize = Mathf.Max(0.01f, compProps != null ? compProps.drawSize : 5f);
            WarbringerLegPose pose = WarbringerLegKinematics.PoseFor(parms.pawn, part.spec.leg, compProps, parms);
            float angle = WarbringerLegKinematics.SegmentRotationDeltaDeg(
                part.spec.SourceStart(drawSize),
                part.spec.SourceEnd(drawSize),
                part.spec.TargetStart(pose),
                part.spec.TargetEnd(pose));

            return rotation * Quaternion.AngleAxis(angle, Vector3.up);
        }
    }

    public class CompWarbringerLegRenderer : ThingComp
    {
        private bool movingLastTick;

        public CompProperties_WarbringerLegRenderer Props => (CompProperties_WarbringerLegRenderer)props;

        public override List<PawnRenderNode> CompRenderNodes()
        {
            Pawn pawn = parent as Pawn;
            if (pawn == null || pawn.Drawer == null || pawn.Drawer.renderer == null || pawn.Drawer.renderer.renderTree == null || !pawn.Spawned)
                return null;

            List<PawnRenderNode> nodes = new List<PawnRenderNode>(WarbringerLegCatalog.Parts.Count);
            for (int i = 0; i < WarbringerLegCatalog.Parts.Count; i++)
            {
                WarbringerLegPartSpec spec = WarbringerLegCatalog.Parts[i];
                PawnRenderNodeProperties renderProps = new PawnRenderNodeProperties
                {
                    workerClass = typeof(PawnRenderNodeWorker_WarbringerLegPart),
                    nodeClass = typeof(PawnRenderNode_WarbringerLegPart),
                    texPath = spec.texPath,
                    parentTagDef = PawnRenderNodeTagDefOf.Body,
                    overrideMeshSize = new Vector2(Props.drawSize, Props.drawSize),
                    drawSize = Vector2.one,
                    baseLayer = Props.baseLayer + spec.layerOffset,
                    pawnType = PawnRenderNodeProperties.RenderNodePawnType.Any,
                    rotateIndependently = true,
                    debugLabel = spec.debugLabel
                };

                PawnRenderNode_WarbringerLegPart node =
                    new PawnRenderNode_WarbringerLegPart(pawn, renderProps, pawn.Drawer.renderer.renderTree);
                node.spec = spec;
                nodes.Add(node);
            }

            return nodes;
        }

        public override void CompTick()
        {
            base.CompTick();

            Pawn pawn = parent as Pawn;
            if (pawn == null || !pawn.Spawned)
                return;

            bool movingNow = pawn.pather != null && pawn.pather.MovingNow;
            if (movingNow || movingNow != movingLastTick)
                pawn.Drawer?.renderer?.renderTree?.SetDirty();

            movingLastTick = movingNow;
        }
    }
}
