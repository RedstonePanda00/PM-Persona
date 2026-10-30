using System;
using RimCampaign.Core;
using RimCampaign.Runtime;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PMP.Campaign
{
    public sealed class PersonaMissionResultWindow : MissionResultWindow
    {



        Material insigniaMaterial;
        Texture2D insignia;
        float started;
        bool reveal;
        int tab;
        Vector2 scroll;
        static readonly Color Ivory=new Color(.91f,.91f,.85f),Gold=new Color(1f,.70f,.27f),Muted=new Color(.64f,.65f,.64f);
        public override Vector2 InitialSize=>new Vector2(UI.screenWidth,UI.screenHeight);
        protected override float Margin=>0;
        static bool Native(string name){var def=DefDatabase<MaterialDef>.GetNamed(name);return def.shader?.Shader?.isSupported==true&&def.shader.Shader.name==def.shader.defName;}
        public override bool NativeShaderReady=>insigniaMaterial!=null&&Native(spec.Material)&&Native("PM17N_Mat_WarpConsoleHoneycombFlow")&&Native("PM17N_Mat_WarpConsoleEnergyFrame");
        public PersonaMissionResultWindow(CampaignController controller) : base(controller)
        {

            forcePause=true;absorbInputAroundWindow=true;closeOnCancel=false;closeOnAccept=false;
            doWindowBackground=false;preventCameraMotion=true;
            layer=WindowLayer.Dialog;
        }
        public override void PreOpen()
        {
            base.PreOpen();started=Time.realtimeSinceStartup;SoundDefOf.TinyBell.PlayOneShotOnCamera();
            if(!string.IsNullOrEmpty(spec.Material)){
                var def=DefDatabase<MaterialDef>.GetNamed(spec.Material);
                if(def.shader?.Shader?.isSupported!=true||def.shader.Shader.name!=def.shader.defName)throw new InvalidOperationException("Campaign results native shader unavailable: "+spec.Material);
                insigniaMaterial=def.CreateMaterialInstance();insigniaMaterial.SetFloat("_Frame",1);insigniaMaterial.SetFloat("_Aspect",1);
                insignia=ContentFinder<Texture2D>.Get("UI/Campaign/StarEatersInsignia");
            }
        }
        public override void PostClose(){if(insigniaMaterial!=null)UnityEngine.Object.Destroy(insigniaMaterial);insigniaMaterial=null;base.PostClose();}
        public override void Reveal()=>reveal=true;
        static void Label(Rect rect,string value,int size,Color color,TextAnchor anchor=TextAnchor.MiddleLeft)
        {
            var style=new GUIStyle(Text.CurFontStyle){fontSize=size,alignment=anchor,wordWrap=true};
            var old=GUI.color;GUI.color=color;GUI.Label(rect,RcText.T(value),style);GUI.color=old;
        }
        static bool Button(Rect rect,string text,bool selected=false)=>WarpConsoleUI.ButtonText(rect,RcText.T(text),selected:selected);
        public override void DoWindowContents(Rect r)
        {
            windowRect=new Rect(0,0,UI.screenWidth,UI.screenHeight);r=new Rect(0,0,windowRect.width,windowRect.height);
            float t=Time.realtimeSinceStartup-started,phase=reveal?1:Mathf.SmoothStep(0,1,Mathf.Clamp01((t-1.6f)/.8f));
            var oldColor=GUI.color;var oldFont=Text.Font;var oldAnchor=Text.Anchor;
            try{
                GUI.color=Color.white;Text.Font=GameFont.Small;Text.Anchor=TextAnchor.UpperLeft;
                Widgets.DrawBoxSolid(r,new Color(.012f,.014f,.017f));
                if(Event.current.type==EventType.Repaint)WarpConsoleUI.DrawHoneycomb(r,Mathf.Lerp(.66f,.28f,phase));
                float w=Mathf.Min(1120,r.width-64),x=(r.width-w)/2;
                if(Event.current.type==EventType.Repaint&&insigniaMaterial!=null){
                    float size=Mathf.Lerp(Mathf.Min(340,r.height*.44f),86,phase);
                    var logoRect=new Rect(Mathf.Lerp((r.width-size)/2,x+w-size-8,phase),Mathf.Lerp(r.height*.30f,57,phase),size,size);
                    insigniaMaterial.SetFloat("_Progress",Mathf.Clamp01(t/.9f));
                    Graphics.DrawTexture(logoRect,insignia,insigniaMaterial);
                }
                Label(new Rect(x,35,w,26),spec.Faction,16,Gold,TextAnchor.MiddleCenter);
                Label(new Rect(x,67,w,64),spec.Heading,42,Ivory,TextAnchor.MiddleCenter);
                Label(new Rect(x,134,w,32),result.Title,20,Muted,TextAnchor.MiddleCenter);
                if(phase<1){Label(new Rect(x,r.height-66,w,24),RcText.T("点击任意位置展开任务报告"),15,Muted,TextAnchor.MiddleCenter);if(Event.current.type==EventType.MouseDown){Reveal();Event.current.Use();}return;}
                float top=196,bottom=r.height-100,h=Mathf.Max(150,bottom-top),left=w*.29f,gap=30,rightX=x+left+gap,rightW=w-left-gap;
                WarpConsoleUI.DrawPanel(new Rect(x,top,left,h));
                Label(new Rect(x+20,top+14,left-40,26),spec.StealthRating?RcText.T("潜行表现"):RcText.T("任务报告"),17,Gold);
                if(spec.StealthRating){
                    Label(new Rect(x,top+44,left,82),result.Performance.Rank(spec),70,Ivory,TextAnchor.MiddleCenter);
                    Label(new Rect(x,top+126,left,28),result.Performance.Score(spec)+" / 100",22,Gold,TextAnchor.MiddleCenter);
                    string[] names={RcText.T("识破来源"),RcText.T("发起汇报"),RcText.T("敌方死亡"),RcText.T("非致命制伏")};
                    int[] values={result.Performance.DetectedBy.Count,result.Performance.Reporters.Count,result.Performance.Killed.Count,result.Performance.SurvivingSubdued};
                    for(int i=0;i<4;i++){
                        Label(new Rect(x+20,top+166+i*26,left-78,26),names[i],16,Muted);
                        Label(new Rect(x+left-60,top+166+i*26,40,26),values[i].ToString(),19,Ivory,TextAnchor.MiddleRight);
                    }
                    TooltipHandler.TipRegion(new Rect(x,top,left,310),RcText.T("识破：每名守卫／摄像头首次完全识破计一次，黄色怀疑不计。\n汇报：每名守卫首次打算汇报计一次。\n敌方死亡：本关受管理守卫的死亡，含制伏后死亡。\n非致命制伏：成功制伏且结算时仍存活的守卫。\n满分 100；每个识破来源 −")+spec.DetectionPenalty+RcText.T("，每名汇报者 −")+spec.ReportPenalty+RcText.T("，每名死亡敌人 −")+spec.KillPenalty+RcText.T("。制伏和用时不加减分。\nS：100；A：85–99；B：65–84；C：40–64；D：0–39。"));
                }
                var duration=TimeSpan.FromSeconds(Math.Max(0,result.Seconds));
                Label(new Rect(x+20,bottom-64,left-40,24),RcText.T("行动用时  ")+((int)duration.TotalMinutes).ToString("00")+":"+duration.Seconds.ToString("00"),16,Muted);
                Label(new Rect(x+20,bottom-34,left-40,20),RcText.T("按游戏推进时间统计"),12,Muted);
                if(Button(new Rect(rightX,top,rightW/2-5,38),RcText.T("行动成果"),tab==0)){tab=0;scroll=Vector2.zero;}
                if(Button(new Rect(rightX+rightW/2+5,top,rightW/2-5,38),RcText.T("随身物资"),tab==1)){tab=1;scroll=Vector2.zero;}
                var area=new Rect(rightX,top+50,rightW,h-50);WarpConsoleUI.DrawPanel(area);
                var rows=tab==0?result.Objectives:result.Cargo;
                string summary=string.IsNullOrEmpty(result.Summary)?RcText.T("行动完成，调查结果已保存。"):RcText.T(result.Summary);
                float summaryHeight=tab==0?Mathf.Max(52,Text.CalcHeight(summary,rightW-56)+24):48;
                var view=new Rect(0,0,rightW-22,Mathf.Max(area.height-2,summaryHeight+rows.Count*54+20));
                Widgets.BeginScrollView(area,ref scroll,view);
                Label(new Rect(18,8,view.width-36,summaryHeight-8),tab==0?summary:RcText.T("背包中的实物；进入后续关卡时按实际状态继承。"),16,Muted);
                float y=summaryHeight;
                foreach(var row in rows){
                    Widgets.DrawBoxSolid(new Rect(18,y,view.width-36,1),new Color(.36f,.30f,.20f,.65f));
                    Label(new Rect(18,y+7,view.width-138,42),row.Label,16,Ivory);
                    Label(new Rect(view.width-112,y+7,94,42),row.Value,16,Gold,TextAnchor.MiddleRight);y+=54;
                }
                if(rows.Count==0)Label(new Rect(18,y,view.width-36,40),RcText.T("无"),16,Muted);
                Widgets.EndScrollView();
                int buttonCount=controller.HasNextMission?4:3;
                float buttonW=(w-(buttonCount-1)*10)/buttonCount,by=r.height-72;
                if(Button(new Rect(x,by,buttonW,42),RcText.T("保存进度")))Find.WindowStack.Add(new Dialog_SaveFileList_Save());
                if(Button(new Rect(x+buttonW+10,by,buttonW,42),RcText.T("重试本关"))){Close();controller.Retry();}
                if(Button(new Rect(x+(buttonW+10)*2,by,buttonW,42),RcText.T("返回主菜单")))Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(RcText.T("返回主菜单？请先保存需要保留的通关进度。"),()=>GenScene.GoToMainMenu()));
                if(controller.HasNextMission){if(Button(new Rect(x+(buttonW+10)*3,by,buttonW,42),RcText.T("继续战役"))){Close();controller.ContinueResult();}}
            }finally{GUI.color=oldColor;Text.Font=oldFont;Text.Anchor=oldAnchor;}
        }
    }
}
