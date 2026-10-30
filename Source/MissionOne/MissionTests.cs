using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;

static class MissionTests
{
    sealed class Host:IHost {
        public RunState State;public int Choices;public List<string> Lines=new List<string>();
        public double Read(string key)=>0;
        public bool Execute(Act a,string source,double delta){if(a.Kind=="rc.random")State.Variables[a.Target]=1;if(a.Kind=="rc.choice")Choices++;if(a.Kind=="Communication")Lines.Add(a.Text);return true;}
    }
    static void Check(bool v,string text){if(!v)throw new Exception("Mission test failed: "+text);}
    public static void Run(Project p){
        foreach(var order in new[]{new[]{"A01","A02","A03"},new[]{"A01","A03","A02"},new[]{"A02","A01","A03"},new[]{"A02","A03","A01"},new[]{"A03","A01","A02"},new[]{"A03","A02","A01"}}){
            var state=new RunState();var host=new Host{State=state};var engine=new Engine(p.Maps[0],state,host);engine.Start();engine.Tick(0,0);
            foreach(var id in order){host.Lines.Clear();engine.Emit("InteractionCompleted",id);engine.Tick(0,0);
                Check(host.Choices==(state.Variables.ContainsKey("found:A03")?1:0),"choice timing for "+string.Join(",",order)+" at "+id);
                if(id!="A03"&&state.Variables.ContainsKey("found:A03"))Check(!host.Lines.Any(t=>t.Contains("能不能找到有用")||t.Contains("就只有这些怪谈")),"post-intel comments consistent");
            }
            engine.Emit("InteractionCompleted","A03");engine.Tick(0,0);Check(host.Choices==1&&state.Variables["files"]==3,"duplicate read idempotent");
        }
        var restored=Json.Copy(p);Check(restored.Maps[0].Stealth.Conversations.Count==15,"editor JSON preserves patrol dialogue");
        Check(restored.Maps[0].Stealth.Conversations.All(t=>t.HearThroughWalls),"all authored patrol conversations can be heard through walls");
        Check(new[]{"supply-blackhat","supply-scarab"}.All(id=>restored.Maps[0].Rules.Single(r=>r.Id==id).Actions[0].Kind=="rc.pmcWarp"),"both equipment rewards use the existing PMC warp implementation");
        Check(restored.Maps[0].Stealth.PlayerCornerPeek&&restored.Maps[0].Stealth.PlayerHearing&&restored.Maps[0].Stealth.PlayerSight==20,"editor JSON preserves wider player vision, corner peeking and hearing");
        Check(restored.Maps[0].Stealth.Guards.All(g=>g.ReportTicks==1200),"all guards allow twenty seconds to interrupt a report");
        Check(restored.Maps[0].Stealth.Devices.Where(d=>d.Kind=="Camera").All(d=>d.DetectionTicks==180),"all cameras retain three-second continuous detection timing");
        var blackhat=restored.Maps[0].Stealth.Devices.Single(x=>x.Id=="blackhat");Check(blackhat.OneShot&&blackhat.RequiredVariable=="aux-off","editor JSON preserves device prerequisite");
        Console.WriteLine("PASS mission logic: all six terminal orders, immediate choice, contextual dialogue, reread idempotency, authoring round-trip.");
    }
}
