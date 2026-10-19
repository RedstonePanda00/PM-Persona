using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PMP_Persona.Rendering;
using PMP_Persona.SupernovaSupport;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    // Invoked only by the isolated -pmp17audit harness.
    internal static class BuildingPlasmaAudit17
    {
        internal static void Run(string folder)
        {
            var map=Find.CurrentMap;
            var origin=map.Center+new IntVec3(15,0,15);
            var spawned=new List<Thing>();
            var walls=new List<Thing>();
            Thing Spawn(string name,IntVec3 offset,ThingDef stuff=null,Rot4? rot=null)
            {
                var thing=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name),stuff);
                GenSpawn.Spawn(thing,origin+offset,map,rot??Rot4.North);spawned.Add(thing);
                // Random quicktest maps may put this fixture inside mountain fog. This test
                // exercises visible building eligibility; explicitly reveal only its occupied cells.
                foreach (var cell in thing.OccupiedRect()) if (cell.InBounds(map)) map.fogGrid.Unfog(cell);
                return thing;
            }
            try
            {
                for(int x=0;x<6;x++) walls.Add(Spawn("Wall",new IntVec3(x,0,0),ThingDefOf.Steel));
                for(int z=1;z<4;z++) walls.Add(Spawn("Wall",new IntVec3(5,0,z),ThingDefOf.Steel));
                var groups=BuildingPlasmaRenderer.Group(walls);
                Require(groups.Count==1 && groups[0].Count==9,"connected L wall was not merged");
                var pendingBefore=new List<Waylith.Rendering.MeshDrawCommand>();
                Waylith.Rendering.MeshDrawSubmission.CopyPendingCommands(pendingBefore,-1);
                using(var surface=BuildingPlasmaRenderer.CaptureForAudit(groups[0]))
                {
                    surface.Source=origin.ToVector3Shifted()+new Vector3(-8,0,-5);
                    var pixels=Render(surface,folder,"Wall-union");
                    // A seam in the middle of a straight run is interior, not an individual-tile rim.
                    var seam=origin.ToVector3Shifted()+new Vector3(1.5f,0,0);
                    Require(Sample(pixels,surface,seam).a>100,"wall seam has an empty mask/overlay");
                    using(var partial=BuildingPlasmaRenderer.CaptureForAudit(walls.Take(4).ToList()))
                    {
                        partial.Source=surface.Source;
                        var cropped=Render(partial,folder,"Wall-short-union");
                        var a=Sample(pixels,surface,seam);var b=Sample(cropped,partial,seam);
                        Require(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)<65,
                            "world-space wall flow reset after changing union bounds");
                    }
                }
                var pendingAfter=new List<Waylith.Rendering.MeshDrawCommand>();
                Waylith.Rendering.MeshDrawSubmission.CopyPendingCommands(pendingAfter,-1);
                Require(pendingAfter.Count==pendingBefore.Count,"building capture polluted main map draw queue");
                foreach(var test in new[]{("TableButcher",new IntVec3(0,0,6),Rot4.East),
                    ("Turret_MiniTurret",new IntVec3(5,0,7),Rot4.North),("Door",new IntVec3(9,0,6),Rot4.North)})
                {
                    var def=DefDatabase<ThingDef>.GetNamed(test.Item1);
                    var thing=Spawn(test.Item1,test.Item2,def.MadeFromStuff?ThingDefOf.Steel:null,test.Item3);
                    using(var surface=BuildingPlasmaRenderer.CaptureForAudit(new List<Thing>{thing}))
                    {surface.Source=origin.ToVector3Shifted();Render(surface,folder,test.Item1);}
                }
                walls[2].Destroy(DestroyMode.Vanish);
                Require(BuildingPlasmaRenderer.Group(walls.Where(t=>t.Spawned)).Count==2,"destroyed wall gap did not split union");
                var player=Spawn("Wall",new IntVec3(10,0,0),ThingDefOf.Steel);player.SetFaction(Faction.OfPlayer);
                var far=Spawn("Wall",new IntVec3(40,0,0),ThingDefOf.Steel);
                var star=(Thing_Supernova)Spawn("PMP_Supernova",new IntVec3(0,0,-5));
                var fields=new List<Thing_Supernova>{star};
                Require(BuildingPlasmaRenderer.Eligible(walls[0],fields),"affected building rejected");
                Require(!BuildingPlasmaRenderer.Eligible(player,fields),"player wall incorrectly affected");
                Require(!BuildingPlasmaRenderer.Eligible(far,fields),"outside-range wall incorrectly affected");
                File.WriteAllText(Path.Combine(folder,"building-plasma.txt"),
                    "PASS linked L wall union, continuous interior seam, world-space flow across bounds changes, main map queue unchanged, rotated multi-cell bench, turret, door, destroyed gap split, player/range filtering.\n");
            }
            finally {foreach(var thing in spawned)if(!thing.Destroyed)thing.Destroy(DestroyMode.Vanish);BuildingPlasmaRenderer.Clear();}
        }

        private static Color32[] Render(BuildingPlasmaRenderer.Surface surface,string folder,string name)
        {
            Require(surface.Texture!=null && !surface.Dirty,"empty building capture: "+name);
            int size=surface.Resolution;
            var capture=TargetOutlineCaptureCamera.Create();
            var output=RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(size,size,TextureFormat.RGBA32,false);
            var previous=RenderTexture.active;
            try
            {
                RenderTexture.active=surface.Texture;image.ReadPixels(new Rect(0,0,size,size),0,0);image.Apply();
                var mask=image.GetPixels32();
                File.WriteAllBytes(Path.Combine(folder,"Building-mask-"+name+".png"),image.EncodeToPNG());
                Require(mask.Count(c=>c.a>180)>100,"blank building silhouette: "+name+" center="+surface.Center+" half="+surface.HalfSize+" maxAlpha="+mask.Max(c=>c.a));
                var block=new MaterialPropertyBlock();BuildingPlasmaRenderer.Configure(surface,block);
                surface.Material.SetVector("_Flow",block.GetVector("_Flow"));
                surface.Material.SetVector("_Clock",new Vector4(2.5f,0,1f/size,surface.HalfSize));
                surface.Material.SetVector("_Surface",block.GetVector("_Surface"));surface.Material.SetFloat("_WorldSpace",1f);
                capture.RenderTo(output,surface.HalfSize,()=>{
                    Require(surface.Material.SetPass(0),"building plasma shader failed");
                    Graphics.DrawMeshNow(MeshPool.plane10,Matrix4x4.Scale(new Vector3(surface.HalfSize*2,1,surface.HalfSize*2)));
                });
                RenderTexture.active=output;image.ReadPixels(new Rect(0,0,size,size),0,0);image.Apply();
                var colors=image.GetPixels32();int interior=0,covered=0;
                for(int y=3;y<size-3;y++)for(int x=3;x<size-3;x++)
                {
                    int i=y*size+x;
                    if(mask[i].a<230 || mask[i-3].a<230 || mask[i+3].a<230 || mask[i-3*size].a<230 || mask[i+3*size].a<230)continue;
                    interior++;if(colors[i].a>60 && colors[i].r+colors[i].g+colors[i].b>60)covered++;
                }
                Require(interior>10 && covered>=interior*.95f,"building interior is not covered: "+name+" "+covered+"/"+interior);
                File.WriteAllBytes(Path.Combine(folder,"Building-plasma-"+name+".png"),image.EncodeToPNG());return colors;
            }
            finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(output);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(capture.gameObject);}
        }
        private static Color32 Sample(Color32[] pixels,BuildingPlasmaRenderer.Surface surface,Vector3 world)
        {
            int x=Mathf.Clamp((int)(((world.x-surface.Center.x)/(2*surface.HalfSize)+.5f)*surface.Resolution),0,surface.Resolution-1);
            int y=Mathf.Clamp((int)(((world.z-surface.Center.z)/(2*surface.HalfSize)+.5f)*surface.Resolution),0,surface.Resolution-1);
            return pixels[y*surface.Resolution+x];
        }
        private static void Require(bool value,string message){if(!value)throw new Exception(message);}
    }
}
