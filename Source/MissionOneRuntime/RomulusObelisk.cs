using RimCampaign.Runtime;
using System.Linq;
using UnityEngine;
using Verse;
namespace PMP.Campaign {
    public sealed class RomulusObelisk:Building {
        Material glow;
        int rune;
        bool bound;
        public override void SpawnSetup(Map map,bool respawningAfterLoad){base.SpawnSetup(map,respawningAfterLoad);
            var c=Current.Game.GetComponent<CampaignController>();
            rune=0;bound=false;
            // During map generation c.Map still refers to the preceding map. Bind in DrawAt.
            if(MissionScope.Owns(c,map,"mission-two"))Bind(c);
        }
        void Bind(CampaignController c){
                bound=true;
                var placement=c.MapSpec.Objects.FirstOrDefault(p=>p.Kind=="Thing"&&p.Def.Name==def.defName&&p.Position.X==Position.x&&p.Position.Z==Position.z);
                if(placement?.Id.StartsWith("m2-rune-")==true)int.TryParse(placement.Id.Substring("m2-rune-".Length),out rune);
        }
        protected override void DrawAt(Vector3 drawLoc,bool flip=false){
            base.DrawAt(drawLoc,flip);
            var c=Current.Game.GetComponent<CampaignController>();
            if(!MissionScope.Owns(c,Map,"mission-two")||MissionEffectsMapComponent.For(Map)==null)return;
            if(!bound)Bind(c);
            if(rune==0||!c.Objects.TryGetValue("m2-rune-"+rune,out var host)||host!=this||!c.State.Variables.TryGetValue("m2-runes",out var progress)||progress<rune)return;
            if(glow==null){glow=DefDatabase<MaterialDef>.GetNamed("PMP17_RomulusRuneMaterial").CreateMaterialInstance();glow.mainTexture=Graphic.MatSingle.mainTexture;}
            glow.SetFloat("_Pulse",.7f+.3f*Mathf.Sin(Time.realtimeSinceStartup*2));
            var at=drawLoc+def.graphicData.drawOffset;at.y+=.03f;
            Graphics.DrawMesh(MeshPool.plane10,Matrix4x4.TRS(at,Quaternion.identity,new Vector3(def.graphicData.drawSize.x,1,def.graphicData.drawSize.y)),glow,0);
        }
        public override void Destroy(DestroyMode mode=DestroyMode.Vanish){if(glow!=null)Object.Destroy(glow);glow=null;base.Destroy(mode);}
    }
}
