using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Romulus {
    // Directional art calibration keeps the original texture proportions intact.
    public sealed class LegionVisualExtension:DefModExtension {
        public float southScale=1,northScale=1;
        public float eyeSize=.15f;
        // Non-human races have no story/bodyType. Supply their anatomical anchors here.
        public List<BodyTypeDef.WoundAnchor> eyeAnchors;
        public float Scale(Rot4 facing)=>facing==Rot4.South?southScale:facing==Rot4.North?northScale:1;
    }
    [HarmonyPatch(typeof(PawnRenderNodeWorker),nameof(PawnRenderNodeWorker.ScaleFor))]
    static class LegionBodyCalibration {
        static void Postfix(PawnRenderNode node,PawnDrawParms parms,ref Vector3 __result){
            if(node.Props.tagDef!=PawnRenderNodeTagDefOf.Body||parms.facing.IsHorizontal)return;
            var visual=parms.pawn.def.GetModExtension<LegionVisualExtension>();
            if(visual!=null){float scale=visual.Scale(parms.facing);__result.x*=scale;__result.z*=scale;}
        }
    }
    public sealed class LegionEyeHistory {
        public const int Capacity=12;
        public readonly Vector3[] positions=new Vector3[Capacity];
        readonly MaterialPropertyBlock block=new MaterialPropertyBlock();
        public int count,lastTick;
        public void Draw(PawnDrawParms parms,Vector3 eye,float alpha){
            if(!parms.pawn.Spawned||parms.pawn.Dead||alpha<=0||parms.pawn.pather?.MovingNow!=true){count=0;return;}
            int tick=Find.TickManager.TicksGame;
            if(tick-lastTick>6||count>0&&(eye-positions[0]).sqrMagnitude>9)count=0;
            // Record the final eye-node matrix, including head motion, rather than the pawn centre.
            var from=eye;
            for(int i=0;i<count;i++){
                var to=positions[i];var delta=to-from;delta.y=0;
                if(delta.sqrMagnitude>.000001f){
                    var centre=(from+to)*.5f;centre.y=eye.y;
                    float fade=1f-(float)i/Capacity;
                    block.SetColor(ShaderPropertyIDs.Color,new Color(1.7f,.04f,.025f,alpha*.5f*fade*parms.tint.a));
                    var matrix=Matrix4x4.TRS(centre,Quaternion.AngleAxis(Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg,Vector3.up),new Vector3(.055f,1,delta.magnitude));
                    GenDraw.DrawMeshNowOrLater(MeshPool.plane10,matrix,LegionEyeMaterial.Trail,parms.DrawNow,block);
                }
                from=to;
            }
            block.Clear();
            if(count==0||tick-lastTick>=3){
                lastTick=tick;
                for(int i=Math.Min(count,positions.Length-1);i>0;i--)positions[i]=positions[i-1];
                positions[0]=eye;count=Math.Min(positions.Length,count+1);
            }
        }
    }
    public sealed class LegionEyeProperties:PawnRenderNodeProperties_Eye {
        public LegionEyeProperties(){workerClass=typeof(LegionEyeWorker);nodeClass=typeof(PawnRenderNode);useGraphic=false;baseLayer=90;pawnType=RenderNodePawnType.Any;}
    }
    [HarmonyPatch(typeof(DynamicPawnRenderNodeSetup_Hediffs),nameof(DynamicPawnRenderNodeSetup_Hediffs.GetDynamicNodes))]
    static class LegionEyeNodes {
        static void Postfix(Pawn pawn,PawnRenderTree tree,ref IEnumerable<(PawnRenderNode node,PawnRenderNode parent)> __result){__result=Expand(__result,pawn,tree);}
        static IEnumerable<(PawnRenderNode node,PawnRenderNode parent)> Expand(IEnumerable<(PawnRenderNode node,PawnRenderNode parent)> source,Pawn pawn,PawnRenderTree tree){
            foreach(var entry in source){
                if(!(entry.node.Props is LegionEyeProperties)){yield return entry;continue;}
                // Bind the real anatomy once when the tree is built; never invent pairs of eyes.
                // Eyes is an apparel coverage group and also contains the skull/brain.
                var bionic=DefDatabase<HediffDef>.GetNamed("BionicEye").RenderNodeProperties[0];
                foreach(var part in pawn.RaceProps.body.AllParts.Where(p=>p.def==BodyPartDefOf.Eye||p.def.tags.Contains(BodyPartTagDefOf.SightSource))){
                    var props=new LegionEyeProperties{parentTagDef=PawnRenderNodeTagDefOf.Head};
                    PawnRenderNode node;
                    if(pawn.RaceProps.Humanlike){
                        props.nodeClass=typeof(PawnRenderNode_AttachmentHead);props.drawData=bionic.drawData;props.drawSize=bionic.drawSize;
                        node=new PawnRenderNode_AttachmentHead(pawn,props,tree);
                    }else{
                        props.nodeClass=typeof(PawnRenderNode);props.drawSize=Vector2.one*(pawn.def.GetModExtension<LegionVisualExtension>()?.eyeSize??.15f)*1.5f;
                        node=new PawnRenderNode(pawn,props,tree);
                    }
                    node.hediff=entry.node.hediff;node.bodyPart=part;
                    yield return (node,entry.parent);
                }
            }
        }
    }
    [StaticConstructorOnStartup] static class LegionEyeMaterial {
        public static readonly Material Glow,Trail;
        public static readonly Color Red=new Color(1.7f,.04f,.025f,1);
        public static Material Immediate(float alpha,Color tint){
            // Vanilla DrawMeshNow ignores property blocks. Cache a bounded alpha palette for portraits.
            var color=Red*tint;color.a=Mathf.Round(Mathf.Clamp01(alpha)*32)/32*tint.a;
            return MaterialPool.MatFrom(new MaterialRequest{mainTex=(Texture2D)Glow.mainTexture,shader=ShaderDatabase.MoteGlow,color=color});
        }
        static LegionEyeMaterial(){
            // A soft circular light; generated once, then shared by all eye nodes and trails.
            var texture=new Texture2D(32,32,TextureFormat.RGBA32,false){name="PMP Legion eye glow",wrapMode=TextureWrapMode.Clamp};
            var pixels=new Color[1024];
            for(int y=0;y<32;y++)for(int x=0;x<32;x++){
                float radius=new Vector2((x-15.5f)/15.5f,(y-15.5f)/15.5f).magnitude;
                float alpha=Mathf.Pow(Mathf.Clamp01(1-radius),.75f);pixels[y*32+x]=new Color(1,1,1,alpha);
            }
            texture.SetPixels(pixels);texture.Apply();Glow=MaterialPool.MatFrom(new MaterialRequest{mainTex=texture,shader=ShaderDatabase.MoteGlow,color=Red});
            // Constant opacity along the segment; only soften its width so joined ends have no gaps.
            var line=new Texture2D(32,4,TextureFormat.RGBA32,false){name="PMP Legion eye trail",wrapMode=TextureWrapMode.Clamp};
            var linePixels=new Color[128];
            for(int y=0;y<4;y++)for(int x=0;x<32;x++)linePixels[y*32+x]=new Color(1,1,1,Mathf.Clamp01((1-Mathf.Abs((x-15.5f)/15.5f))*3));
            line.SetPixels(linePixels);line.Apply();Trail=MaterialPool.MatFrom(new MaterialRequest{mainTex=line,shader=ShaderDatabase.MoteGlow,color=Color.white});
        }
    }
    public sealed class LegionEyeWorker:PawnRenderNodeWorker_HediffEye {
        public override bool CanDrawNow(PawnRenderNode node,PawnDrawParms parms){
            if(!base.CanDrawNow(node,parms)||parms.pawn.Dead||parms.facing==Rot4.North)return false;
            var r=node.hediff as Hediff_LegionRecord;
            if(r==null||r.EyeAlpha<=0||node.bodyPart==null||parms.pawn.health.hediffSet.PartIsMissing(node.bodyPart))return false;
            if(parms.pawn.RaceProps.Humanlike){if(!TryGetWoundAnchor(node.bodyPart.woundAnchorTag,parms,out _))return false;}
            else if(MechanicalAnchor(node,parms)==null)return false;
            return true;
        }
        public override void AppendDrawRequests(PawnRenderNode node,PawnDrawParms parms,List<PawnGraphicDrawRequest> requests){
            if(CanDrawNow(node,parms))requests.Add(new PawnGraphicDrawRequest(node,parms.pawn.RaceProps.Humanlike?node.GetMesh(parms):MeshPool.plane10,
                parms.DrawNow?LegionEyeMaterial.Immediate(((Hediff_LegionRecord)node.hediff).EyeAlpha,parms.tint):LegionEyeMaterial.Glow));
        }
        public override Vector3 OffsetFor(PawnRenderNode node,PawnDrawParms parms,out Vector3 pivot){
            pivot=Vector3.zero;Vector3 offset=Vector3.zero;
            if(parms.pawn.RaceProps.Humanlike)offset=base.OffsetFor(node,parms,out pivot);
            else {
                var anchor=MechanicalAnchor(node,parms);
                if(anchor!=null)PawnDrawUtility.CalcAnchorData(parms.pawn,anchor,parms.facing,out offset,out _);
                offset*=parms.pawn.def.GetModExtension<LegionVisualExtension>()?.Scale(parms.facing)??1;
            }
            return offset;
        }
        static BodyTypeDef.WoundAnchor MechanicalAnchor(PawnRenderNode node,PawnDrawParms parms)=>
            parms.pawn.def.GetModExtension<LegionVisualExtension>()?.eyeAnchors?.FirstOrDefault(a=>a.tag==node.bodyPart?.woundAnchorTag&&PawnDrawUtility.AnchorUsable(parms.pawn,a,parms.facing));
        // The native attachment offset/scale/animation is retained; glow overlays the helmet eye lenses.
        public override float LayerFor(PawnRenderNode node,PawnDrawParms parms)=>90+node.debugLayerOffset;
        public override MaterialPropertyBlock GetMaterialPropertyBlock(PawnRenderNode node,Material material,PawnDrawParms parms){
            var r=(Hediff_LegionRecord)node.hediff;
            var block=node.MatPropBlock;
            float alpha=r.EyeAlpha*parms.tint.a;
            block.SetColor(ShaderPropertyIDs.Color,new Color(1.7f,.04f,.025f,alpha));return block;
        }
        public override void PostDraw(PawnRenderNode node,PawnDrawParms parms,Mesh mesh,Matrix4x4 matrix){
            base.PostDraw(node,parms,mesh,matrix);
            if(parms.Portrait||node.bodyPart==null||!(node.hediff is Hediff_LegionRecord r))return;
            if(!r.EyeTrails.TryGetValue(node.bodyPart,out var history))r.EyeTrails.Add(node.bodyPart,history=new LegionEyeHistory());
            history.Draw(parms,matrix.MultiplyPoint3x4(Vector3.zero),r.EyeAlpha);
        }
    }
    [HarmonyPatch(typeof(PawnRenderer),"ParallelGetPreRenderResults")]
    static class LegionEyeCache {
        static void Prefix(Pawn ___pawn,ref bool disableCache){if(LegionDoctrine.Record(___pawn)?.EyeAlpha>0)disableCache=true;}
    }
}
