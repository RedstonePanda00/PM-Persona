using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;

static class MissionFourTests {
 sealed class Host:IHost,IConditionHost {
  public RunState State;public bool Suppressed,Recalled;public List<string> Calls=new List<string>();
  public double Read(string key)=>key.StartsWith("timer:")?State.Timers.GetValueOrDefault(key.Substring(6)):0;
  public bool Evaluate(Condition c)=>c.Op=="rc.targetsCleared"?false:throw new Exception("Unknown test condition "+c.Op);
  public bool Execute(Act a,string source,double dt){Calls.Add(a.Kind+":"+a.Target);if(a.Kind=="pmp.suppressSubnode")Suppressed=true;if(a.Kind=="rc.pmcRecall")Recalled=true;if(a.Kind=="rc.finish"){if(!Suppressed||!Recalled)throw new Exception("Premature completion");State.WantSuccess=true;}return true;}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception("Mission four: "+message);}
 static (RunState,Host,Engine) Started(MapSpec map){var state=new RunState();var host=new Host{State=state};var engine=new Engine(map,state,host);engine.Start();for(int i=0;i<10;i++)engine.Tick(1,1);return (state,host,engine);}
 public static void Run(Project project){
  var map=project.Maps.Single(m=>m.Id=="romulus-subnode");
  foreach(string ev in new[]{"Death","Downed"}){
   var (s,h,e)=Started(map);e.EmitForRule("m4-mason-"+ev.ToLowerInvariant(),ev,"hero");e.Tick(0,0);e.Tick(0,0);
   Check(s.Phase=="Defeated","Mason "+ev+" fails with checkpoint retry path");
  }
  var (state,host,engine)=Started(map);
  Check(!state.Variables.ContainsKey("m4-stage"),"installation does not start automatically");
  engine.Enqueue(map.Interactions.Single().Actions);engine.Tick(0,0);
  for(int i=0;i<30;i++)engine.Tick(1,1);
  Check(!host.Suppressed&&!host.Recalled,"no completion during first half of installation");
  state=Json.Copy(state);host.State=state;engine=new Engine(map,state,host);
  for(int i=0;i<29;i++)engine.Tick(1,1);
  Check(!host.Suppressed&&!host.Recalled,"saved timer does not restart or finish before sixty seconds");
  for(int i=0;i<25&&state.Phase!="Succeeded";i++)engine.Tick(1,1);
  Check(state.Phase=="Succeeded"&&host.Suppressed&&host.Recalled,"solo mainline succeeds after suppression and recall");
  Check(map.Objectives.Where(o=>!o.Required).All(o=>state.Objectives[o.Id]!="Complete"),"uncompleted optional outposts stay uncompleted in solo result");
  Check(Enumerable.Range(1,3).All(i=>state.Firings["m4-wave-"+i+"-trigger"]==1),"three waves each run once across restored installation");
  Check(!map.Objectives.Any(o=>o.Id=="m4-recall"),"automatic recall is not a mission objective");
  Check(host.Calls.FindIndex(s=>s.StartsWith("rc.pmcRecall:"))<host.Calls.FindIndex(s=>s.StartsWith("pmp.suppressSubnode:")),"node collapse follows completed recall");
  Console.WriteLine("PASS mission four: down/death defeat, solo victory without optional objectives, sixty-second saved timer, all three waves and recall ordering.");
 }
}
