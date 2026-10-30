using UnityEngine;
using Verse;

namespace PMP.Anomaly {
    [StaticConstructorOnStartup]
    static class StasisVisual {
        static readonly Material Field = DefDatabase<MaterialDef>.GetNamed("PMP_StasisFieldMaterial").CreateMaterialInstance();
        internal static void Draw(Vector3 position) {
            Field.SetVector("_State",new Vector4(Find.TickManager.TicksGame/60f,0,0,0));
            position.y=AltitudeLayer.MoteOverhead.AltitudeFor();
            position.z+=.825f;
            Graphics.DrawMesh(MeshPool.plane10,Matrix4x4.TRS(position,Quaternion.identity,new Vector3(5.4f,1,5.4f)),Field,0,null,0,null,UnityEngine.Rendering.ShadowCastingMode.Off,false,renderQueue:3500);
        }
    }
}
