using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;

static class MissionTwoObjectiveTests
{
    sealed class Host : IHost
    {
        public bool HoldRendezvous;
        public double Read(string key) => 0;
        public bool Execute(Act action, string source, double delta) => action.Kind != "rc.joinActor" || !HoldRendezvous;
    }
    static void Check(bool value, string message) { if (!value) throw new Exception("Mission two objectives: " + message); }
    static void Drain(Engine engine, RunState state)
    {
        for (int i = 0; i < 100 && state.Fibers.Count > 0; i++) engine.Tick(1, 1);
    }
    static void Visible(Engine engine, params string[] ids) => Check(engine.VisibleObjectives().Select(o => o.Id).SequenceEqual(ids), "visible objectives must be " + string.Join(",", ids));
    public static void Run(Project project)
    {
        var map = Json.Copy(project.Maps.Single(m => m.Id == "romulus-outpost"));
        // Use the authored phase-changing actions, without unrelated combat/event polling.
        var center = map.Rules.Single(r => r.Id == "m2-center-enter").Actions;
        var defeated = map.Rules.Single(r => r.Id == "m2-centurion-down").Actions;
        var scan = map.Interactions.Single(i => i.Id == "m2-scan").Actions;
        map.Rules.Clear();
        var state = new RunState(); state.Variables["m2-stage"] = 1;
        foreach (var objective in map.Objectives) state.Objectives[objective.Id] = "Active";
        var host = new Host(); var engine = new Engine(map, state, host);
        Visible(engine, "m2-data");
        Check(map.Objectives.Single(o => o.Id == "m2-data").Name == "找到并且在枢纽上安装扫描设备", "initial scanner wording");
        engine.Enqueue(center); Drain(engine, state);
        Visible(engine, "m2-data", "m2-centurion");
        engine.Enqueue(defeated); Drain(engine, state);
        Check(state.Objectives["m2-centurion"] == "Complete", "centurion completion remains recorded");
        Visible(engine, "m2-data", "m2-centurion");
        host.HoldRendezvous = true; engine.Enqueue(scan); Drain(engine, state);
        Check(state.Objectives["m2-data"] == "Complete" && state.Variables["m2-stage"] == 3, "installation completes while its cutscene is still running");
        Check(!engine.VisibleObjectives().Any(o => o.Id == "m2-escape"), "extraction objective cannot appear before rendezvous/cutscene handoff");
        state = Json.Copy(state); engine = new Engine(map, state, host);
        host.HoldRendezvous = false; Drain(engine, state);
        Visible(engine, "m2-escape");
        Check(state.Objectives["m2-data"] == "Complete" && state.Objectives["m2-centurion"] == "Complete", "hidden finished objectives retain completion after save/load");
        Check(map.Rewards.Single(r => r.Id == "m2-solarite-15").Contents.Single().Count == 30, "tutorial reward is 30");
        Check(map.Rewards.Single(r => r.Id == "m2-solarite-30").Contents.Single().Count == 50, "riddle reward is 50");
        var legacy = new MapSpec { Objectives = new List<Objective> { new Objective { Id = "legacy" } } };
        Check(new Engine(Json.Copy(legacy), new RunState(), host).VisibleObjectives().Single().Id == "legacy", "objectives without visibility conditions keep their existing display behavior");
        Console.WriteLine("PASS mission two: phased objective display, scanner cutscene handoff, save/load and 30/50 Solarite rewards.");
    }
}
