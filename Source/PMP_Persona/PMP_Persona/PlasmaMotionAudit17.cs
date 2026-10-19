using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using PMP_Persona.Rendering;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona
{
    // Opt-in multi-frame exercise of the public support renderer, including atlas-cached humans.
    public sealed class PlasmaMotionAudit17 : GameComponent
    {
        private int warmup, frame, firstFrame, maxCaptures;
        private bool finished;
        private readonly List<Pawn> pawns = new List<Pawn>();
        private readonly List<double> costs = new List<double>();
        private TargetOutlineCaptureCamera capture;
        private RenderTexture output;
        private Texture2D pixels;
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath,"PlasmaMotionAudit17");
        public PlasmaMotionAudit17(Game game) { }
        public override void GameComponentUpdate()
        {
            if(!GenCommandLine.CommandLineArgPassed("pmp17visualaudit") || finished || Find.CurrentMap==null || ++warmup<90) return;
            try
            {
                if(frame==0) Start();
                var watch=System.Diagnostics.Stopwatch.StartNew();
                foreach(var pawn in pawns)
                {
                    pawn.Rotation=new Rot4((frame/18)%4);
                    var renderer=pawn.Drawer.renderer;
                    renderer.DynamicDrawPhaseAt(DrawPhase.EnsureInitialized,pawn.DrawPos);
                    renderer.DynamicDrawPhaseAt(DrawPhase.ParallelPreDraw,pawn.DrawPos);
                    // A capture must not corrupt requests prepared for the actual map draw.
                    var requests=Traverse.Create(renderer.renderTree).Field("drawRequests").GetValue<List<PawnGraphicDrawRequest>>();
                    var saved=requests.ToArray();
                    TargetPlasmaRenderer.Draw(pawn,new Vector3(1,0,.45f),.8f);
                    if(!requests.SequenceEqual(saved)) throw new Exception("capture changed prepared map requests");
                    renderer.RenderPawnAt(pawn.DrawPos);
                }
                watch.Stop(); costs.Add(watch.Elapsed.TotalMilliseconds);
                maxCaptures=Math.Max(maxCaptures,TargetPlasmaRenderer.CapturesThisFrame);
                Color32[] withoutSurface=null;
                if(frame==18)
                {
                    capture.RenderTo(output,8f,()=>{});
                    withoutSurface=ReadPixels();
                    File.WriteAllBytes(Path.Combine(Folder,"Before-surface.png"),pixels.EncodeToPNG());
                }
                TargetPlasmaRenderer.Flush();
                if(frame%9==0)
                {
                    capture.RenderTo(output,8f,()=>{});
                    var old=RenderTexture.active;
                    try {
                        RenderTexture.active=output; pixels.ReadPixels(new Rect(0,0,768,768),0,0); pixels.Apply();
                        File.WriteAllBytes(Path.Combine(Folder,"Live-"+frame.ToString("00")+".png"),pixels.EncodeToPNG());
                        if(withoutSurface!=null) VerifySurfaceAbovePawn(withoutSurface,pixels.GetPixels32());
                    } finally { RenderTexture.active=old; }
                }
                if(++frame<72) return;
                if(TargetPlasmaRenderer.CachedTargetCount!=pawns.Count || pawns.Any(p=>TargetPlasmaRenderer.LastCaptureFrame(p)<firstFrame+30))
                    throw new Exception("crowd capture starvation");
                if(maxCaptures>8) throw new Exception("capture budget exceeded");
                var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();
                if(errors.Length>0) throw new Exception(string.Join("\n",errors));
                TargetPlasmaRenderer.Clear();
                if(TargetPlasmaRenderer.CachedTargetCount!=0) throw new Exception("cache not released");
                costs.Sort();
                File.WriteAllText(Path.Combine(Folder,"results.txt"),"PASS 32 targets across 72 rendered frames; actual queued pawn + plasma; prepared requests preserved; all targets refreshed; bounded captures; cache released; no Verse errors.\n"+
                    "CPU submission/capture median="+costs[costs.Count/2].ToString("F2")+"ms; p95="+costs[(int)(costs.Count*.95)].ToString("F2")+"ms (includes synchronous offscreen GPU capture; not GPU frame time).\n");
                Finish("PASS");
            }
            catch(Exception ex) { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder,"results.txt"),ex.ToString()); Finish("FAIL"); }
        }
        private Color32[] ReadPixels()
        {
            var old=RenderTexture.active;
            try { RenderTexture.active=output;pixels.ReadPixels(new Rect(0,0,768,768),0,0);pixels.Apply();return pixels.GetPixels32(); }
            finally {RenderTexture.active=old;}
        }
        private void VerifySurfaceAbovePawn(Color32[] before,Color32[] after)
        {
            Pawn pawn=pawns[2];
            using(var silhouette=TargetPlasmaRenderer.CaptureForAudit(pawn))
            {
                var image=new Texture2D(256,256,TextureFormat.RGBA32,false);
                var previous=RenderTexture.active;
                try
                {
                    RenderTexture.active=silhouette.Texture;image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                    var mask=image.GetPixels32();int interior=0,changed=0;
                    for(int y=4;y<252;y++)for(int x=4;x<252;x++)
                    {
                        int j=y*256+x;
                        if(mask[j].a<230||mask[j-4].a<230||mask[j+4].a<230||mask[j-1024].a<230||mask[j+1024].a<230)continue;
                        Vector3 world=pawn.DrawPos+new Vector3(((x+.5f)/256f-.5f)*silhouette.HalfSize*2,0,((y+.5f)/256f-.5f)*silhouette.HalfSize*2);
                        Vector3 uv=capture.GetComponent<Camera>().WorldToViewportPoint(world);
                        int px=(int)(uv.x*768),py=(int)(uv.y*768);
                        if(px<0||px>=768||py<0||py>=768)continue;
                        var a=before[py*768+px];var b=after[py*768+px];interior++;
                        if(Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>30)changed++;
                    }
                    File.WriteAllText(Path.Combine(Folder,"surface-coverage.txt"),changed+"/"+interior+" interior pixels visibly overlaid");
                    if(interior<100||changed<interior*.9f)throw new Exception("plasma hidden behind pawn: "+changed+"/"+interior);
                }
                finally {RenderTexture.active=previous;UnityEngine.Object.Destroy(image);}
            }
        }
        private void Start()
        {
            Directory.CreateDirectory(Folder); TargetPlasmaRenderer.Clear(); firstFrame=Time.frameCount;
            var map=Find.CurrentMap;
            for(int i=0;i<32;i++)
            {
                string kind=new[]{"Colonist","Muffalo","Mech_CentipedeBlaster","Colonist"}[i%4];
                var pawn=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind));
                IntVec3 cell=i<4 ? map.Center+new IntVec3((i%2)*6-3,0,(i/2)*6-3) : map.Center+new IntVec3(20+(i%7)*3,0,(i/7)*3);
                if(!cell.Standable(map)) cell=GenRadial.RadialCellsAround(cell,10,true).First(c=>c.InBounds(map)&&c.Standable(map));
                GenSpawn.Spawn(pawn,cell,map); pawns.Add(pawn);
                if(pawn.RaceProps.Humanlike) pawn.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_Duster"),ThingDefOf.Cloth));
                if(i==3) HealthUtility.DamageUntilDowned(pawn);
                else { var job=JobMaker.MakeJob(JobDefOf.Wait); job.expiryInterval=10000; pawn.jobs.StartJob(job,JobCondition.InterruptForced); }
            }
            capture=TargetOutlineCaptureCamera.Create();
            var camera=capture.GetComponent<Camera>();
            camera.transform.position=map.Center.ToVector3Shifted()+Vector3.up*20;
            camera.nearClipPlane=.1f;camera.farClipPlane=40;camera.cullingMask=1;
            camera.backgroundColor=new Color(.025f,.035f,.045f,1);
            output=new RenderTexture(768,768,24,RenderTextureFormat.ARGB32);output.Create();
            pixels=new Texture2D(768,768,TextureFormat.RGBA32,false);
        }
        private void Finish(string status)
        {
            finished=true;
            foreach(var pawn in pawns) if(!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
            TargetPlasmaRenderer.Clear();
            if(output!=null){output.Release();UnityEngine.Object.Destroy(output);}
            if(pixels!=null)UnityEngine.Object.Destroy(pixels);
            if(capture!=null)UnityEngine.Object.Destroy(capture.gameObject);
            File.WriteAllText(Path.Combine(Folder,"complete.txt"),status);Application.Quit();
        }
    }
}
