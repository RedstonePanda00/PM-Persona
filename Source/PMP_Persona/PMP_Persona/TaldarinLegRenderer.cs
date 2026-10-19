using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    public class CompProperties_TaldarinLegRenderer : CompProperties
    {
        public float drawSize = 3f;
        public float baseLayer = -4f;
        public int gaitCycleTicks = 80;
        public float swingRatio = 0.22f;
        public float strideLength = 0.2f;
        public float swingOutwardOffset = 0.03f;

        public CompProperties_TaldarinLegRenderer()
        {
            compClass = typeof(CompTaldarinLegRenderer);
        }
    }

    internal enum TaldarinLegSegment
    {
        Node,
        Leg
    }

    internal sealed class TaldarinLegChain
    {
        public readonly string debugName;
        public readonly bool right;
        public readonly float phaseOffset;
        public readonly Vector2 hipPixel;
        public readonly Vector2 kneePixel;
        public readonly Vector2 footPixel;

        public TaldarinLegChain(
            string debugName,
            bool right,
            float phaseOffset,
            Vector2 hipPixel,
            Vector2 kneePixel,
            Vector2 footPixel)
        {
            this.debugName = debugName;
            this.right = right;
            this.phaseOffset = phaseOffset;
            this.hipPixel = hipPixel;
            this.kneePixel = kneePixel;
            this.footPixel = footPixel;
        }

        public Vector3 Hip(float drawSize)
        {
            return TaldarinLegKinematics.PixelToLocal(hipPixel, drawSize);
        }

        public Vector3 Knee(float drawSize)
        {
            return TaldarinLegKinematics.PixelToLocal(kneePixel, drawSize);
        }

        public Vector3 Foot(float drawSize)
        {
            return TaldarinLegKinematics.PixelToLocal(footPixel, drawSize);
        }
    }

    internal sealed class TaldarinLegPartSpec
    {
        public readonly TaldarinLegChain leg;
        public readonly TaldarinLegSegment segment;
        public readonly string texPath;
        public readonly float layerOffset;
        public readonly string debugLabel;

        public TaldarinLegPartSpec(
            TaldarinLegChain leg,
            TaldarinLegSegment segment,
            string texPath,
            float layerOffset)
        {
            this.leg = leg;
            this.segment = segment;
            this.texPath = texPath;
            this.layerOffset = layerOffset;
            debugLabel = "TaldarinLeg." + leg.debugName + "." + segment;
        }

        public Vector3 SourceStart(float drawSize)
        {
            return segment == TaldarinLegSegment.Node
                ? leg.Hip(drawSize)
                : leg.Knee(drawSize);
        }

        public Vector3 SourceEnd(float drawSize)
        {
            return segment == TaldarinLegSegment.Node
                ? leg.Knee(drawSize)
                : leg.Foot(drawSize);
        }

        public Vector3 TargetStart(TaldarinLegPose pose)
        {
            return segment == TaldarinLegSegment.Node ? pose.hip : pose.knee;
        }

        public Vector3 TargetEnd(TaldarinLegPose pose)
        {
            return segment == TaldarinLegSegment.Node ? pose.knee : pose.foot;
        }
    }

    internal struct TaldarinLegPose
    {
        public Vector3 hip;
        public Vector3 knee;
        public Vector3 foot;
    }

    internal static class TaldarinLegCatalog
    {
        // Four-beat crawl: FrontLeft -> BackRight -> FrontRight -> BackLeft.
        public static readonly TaldarinLegChain FrontRight = new TaldarinLegChain(
            "FrontRight",
            true,
            0.5f,
            new Vector2(565f, 758f),
            new Vector2(648f, 688f),
            new Vector2(780f, 870f));

        public static readonly TaldarinLegChain FrontLeft = new TaldarinLegChain(
            "FrontLeft",
            false,
            0f,
            new Vector2(433f, 758f),
            new Vector2(350f, 688f),
            new Vector2(218f, 870f));

        public static readonly TaldarinLegChain BackRight = new TaldarinLegChain(
            "BackRight",
            true,
            0.75f,
            new Vector2(565f, 708f),
            new Vector2(648f, 638f),
            new Vector2(780f, 790f));

        public static readonly TaldarinLegChain BackLeft = new TaldarinLegChain(
            "BackLeft",
            false,
            0.25f,
            new Vector2(433f, 708f),
            new Vector2(350f, 638f),
            new Vector2(218f, 790f));

        public static readonly List<TaldarinLegPartSpec> Parts = new List<TaldarinLegPartSpec>
        {
            new TaldarinLegPartSpec(
                BackRight,
                TaldarinLegSegment.Leg,
                "Things/Pawn/Mechanoid/Taldarin/LegRightBack",
                0f),
            new TaldarinLegPartSpec(
                BackRight,
                TaldarinLegSegment.Node,
                "Things/Pawn/Mechanoid/Taldarin/NodeRightBack",
                0.01f),
            new TaldarinLegPartSpec(
                BackLeft,
                TaldarinLegSegment.Leg,
                "Things/Pawn/Mechanoid/Taldarin/LegLeftBack",
                0f),
            new TaldarinLegPartSpec(
                BackLeft,
                TaldarinLegSegment.Node,
                "Things/Pawn/Mechanoid/Taldarin/NodeLeftBack",
                0.01f),
            new TaldarinLegPartSpec(
                FrontRight,
                TaldarinLegSegment.Leg,
                "Things/Pawn/Mechanoid/Taldarin/LegRightFront",
                0.02f),
            new TaldarinLegPartSpec(
                FrontRight,
                TaldarinLegSegment.Node,
                "Things/Pawn/Mechanoid/Taldarin/NodeRightFront",
                0.03f),
            new TaldarinLegPartSpec(
                FrontLeft,
                TaldarinLegSegment.Leg,
                "Things/Pawn/Mechanoid/Taldarin/LegLeftFront",
                0.02f),
            new TaldarinLegPartSpec(
                FrontLeft,
                TaldarinLegSegment.Node,
                "Things/Pawn/Mechanoid/Taldarin/NodeLeftFront",
                0.03f)
        };
    }

    internal static class TaldarinLegKinematics
    {
        private const float CanvasPixels = 1024f;

        public static Vector3 PixelToLocal(Vector2 pixel, float drawSize)
        {
            return new Vector3(
                (pixel.x - CanvasPixels * 0.5f) / CanvasPixels * drawSize,
                0f,
                (CanvasPixels * 0.5f - pixel.y) / CanvasPixels * drawSize);
        }

        public static TaldarinLegPose PoseFor(
            Pawn pawn,
            TaldarinLegChain leg,
            CompProperties_TaldarinLegRenderer props,
            PawnDrawParms parms)
        {
            float drawSize = SafeDrawSize(props);
            TaldarinLegPose pose = new TaldarinLegPose
            {
                hip = leg.Hip(drawSize),
                knee = leg.Knee(drawSize),
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
            int cycleTicks = Mathf.Max(1, props != null ? props.gaitCycleTicks : 80);
            float swingRatio = Mathf.Clamp(props != null ? props.swingRatio : 0.22f, 0.1f, 0.24f);
            float strideLength = Mathf.Max(0f, props != null ? props.strideLength : 0.2f);
            float outwardOffset = Mathf.Max(0f, props != null ? props.swingOutwardOffset : 0.03f);
            float phase = GaitPhase01(pawn, leg, cycleTicks);
            float along;
            float outward;

            if (phase < swingRatio)
            {
                float t = Mathf.Clamp01(phase / swingRatio);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                along = Mathf.Lerp(-strideLength * 0.5f, strideLength * 0.5f, eased);
                outward = Mathf.Sin(t * Mathf.PI) * outwardOffset * sideSign;
            }
            else
            {
                float t = Mathf.Clamp01((phase - swingRatio) / Mathf.Max(0.001f, 1f - swingRatio));
                float eased = Mathf.SmoothStep(0f, 1f, t);
                along = Mathf.Lerp(strideLength * 0.5f, -strideLength * 0.5f, eased);
                outward = 0f;
            }

            Vector3 desiredFoot = pose.foot + moveDir * along + sideDir * outward;
            SolveTwoBone(
                pose.hip,
                desiredFoot,
                (pose.knee - pose.hip).magnitude,
                (pose.foot - pose.knee).magnitude,
                pose.knee,
                out pose.knee,
                out pose.foot);

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

        private static float SafeDrawSize(CompProperties_TaldarinLegRenderer props)
        {
            return Mathf.Max(0.01f, props != null ? props.drawSize : 3f);
        }

        private static float GaitPhase01(Pawn pawn, TaldarinLegChain leg, int cycleTicks)
        {
            int ticks = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            float desync = pawn != null ? pawn.thingIDNumber % cycleTicks : 0f;
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
            float h = Mathf.Sqrt(Mathf.Max(0f, lengthA * lengthA - a * a));
            Vector3 mid = root + dir * a;
            Vector3 perp = new Vector3(dir.z, 0f, -dir.x);
            Vector3 candidateA = mid + perp * h;
            Vector3 candidateB = mid - perp * h;
            joint = (candidateA - defaultJoint).sqrMagnitude <= (candidateB - defaultJoint).sqrMagnitude
                ? candidateA
                : candidateB;
        }
    }

    public class PawnRenderNode_TaldarinLegPart : PawnRenderNode
    {
        internal TaldarinLegPartSpec spec;

        public PawnRenderNode_TaldarinLegPart(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
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

    public class PawnRenderNodeWorker_TaldarinLegPart : PawnRenderNodeWorker
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            return base.CanDrawNow(node, parms) && node is PawnRenderNode_TaldarinLegPart;
        }

        public override Vector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out Vector3 pivot)
        {
            Vector3 result = base.OffsetFor(node, parms, out pivot);
            PawnRenderNode_TaldarinLegPart part = node as PawnRenderNode_TaldarinLegPart;
            if (part == null || part.spec == null || parms.pawn == null)
                return result;

            CompTaldarinLegRenderer comp = parms.pawn.TryGetComp<CompTaldarinLegRenderer>();
            CompProperties_TaldarinLegRenderer compProps = comp != null ? comp.Props : null;
            float drawSize = Mathf.Max(0.01f, compProps != null ? compProps.drawSize : 3f);
            Vector3 sourceStart = part.spec.SourceStart(drawSize);
            TaldarinLegPose pose = TaldarinLegKinematics.PoseFor(parms.pawn, part.spec.leg, compProps, parms);

            pivot = sourceStart;
            return result + (part.spec.TargetStart(pose) - sourceStart);
        }

        public override Quaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
        {
            Quaternion rotation = base.RotationFor(node, parms);
            PawnRenderNode_TaldarinLegPart part = node as PawnRenderNode_TaldarinLegPart;
            if (part == null || part.spec == null || parms.pawn == null)
                return rotation;

            CompTaldarinLegRenderer comp = parms.pawn.TryGetComp<CompTaldarinLegRenderer>();
            CompProperties_TaldarinLegRenderer compProps = comp != null ? comp.Props : null;
            float drawSize = Mathf.Max(0.01f, compProps != null ? compProps.drawSize : 3f);
            TaldarinLegPose pose = TaldarinLegKinematics.PoseFor(parms.pawn, part.spec.leg, compProps, parms);
            float angle = TaldarinLegKinematics.SegmentRotationDeltaDeg(
                part.spec.SourceStart(drawSize),
                part.spec.SourceEnd(drawSize),
                part.spec.TargetStart(pose),
                part.spec.TargetEnd(pose));

            return rotation * Quaternion.AngleAxis(angle, Vector3.up);
        }
    }

    public class CompTaldarinLegRenderer : ThingComp
    {
        private bool movingLastTick;

        public CompProperties_TaldarinLegRenderer Props => (CompProperties_TaldarinLegRenderer)props;

        public override List<PawnRenderNode> CompRenderNodes()
        {
            Pawn pawn = parent as Pawn;
            if (pawn == null || pawn.Drawer == null || pawn.Drawer.renderer == null ||
                pawn.Drawer.renderer.renderTree == null || !pawn.Spawned)
                return null;

            List<PawnRenderNode> nodes = new List<PawnRenderNode>(TaldarinLegCatalog.Parts.Count);
            for (int i = 0; i < TaldarinLegCatalog.Parts.Count; i++)
            {
                TaldarinLegPartSpec spec = TaldarinLegCatalog.Parts[i];
                PawnRenderNodeProperties renderProps = new PawnRenderNodeProperties
                {
                    workerClass = typeof(PawnRenderNodeWorker_TaldarinLegPart),
                    nodeClass = typeof(PawnRenderNode_TaldarinLegPart),
                    texPath = spec.texPath,
                    parentTagDef = PawnRenderNodeTagDefOf.Body,
                    overrideMeshSize = new Vector2(Props.drawSize, Props.drawSize),
                    drawSize = Vector2.one,
                    baseLayer = Props.baseLayer + spec.layerOffset,
                    pawnType = PawnRenderNodeProperties.RenderNodePawnType.Any,
                    rotateIndependently = true,
                    debugLabel = spec.debugLabel
                };

                PawnRenderNode_TaldarinLegPart node = new PawnRenderNode_TaldarinLegPart(
                    pawn,
                    renderProps,
                    pawn.Drawer.renderer.renderTree);
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
