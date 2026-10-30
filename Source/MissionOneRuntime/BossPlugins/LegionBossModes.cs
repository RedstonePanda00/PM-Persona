using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Runtime;
using Verse;

namespace PMP.Campaign.BossPlugins
{
    public enum LegionBossMode { Unspecified, Fixed, Raid }

    // Mode belongs to a Boss record, not to the pawn's health. Only pre-arrival orders need a ledger.
    public static class LegionBossModes
    {
        const string StateKey = "persona.legion.behavior";
        static LegionBossOrders Orders => Current.Game?.GetComponent<LegionBossOrders>();
        public static bool Supported(Pawn pawn) => pawn?.kindDef?.defName is
            "PMP_LegionCenturion" or "PMP_ChampionWarrior" or "PMP_SanctuaryChampion";
        public static LegionBossMode Get(Pawn pawn)
        {
            if (!Supported(pawn)) return LegionBossMode.Unspecified;
            var record = BossMapComponent.For(pawn);
            if (record != null && record.ModuleState.TryGetValue(StateKey, out var state) &&
                state.TryGetValue("mode", out var value) && Enum.TryParse(value, out LegionBossMode mode)) return mode;
            var pending = Orders?.Get(pawn) ?? LegionBossMode.Unspecified;
            return pending;
        }
        public static void Set(Pawn pawn, LegionBossMode mode)
        {
            if (!Supported(pawn) || pawn.Destroyed) return;
            var record = BossMapComponent.For(pawn);
            if (record == null)
            {
                var orders = Orders;
                if (orders == null) return;
                orders.Set(pawn, mode);
            }
            else
            {
                if (!record.ModuleState.TryGetValue(StateKey, out var state))
                    record.ModuleState[StateKey] = state = new Dictionary<string, string>();
                state["mode"] = mode.ToString();
                Orders?.Remove(pawn);
            }
        }
        public static void Attach(Pawn pawn)
        {
            if (!Supported(pawn) || BossMapComponent.For(pawn) == null) return;
            var mode = Orders?.Get(pawn) ?? LegionBossMode.Unspecified;
            if (mode != LegionBossMode.Unspecified) Set(pawn, mode);
        }
    }

    public sealed class LegionBossOrders : GameComponent
    {
        Dictionary<Pawn, LegionBossMode> pending = new Dictionary<Pawn, LegionBossMode>();
        List<Pawn> keys;
        List<LegionBossMode> values;
        public LegionBossOrders(Game game) { }
        public LegionBossMode Get(Pawn pawn) => pawn != null && pending.TryGetValue(pawn, out var mode) ? mode : LegionBossMode.Unspecified;
        public void Set(Pawn pawn, LegionBossMode mode) => pending[pawn] = mode;
        public void Remove(Pawn pawn) => pending.Remove(pawn);
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
                foreach (var pawn in pending.Keys.Where(p => p == null || p.Destroyed || p.Dead).ToList()) pending.Remove(pawn);
            Scribe_Collections.Look(ref pending, "pmpPendingBossModes", LookMode.Reference, LookMode.Value, ref keys, ref values);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) pending ??= new Dictionary<Pawn, LegionBossMode>();
        }
    }
}
