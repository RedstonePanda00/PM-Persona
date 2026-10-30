using System.Collections.Generic;
using System.Linq;
using RimCampaign.Runtime;
using UnityEngine;
using Verse;

namespace PMP.Campaign {
    // Persona appearance; references existing interactive hosts without new wall defs/textures.
    [StaticConstructorOnStartup]
    public sealed class MissionWallCracks {
        readonly List<Thing> hosts=new List<Thing>();
        bool initialized;
        static Mesh crack;
        static Material material;
        public void Reset(){hosts.Clear();initialized=false;}
        public int HostCount=>hosts.Count;
        public void Bind(CampaignController c){
            hosts.Clear();
            foreach(var spec in c.MapSpec.Interactions.Where(s=>s.BlockCells.Count>0))
                if(c.Objects.TryGetValue(spec.Host,out var host)&&host?.def.defName=="PMP_RomulusRedWall"&&!hosts.Contains(host))hosts.Add(host);
            initialized=true;
        }
        public void Draw(CampaignController c){
            if(!initialized)Bind(c);
            if(crack==null)crack=CreateMesh();
            if(material==null)material=SolidColorMaterials.SimpleSolidColorMaterial(new Color(.08f,.025f,.025f,.96f));
            foreach(var host in hosts){
                if(host?.Spawned!=true||host.Map!=c.Map||host.Fogged())continue;
                var at=host.DrawPos;at.y=AltitudeLayer.MetaOverlays.AltitudeFor()+.01f;
                Graphics.DrawMesh(crack,Matrix4x4.TRS(at,Quaternion.Euler(0,(host.thingIDNumber&3)*90,0),Vector3.one),material,0);
            }
        }
        static Mesh CreateMesh(){
            var vertices=new List<Vector3>();var triangles=new List<int>();
            void Stroke(float x1,float z1,float x2,float z2,float width){
                var a=new Vector3(x1,0,z1);var b=new Vector3(x2,0,z2);var normal=Vector3.Cross((b-a).normalized,Vector3.up)*width*.5f;int n=vertices.Count;
                vertices.Add(a-normal);vertices.Add(a+normal);vertices.Add(b+normal);vertices.Add(b-normal);
                triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});
            }
            var points=new[]{new Vector2(-.25f,.43f),new Vector2(-.08f,.24f),new Vector2(-.16f,.10f),new Vector2(.08f,-.02f),new Vector2(-.02f,-.20f),new Vector2(.18f,-.43f)};
            for(int i=1;i<points.Length;i++)Stroke(points[i-1].x,points[i-1].y,points[i].x,points[i].y,.065f);
            Stroke(-.16f,.10f,-.34f,.02f,.045f);Stroke(-.34f,.02f,-.43f,-.13f,.035f);
            Stroke(.08f,-.02f,.30f,.16f,.045f);Stroke(.30f,.16f,.43f,.13f,.035f);
            Stroke(-.02f,-.20f,-.24f,-.32f,.04f);
            var mesh=new Mesh{name="Persona interactive wall cracks"};mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;
        }
    }
}
