using ProtossMech;
using RimCampaign.SDK;
using UnityEngine;
using Verse;

namespace PMP.Campaign
{
    [CampaignModule("pmp.communication","CommunicationRenderer","噬星者通讯：透明底板与 WildHunt 能量描边")]
    public sealed class PersonaCommunicationRenderer : ICommunicationRenderer
    {
        public void Draw(CommunicationDrawContext context)
        {
            var color=GUI.color;
            try{
                GUI.color=Color.white;
                Widgets.DrawBoxSolid(context.TextPanel,new Color(.047f,.039f,.027f,context.BackgroundOpacity));
                // Reuse the list row's native energy material, without its opaque fill or armor corner.
                if(Event.current.type==EventType.Repaint)WarpConsoleUI.DrawEnergyFrame(context.Panel,.55f,.72f);
                if(context.Data.TryGetValue("portrait",out var portrait)&&portrait=="ca") {
                    DrawAssistant(context);
                    context.DrawDefaultText();context.DrawDefaultSpectrum();
                } else context.DrawDefaultContents();
            }finally{GUI.color=color;}
        }

        private static void DrawAssistant(CommunicationDrawContext context)
        {
            var rect=context.PortraitRect;var center=rect.center;
            float time=(float)context.Elapsed,scale=rect.width;
            Widgets.DrawBoxSolid(rect,new Color(.025f,.07f,.10f,.92f));
            for(int y=0;y<rect.height;y+=5)
                Widgets.DrawBoxSolid(new Rect(rect.x,rect.y+y,rect.width,1),new Color(.2f,.8f,1,.06f));
            for(int ring=0;ring<3;ring++) {
                float radius=scale*(.20f+ring*.10f);
                for(int segment=0;segment<48;segment++) {
                    if((segment+ring*3)%12>8)continue;
                    float angle=segment*Mathf.PI/24+time*(ring==1?-.18f:.12f);
                    var p=center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
                    var q=center+new Vector2(Mathf.Cos(angle+Mathf.PI/24),Mathf.Sin(angle+Mathf.PI/24))*radius;
                    Widgets.DrawLine(p,q,new Color(.25f,.83f,1,.45f+ring*.16f),ring==1?2:1);
                }
            }
            float pulse=1+.12f*Mathf.Sin(time*8)*Mathf.Sin(time*3.1f);
            var color=new Color(.55f,.95f,1,.95f);
            for(int i=0;i<4;i++) {
                float a=i*Mathf.PI/2;
                var p=center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*scale*.12f*pulse;
                var q=center+new Vector2(Mathf.Cos(a+Mathf.PI/2),Mathf.Sin(a+Mathf.PI/2))*scale*.12f*pulse;
                Widgets.DrawLine(p,q,color,2);
            }
            float scan=rect.y+Mathf.Repeat(time*.24f,1)*rect.height;
            Widgets.DrawBoxSolid(new Rect(rect.x+4,scan,rect.width-8,1),new Color(.4f,.9f,1,.45f));
        }
    }
}
