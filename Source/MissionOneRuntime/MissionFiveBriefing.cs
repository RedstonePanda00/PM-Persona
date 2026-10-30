using System;
using System.Collections.Generic;
using RimCampaign.Runtime;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Campaign {
 // North-up regional relief with a shared camera projection for terrain and markers.
 public sealed class MissionFiveBriefing {
  public bool Visible;
  Texture2D terrain,playerIcon,enemyIcon;Material material;Camera sensor;
  readonly RenderTexture[] captures=new RenderTexture[3];int captureIndex,settled=-1;
  float extent,arrowsStarted=-1,height=1.65f,previousFarClip;
  const float PanSeconds=1.35f;
  readonly float[] revealedAt={-1,-1,-1,-1};
  float stageStarted,panStarted,playerRevealedAt=-1,fromHeight=1.65f,toHeight=1.65f;
  int previousStage=-1,focusSite=-2;
  Vector2 focus,fromFocus,toFocus;
  static readonly Vector2 Center=new Vector2(.63f,.48f);
  static readonly Vector2[] Sites={new Vector2(-.44f,-.44f),new Vector2(-.44f,.44f),new Vector2(.44f,-.44f),new Vector2(.44f,.44f)};
  static readonly IntVec3[] Views={new IntVec3(162,0,162),new IntVec3(96,0,96),new IntVec3(228,0,228)};
  void Initialize(Map map){
   if(terrain!=null)return;extent=Mathf.Max(2.5f,UI.screenWidth/(float)UI.screenHeight*1.25f);
   const int n=2048;terrain=new Texture2D(n,n,TextureFormat.RGBA32,false,true){wrapMode=TextureWrapMode.Clamp,filterMode=FilterMode.Bilinear};
   var pixels=new Color32[n*n];
   for(int z=0;z<n;z++)for(int x=0;x<n;x++){
    float wx=((x+.5f)/n-.5f)*2*extent,wz=((z+.5f)/n-.5f)*2*extent;
    var cell=new IntVec3(Mathf.FloorToInt(162+wx*325),0,Mathf.FloorToInt(162+wz*325));
    float noise=Mathf.PerlinNoise(wx*12+31,wz*12+17),h=.08f+noise*.4f,road=0;
    if(cell.InBounds(map)){
     h=cell.GetEdifice(map)?.def.building?.isNaturalRock==true?.64f+noise*.2f:.04f+noise*.09f;
     road=cell.GetTerrain(map).defName=="Gravel"?.6f:0;
    }
    pixels[z*n+x]=new Color(h,0,road,1);
   }
   terrain.SetPixels32(pixels);terrain.Apply();material=DefDatabase<MaterialDef>.GetNamed("PMP17_CampaignTerrainMaterial").CreateMaterialInstance();
   playerIcon=ContentFinder<Texture2D>.Get("UI/Campaign/StarEatersInsignia");enemyIcon=ContentFinder<Texture2D>.Get("UI/Campaign/RomulusInsignia");
   sensor=new GameObject("Fifth mission terrain capture",typeof(Camera)).GetComponent<Camera>();sensor.enabled=false;
   previousFarClip=Find.CameraDriver.MyCamera.farClipPlane;Find.CameraDriver.MyCamera.farClipPlane=Mathf.Max(previousFarClip,1000);
  }
  void Capture(){
   if(captureIndex>=3)return;
   if(settled<0){Find.CameraDriver.SetRootPosAndSize(Views[captureIndex].ToVector3Shifted(),captureIndex==0?65:36);settled=Time.frameCount;return;}
   if(Time.frameCount-settled<12)return;
   var source=Find.CameraDriver.MyCamera;var previous=RenderTexture.active;
   try{
    var rt=new RenderTexture(640,360,24,RenderTextureFormat.ARGB32);rt.Create();captures[captureIndex]=rt;
    sensor.worldToCameraMatrix=source.worldToCameraMatrix;sensor.projectionMatrix=source.projectionMatrix;sensor.cullingMask=source.cullingMask;
    sensor.clearFlags=CameraClearFlags.Color;sensor.backgroundColor=new Color(.02f,.03f,.03f);sensor.targetTexture=rt;sensor.Render();captureIndex++;settled=-1;
   }finally{sensor.targetTexture=null;RenderTexture.active=previous;}
  }
  void Animate(int stage){
   float now=Time.realtimeSinceStartup;
   if(stage!=previousStage){previousStage=stage;stageStarted=now;}
   int site=stage==2?(now-stageStarted<2.2f?3:2):stage==3?1:stage==4?0:-1;
   float targetHeight=stage==0?1.65f:site>=0?.95f:1.12f;
   if(site!=focusSite||targetHeight!=toHeight){
    fromFocus=focus;fromHeight=height;toFocus=site<0?Vector2.zero:Sites[site];toHeight=targetHeight;panStarted=now;focusSite=site;
    if(site>=0&&revealedAt[site]<0)revealedAt[site]=now+.85f;
   }
   float blend=Mathf.SmoothStep(0,1,(now-panStarted)/PanSeconds);
   focus=Vector2.Lerp(fromFocus,toFocus,blend);height=Mathf.Lerp(fromHeight,toHeight,blend);
   if(stage>=1&&playerRevealedAt<0)playerRevealedAt=now+.3f;
   if(stage>=5&&arrowsStarted<0)arrowsStarted=panStarted+PanSeconds;
  }
  Vector2 Point(Vector2 world){float h=terrain.GetPixelBilinear(world.x/(2*extent)+.5f,world.y/(2*extent)+.5f).r*.24f;return new Vector2(Center.x*UI.screenWidth+(world.x-focus.x)/(height-h)*UI.screenHeight,Center.y*UI.screenHeight-(world.y-focus.y)/(height-h)*UI.screenHeight);}
  void Icon(Texture2D texture,Vector2 world,float size,float revealed){
   if(revealed<0||Time.realtimeSinceStartup<=revealed)return;
   size*=Mathf.SmoothStep(0,1,(Time.realtimeSinceStartup-revealed)/.65f);
   var p=Point(world);GUI.DrawTexture(new Rect(p.x-size/2,p.y-size/2,size,size),texture,ScaleMode.ScaleToFit);
  }
  void Arrow(Vector2 start,Vector2 end,float progress){
   if(progress<=0)return;
   var delta=end-start;var bend=new Vector2(-delta.y,delta.x).normalized*45;var control=(start+end)/2+bend;
   Vector2 At(float t)=>(1-t)*(1-t)*start+2*(1-t)*t*control+t*t*end;
   float tip=Mathf.SmoothStep(0,1,progress);var gold=new Color(1,.74f,.2f,.9f);var previous=start;
   for(int i=1;i<=25;i++){var next=At(tip*i/25);Widgets.DrawLine(previous,next,gold,2);previous=next;}
   var tangent=(At(tip)-At(Mathf.Max(0,tip-.025f))).normalized;var side=new Vector2(-tangent.y,tangent.x);
   float head=Mathf.Min(12,delta.magnitude*tip*.25f);
   Widgets.DrawLine(previous,previous-tangent*head+side*head*.5f,gold,2);Widgets.DrawLine(previous,previous-tangent*head-side*head*.5f,gold,2);
  }
  public void Draw(CampaignController c){
   Initialize(c.Map);int stage=(int)c.State.Variables.GetValueOrDefault("m5-brief-stage");
   Animate(stage);
   var screen=new Rect(0,0,UI.screenWidth,UI.screenHeight);
   if(Event.current.type==EventType.Repaint){
    Capture();material.SetVector("_Scene",new Vector4(Time.realtimeSinceStartup,screen.width/screen.height,extent,terrain.width));
    material.SetVector("_Eye",new Vector4(focus.x,height,focus.y,Center.x));material.SetVector("_Forward",new Vector4(0,-1,0,Center.y));
    material.SetVector("_Right",new Vector4(1,0,0,0));material.SetVector("_Up",new Vector4(0,0,1,0));Graphics.DrawTexture(screen,terrain,material);
   }
   float scale=Mathf.Min(screen.width/1600,screen.height/900),size=48*scale;
   if(stage>=1){
    Vector2[] windows={new Vector2(.79f,.46f),new Vector2(.44f,.80f),new Vector2(.43f,.06f)};
    for(int i=0;i<3;i++)if(captures[i]!=null){
     var anchor=Point(new Vector2((Views[i].x-162)/325f,(Views[i].z-162)/325f));
     var r=new Rect(windows[i].x*screen.width,windows[i].y*screen.height,225*scale,126*scale);r.x=Mathf.Min(r.x,screen.width-r.width-16);r.y=Mathf.Min(r.y,screen.height-r.height-16);
     Widgets.DrawLine(anchor,new Vector2(Mathf.Clamp(anchor.x,r.x,r.xMax),Mathf.Clamp(anchor.y,r.y,r.yMax)),new Color(1,.78f,.25f),1);
     GUI.DrawTexture(r,captures[i],ScaleMode.ScaleAndCrop);var old=GUI.color;GUI.color=new Color(1,.78f,.25f);Widgets.DrawBox(r);GUI.color=old;
    }
   }
   if(stage>=5){
    foreach(var site in Sites)Arrow(Point(site),Point(Vector2.zero),Mathf.Clamp01((Time.realtimeSinceStartup-arrowsStarted)/2));
   }
   Icon(playerIcon,Vector2.zero,size*1.2f,playerRevealedAt);
   for(int i=0;i<4;i++)Icon(enemyIcon,Sites[i],size,revealedAt[i]);
  }
  public void End(){
   if(sensor!=null&&Find.CameraDriver!=null)Find.CameraDriver.MyCamera.farClipPlane=previousFarClip;
   Visible=false;foreach(var rt in captures)if(rt!=null){rt.Release();UnityEngine.Object.Destroy(rt);}Array.Clear(captures,0,captures.Length);
   if(sensor!=null)UnityEngine.Object.Destroy(sensor.gameObject);if(terrain!=null)UnityEngine.Object.Destroy(terrain);if(material!=null)UnityEngine.Object.Destroy(material);
   sensor=null;terrain=null;material=null;captureIndex=0;settled=-1;arrowsStarted=playerRevealedAt=-1;
   previousStage=-1;focusSite=-2;focus=fromFocus=toFocus=Vector2.zero;height=fromHeight=toHeight=1.65f;
   for(int i=0;i<4;i++)revealedAt[i]=-1;
  }
 }
}
