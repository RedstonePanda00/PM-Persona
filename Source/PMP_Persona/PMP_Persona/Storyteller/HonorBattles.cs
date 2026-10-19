using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace PMP_Persona.Storyteller
{
    public class HonorBattle : IExposable
    {
        public Map map;
        public List<Pawn> enemies = new List<Pawn>();
        public List<Pawn> visitors = new List<Pawn>();
        public List<Pawn> wounded = new List<Pawn>();
        public HashSet<int> participants = new HashSet<int>();
        public float points, resistance;
        public int started, lastDamage, ended = -1;
        public bool raid, settled, arrived;
        public void ExposeData()
        {
            Scribe_References.Look(ref map, "map");
            Scribe_Collections.Look(ref enemies, "enemies", LookMode.Reference);
            Scribe_Collections.Look(ref visitors, "visitors", LookMode.Reference);
            Scribe_Collections.Look(ref wounded, "wounded", LookMode.Reference);
            Scribe_Collections.Look(ref participants, "participants", LookMode.Value);
            Scribe_Values.Look(ref points, "points"); Scribe_Values.Look(ref resistance, "resistance");
            Scribe_Values.Look(ref started, "started"); Scribe_Values.Look(ref lastDamage, "lastDamage");
            Scribe_Values.Look(ref ended, "ended", -1); Scribe_Values.Look(ref raid, "raid");
            Scribe_Values.Look(ref settled, "settled"); Scribe_Values.Look(ref arrived, "arrived");
        }
    }
    public partial class MasonKaldalisState
    {
        private List<HonorBattle> battles = new List<HonorBattle>();
        internal bool HasPlayerCombat(Map map) => battles.Any(b => b.map == map && b.resistance > 0);
        internal void RegisterRaid(IncidentParms parms, List<Pawn> pawns)
        {
            if (!Enabled || parms.target is not Map map || pawns.NullOrEmpty()) return;
            battles.Add(new HonorBattle { map = map, enemies = new List<Pawn>(pawns), points = parms.points, raid = true, started = GenTicks.TicksGame });
        }
        internal void RecordDamage(Pawn victim, DamageInfo info, float damage)
        {
            if (!Enabled || damage <= 0 || victim.Map == null || info.Instigator == null) return;
            Thing attacker = info.Instigator;
            bool ourAttack = attacker.Faction == Faction.OfPlayer && victim.HostileTo(Faction.OfPlayer) && !victim.IsPrisonerOfColony;
            bool enemyAttack = attacker.HostileTo(Faction.OfPlayer) && !victim.HostileTo(Faction.OfPlayer);
            if (!ourAttack && !enemyAttack) return;
            Pawn enemy = ourAttack ? victim : attacker as Pawn;
            if (enemy == null) return;
            HonorBattle battle = battles.FirstOrDefault(b => !b.settled && b.ended < 0 && b.map == victim.Map && b.enemies.Contains(enemy));
            if (battle == null)
            {
                battle = battles.FirstOrDefault(b => !b.raid && !b.settled && b.ended < 0 && b.map == victim.Map);
                if (battle == null)
                {
                    battle = new HonorBattle { map = victim.Map, started = GenTicks.TicksGame };
                    battles.Add(battle);
                }
                battle.enemies.Add(enemy);
            }
            battle.arrived = true; battle.lastDamage = GenTicks.TicksGame;
            if (ourAttack)
            {
                battle.resistance += damage;
                if (attacker is Pawn pawn && Applies(pawn)) battle.participants.Add(pawn.thingIDNumber);
                lastBattleTick = GenTicks.TicksGame;
            }
            else if (Applies(victim)) battle.participants.Add(victim.thingIDNumber);
            else if (victim.RaceProps.Humanlike && !victim.IsPrisoner && !victim.IsSlave && !battle.visitors.Contains(victim)) battle.visitors.Add(victim);
        }
        private void TickBattles(int now)
        {
            foreach (HonorBattle battle in battles)
            {
                if (battle.settled) continue;
                if (battle.map == null) { battle.settled = true; continue; }
                if (battle.ended < 0)
                {
                    if (battle.enemies.Any(p => p != null && p.Spawned)) battle.arrived = true;
                    if (!battle.arrived && now - battle.started < 60000) continue; // drop pods and delayed arrivals
                    if (battle.enemies.Any(p => p != null && !p.Dead && !p.Downed && p.Spawned && p.Map == battle.map && p.HostileTo(Faction.OfPlayer))) continue;
                    if (now - battle.lastDamage < 2500) continue;
                    battle.ended = now;
                    if (battle.resistance > 0)
                    {
                        NotifyBattleFinished();
                        if (battle.raid && battle.resistance >= Math.Max(20, battle.points * .05f) && battle.map.mapPawns.FreeColonistsSpawned.Any(p => !p.Downed)) Award("Raid", HonorRules.RaidReward(battle.points));
                        if (battle.visitors.Any(p => p != null && !p.Dead)) Award("ProtectVisitors", 5);
                    }
                    battle.wounded = battle.map.mapPawns.AllPawnsSpawned.Where(p => p.RaceProps.Humanlike && p.Downed && !p.Dead &&
                        (p.Faction == Faction.OfPlayer || p.IsPrisonerOfColony || !p.HostileTo(Faction.OfPlayer))).ToList();
                }
                if (now - battle.ended < Config.rescueGraceTicks) continue;
                int allowance = 1 + battle.participants.Count / 6;
                foreach (Pawn pawn in battle.wounded.OrderBy(p => p?.thingIDNumber ?? 0))
                {
                    // Dead colonists are not penalized; moving/carrying/treating a survivor is rescue.
                    if (pawn == null || pawn.Dead || !pawn.Downed || !pawn.Spawned || pawn.InBed() || RecentlyTended(pawn, battle.started)) continue;
                    if (allowance-- > 0) continue;
                    if (Once("abandoned:" + pawn.thingIDNumber + ":" + battle.started))
                        Award("Abandonment", pawn.Faction == Faction.OfPlayer || pawn.IsPrisonerOfColony || pawn.IsSlaveOfColony ? -6 : -3);
                }
                battle.settled = true;
            }
            battles.RemoveAll(b => b.settled);
        }
    }
    [HarmonyPatch(typeof(IncidentWorker_Raid), "PostProcessSpawnedPawns")]
    internal static class HonorRaidStarted
    {
        static void Postfix(IncidentWorker_Raid __instance, IncidentParms parms, List<Pawn> pawns)
        {
            if (__instance is IncidentWorker_RaidEnemy) MasonKaldalisState.Current?.RegisterRaid(parms, pawns);
        }
    }
    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    internal static class HonorBattleDamage
    {
        static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt) => MasonKaldalisState.Current?.RecordDamage(__instance, dinfo, totalDamageDealt);
    }
}
