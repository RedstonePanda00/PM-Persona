using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Storyteller
{
    public class HonorLedgerEntry : IExposable
    {
        public string kind;
        public int tick;
        public float delta, balance;
        public void ExposeData()
        {
            Scribe_Values.Look(ref kind, "kind"); Scribe_Values.Look(ref tick, "tick");
            Scribe_Values.Look(ref delta, "delta"); Scribe_Values.Look(ref balance, "balance");
        }
    }
    public partial class MasonKaldalisState
    {
        private List<HonorLedgerEntry> ledger = new List<HonorLedgerEntry>();
        internal IReadOnlyList<HonorLedgerEntry> Ledger => ledger;
        internal string Phase => !Enabled ? "Paused" : PulseActive ? "Pulse" : GenTicks.TicksGame < restUntil ? "Rest" : BattleDrive >= 60 ? "Probing" : "Building";
    }
    public class Dialog_HonorLedger : Window
    {
        private readonly MasonKaldalisState state;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(700, 620);
        public Dialog_HonorLedger(MasonKaldalisState state)
        {
            this.state = state; doCloseX = true; doCloseButton = true; absorbInputAroundWindow = true;
        }
        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0, 0, inRect.width - 30, 30), "PMP.Honor.Status".Translate());
            Widgets.Label(new Rect(0, 35, inRect.width, 65), "PMP.Honor.Summary".Translate(state.Honor.ToString("0.##"),
                ("PMP.Honor.Tier" + state.Tier).Translate(), state.BattleDrive.ToString("0"), ("PMP.Honor.Phase." + state.Phase).Translate(), state.RewardRemaining.ToString("0.##"),
                (System.Math.Max(0, state.Budget.nextTick - GenTicks.TicksGame) / 60000f).ToString("0.00")));
            Widgets.Label(new Rect(0, 105, inRect.width, 70), "PMP.Honor.Rules".Translate());
            Rect outer = new Rect(0, 180, inRect.width, inRect.height - 235);
            Rect view = new Rect(0, 0, outer.width - 20, state.Ledger.Count * 32 + 32);
            Widgets.BeginScrollView(outer, ref scroll, view);
            if (state.Ledger.Count == 0) Widgets.Label(new Rect(0, 0, view.width, 30), "PMP.Honor.Empty".Translate());
            for (int i = 0; i < state.Ledger.Count; i++)
            {
                var entry = state.Ledger[state.Ledger.Count - 1 - i];
                Widgets.Label(new Rect(0, i * 32, view.width, 30), "PMP.Honor.Entry".Translate(
                    ((GenTicks.TicksGame - entry.tick) / 60000f).ToString("0.0"),
                    ("PMP.Honor.Reason." + entry.kind).Translate(), entry.delta.ToString("+0.##;-0.##;0"), entry.balance.ToString("0.##")));
            }
            Widgets.EndScrollView();
        }
    }
}
