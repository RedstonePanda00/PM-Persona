using System;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Campaign {
    public sealed class MissionTwoPresentation {
        public bool Briefing;
        bool third;
        public float Elapsed;
        Material terrainMaterial;
        Texture2D terrain,insignia;
        const int TerrainResolution=1024;
        static readonly Vector2 ScreenCenter=new Vector2(.60f,.43f);
        Color32[] terrainPixels;
        float terrainExtent,noiseX,noiseZ,riverPhase;
        int terrainSeed;
        bool seeded;
        readonly RenderTexture[] captures=new RenderTexture[3];
        Camera sensor;
        int captureIndex,settledFrame=-1;
        string banner="";
        float bannerUntil;
        public void Banner(string text){banner=text;bannerUntil=Time.realtimeSinceStartup+4;}
        public void End(){Briefing=false;banner="";bannerUntil=0;Release();}
        void Release(){foreach(var rt in captures)if(rt!=null){rt.Release();UnityEngine.Object.Destroy(rt);}Array.Clear(captures,0,captures.Length);if(sensor!=null)UnityEngine.Object.Destroy(sensor.gameObject);sensor=null;if(terrain!=null)UnityEngine.Object.Destroy(terrain);terrain=null;terrainPixels=null;seeded=false;if(terrainMaterial!=null)UnityEngine.Object.Destroy(terrainMaterial);terrainMaterial=null;captureIndex=0;settledFrame=-1;}
        // Fictional regional relief for the holographic briefing, independent of the playable map.
        float Valley(float x,float z)=>x-(.49f+(third?.18f:.10f)*Mathf.Sin(z*(third?3.5f:7f)+riverPhase));
        float Relief(float x,float z){
            float valley=Valley(x,z);
            float banks=1-Mathf.Exp(-valley*valley/(third?.035f:.009f));
            float noise=.54f*Mathf.PerlinNoise(x*4+noiseX,z*4+noiseZ)+.29f*Mathf.PerlinNoise(x*9+noiseX+31,z*9+noiseZ+7)+.17f*Mathf.PerlinNoise(x*21+noiseX+9,z*21+noiseZ+61);
            return .025f+banks*Mathf.Pow(noise,third?1.25f:1.65f)*.94f;
        }
        Vector2 Anchor(int index)=>third&&index==0?new Vector2(.15f,.50f):index==0?new Vector2(.48f,.43f):index==1?new Vector2(.68f,.37f):new Vector2(.55f,.68f);
        void Initialize(){
            float extent=Mathf.Max(6f,UI.screenWidth/(float)UI.screenHeight*3.5f);
            if(terrain!=null&&terrainExtent>=extent)return;
            // A private random source changes only the briefing, never gameplay RNG.
            if(!seeded){terrainSeed=Guid.NewGuid().GetHashCode();var random=new System.Random(terrainSeed);noiseX=(float)random.NextDouble()*1000;noiseZ=(float)random.NextDouble()*1000;riverPhase=(float)random.NextDouble()*Mathf.PI*2;seeded=true;}
            if(terrain!=null)UnityEngine.Object.Destroy(terrain);
            terrainExtent=extent;
            const int resolution=TerrainResolution;
            // Height/river/road are data channels, so GPU sampling must not apply sRGB decoding.
            terrain=new Texture2D(resolution,resolution,TextureFormat.RGBA32,false,true);terrain.wrapMode=TextureWrapMode.Clamp;terrain.filterMode=FilterMode.Bilinear;
            var colors=new Color32[resolution*resolution];
            for(int z=0;z<resolution;z++)for(int x=0;x<resolution;x++){
                float u=((x+.5f)/resolution-.5f)*2*extent+.5f,v=((z+.5f)/resolution-.5f)*2*extent+.5f,valley=Valley(u,v);
                float river=Mathf.Exp(-valley*valley/.00015f);
                float road=Mathf.Exp(-Mathf.Pow(u-(.62f+.05f*Mathf.Sin(v*11+riverPhase)),2)/.00010f);
                colors[z*resolution+x]=new Color(Relief(u,v),river,road,1);
            }
            terrainPixels=colors;terrain.SetPixels32(colors);terrain.Apply();if(terrainMaterial==null)terrainMaterial=DefDatabase<MaterialDef>.GetNamed("PMP17_CampaignTerrainMaterial").CreateMaterialInstance();
            insignia=ContentFinder<Texture2D>.Get("UI/Campaign/RomulusInsignia");
            if(sensor==null){sensor=new GameObject("Persona mission briefing capture",typeof(Camera)).GetComponent<Camera>();sensor.enabled=false;}
        }
        void Capture(CampaignController c){
            if(captureIndex>=3)return;
            string[] targets=c.NodeId=="mission-three"?new[]{"camp","brief-defenses","anomaly-hub"}:new[]{"brief-outpost","brief-defenses","anomaly-hub"};var p=c.MapSpec.Objects.Single(o=>o.Id==targets[captureIndex]);
            if(settledFrame<0){Find.CameraDriver.SetRootPosAndSize(new Vector3(p.Position.X,0,p.Position.Z),captureIndex==0?45:20);settledFrame=Time.frameCount;return;}
            if(Time.frameCount-settledFrame<3)return;
            var source=Find.CameraDriver.MyCamera;var previous=RenderTexture.active;
            try{
                var rt=new RenderTexture(640,360,24,RenderTextureFormat.ARGB32);rt.Create();captures[captureIndex]=rt;
                sensor.worldToCameraMatrix=source.worldToCameraMatrix;sensor.projectionMatrix=source.projectionMatrix;sensor.cullingMask=source.cullingMask;sensor.clearFlags=CameraClearFlags.Color;sensor.backgroundColor=new Color(.02f,.03f,.03f);sensor.targetTexture=rt;sensor.Render();
                captureIndex++;settledFrame=-1;
            }finally{sensor.targetTexture=null;RenderTexture.active=previous;}
        }
        public void Draw(CampaignController c){
            if(!MissionScope.Owns(c,Find.CurrentMap)||c.Transitioning)return;
            if(Briefing){
                third=c.NodeId=="mission-three";Initialize();var screen=new Rect(0,0,UI.screenWidth,UI.screenHeight);
                if(Event.current.type==EventType.Repaint){
                    Capture(c);CameraBasis(out var eye,out var forward,out var right,out var up);
                    terrainMaterial.SetVector("_Scene",new Vector4(Elapsed,screen.width/screen.height,terrainExtent,TerrainResolution));
                    terrainMaterial.SetVector("_Eye",new Vector4(eye.x,eye.y,eye.z,ScreenCenter.x));terrainMaterial.SetVector("_Forward",new Vector4(forward.x,forward.y,forward.z,ScreenCenter.y));terrainMaterial.SetVector("_Right",right);terrainMaterial.SetVector("_Up",up);
                    Graphics.DrawTexture(screen,terrain,terrainMaterial);
                }
                float scale=Mathf.Min(screen.width/1600,screen.height/900),logoSize=42*scale;
                DrawInsignia(insignia,Anchor(third?1:0),screen,logoSize);
                var empire=DefDatabase<FactionDef>.GetNamedSilentFail("Empire")?.FactionIcon;
                if(empire!=null)DrawInsignia(empire,third?Anchor(0):new Vector2(.36f,.43f),screen,logoSize);
                Label(new Rect(screen.width*.30f,24*scale,screen.width*.4f,36*scale),c.NodeId=="mission-three"?"地形扫描 / 军团集结圣所":"地形扫描 / 异常前哨",21*scale,new Color(1,.82f,.3f));
                Vector2[] offsets={new Vector2(-255,-170),new Vector2(95,120),new Vector2(65,-235)};
                for(int i=0;i<3;i++){
                    float reveal=Mathf.Clamp01((Elapsed-(6+i*6))/.45f);if(reveal<=0||captures[i]==null)continue;
                    var point=Project(i,screen);float w=235*scale,h=132*scale;var r=new Rect(point.x+offsets[i].x*scale,point.y+offsets[i].y*scale,w,h);
                    r.width*=Mathf.SmoothStep(0,1,reveal);var col=GUI.color;GUI.color=new Color(1,.8f,.35f,reveal);Widgets.DrawLine(point,new Vector2(Mathf.Clamp(point.x,r.x,r.xMax),Mathf.Clamp(point.y,r.y,r.yMax)),GUI.color,1);Widgets.DrawBox(new Rect(point.x-3,point.y-3,6,6),1);GUI.color=new Color(1,1,1,reveal);GUI.DrawTexture(r,captures[i],ScaleMode.ScaleAndCrop);GUI.color=new Color(1,.8f,.35f,reveal);Widgets.DrawBox(r,1);GUI.color=col;
                }
            }
            if(Time.realtimeSinceStartup<bannerUntil){float remaining=bannerUntil-Time.realtimeSinceStartup;float alpha=Mathf.Min(1,Mathf.Min(remaining,4-remaining)*2);Label(new Rect(0,UI.screenHeight*.73f,UI.screenWidth,60),banner,36,new Color(.94f,.16f,.10f,alpha));}
        }
        // The shader receives this basis; UI markers use the same camera and uploaded height samples.
        void CameraBasis(out Vector3 eye,out Vector3 forward,out Vector3 right,out Vector3 up){
            float angle=Elapsed*.018f-.24f;
            float distance=Mathf.Lerp(1.5f,1.02f,Mathf.SmoothStep(0,1,Mathf.Clamp01((Elapsed-3)/1.1f)));
            eye=new Vector3(Mathf.Sin(angle)*distance,distance*.86f,Mathf.Cos(angle)*distance);
            forward=(new Vector3(0,.035f,0)-eye).normalized;right=Vector3.Cross(forward,Vector3.up).normalized;up=Vector3.Cross(right,forward);
        }
        float SurfaceHeight(Vector2 p){
            float x=((p.x-.5f)/(terrainExtent*2)+.5f)*TerrainResolution-.5f,z=((p.y-.5f)/(terrainExtent*2)+.5f)*TerrainResolution-.5f;
            int ix=Mathf.FloorToInt(x),iz=Mathf.FloorToInt(z);
            float Pixel(int a,int b)=>terrainPixels[Mathf.Clamp(b,0,TerrainResolution-1)*TerrainResolution+Mathf.Clamp(a,0,TerrainResolution-1)].r/255f;
            return Mathf.Lerp(Mathf.Lerp(Pixel(ix,iz),Pixel(ix+1,iz),x-ix),Mathf.Lerp(Pixel(ix,iz+1),Pixel(ix+1,iz+1),x-ix),z-iz)*.24f;
        }
        Vector2 Project(int index,Rect screen)=>ProjectLocation(Anchor(index),screen);
        Vector2 ProjectLocation(Vector2 p,Rect screen){
            CameraBasis(out var eye,out var forward,out var right,out var up);
            var delta=new Vector3(p.x-.5f,SurfaceHeight(p),p.y-.5f)-eye;
            float z=Vector3.Dot(delta,forward);return new Vector2(screen.x+screen.width*ScreenCenter.x+Vector3.Dot(delta,right)/z*screen.height,screen.y+screen.height*ScreenCenter.y-Vector3.Dot(delta,up)/z*screen.height);
        }
        void DrawInsignia(Texture2D icon,Vector2 location,Rect screen,float size){var p=ProjectLocation(location,screen);GUI.DrawTexture(new Rect(p.x-size/2,p.y-size,size,size),icon,ScaleMode.ScaleToFit);}
        static void Label(Rect rect,string text,float size,Color tint){var old=GUI.color;GUI.color=tint;GUI.Label(rect,RcText.T(text),new GUIStyle(Text.CurFontStyle){fontSize=Math.Max(13,(int)size),alignment=TextAnchor.MiddleCenter});GUI.color=old;}
    }
    [CampaignModule("pmp.m2.brief","Action","Romulus tactical scan presentation.")]
    public sealed class MissionTwoBrief:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;if(!MissionScope.Matches(c))return true;var view=MissionEffectsMapComponent.CurrentPresentation;if(view==null)return true;string key="brief:"+a.Id;c.State.Variables.TryGetValue(key,out var elapsed);elapsed+=delta;c.State.Variables[key]=elapsed;view.Elapsed=(float)elapsed;if(elapsed>=a.Value){view.End();return true;}view.Briefing=true;return false;}
    }
    [CampaignModule("pmp.m2.banner","Action","Mission-two boss title.")]
    public sealed class MissionTwoBanner:IActionModule {public bool Execute(Act a,IHost host,string source,double delta){if(MissionScope.Matches(host as CampaignController,"mission-two"))MissionEffectsMapComponent.CurrentPresentation?.Banner(a.Text);return true;}}
}
