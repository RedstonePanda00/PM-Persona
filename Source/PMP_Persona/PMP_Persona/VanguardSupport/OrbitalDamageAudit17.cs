using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using PMP_Persona.PrismSupport;

namespace PMP_Persona.VanguardSupport
{
    // Invoked only by the opt-in isolated Prism audit, never in a normal colony.
    internal static class OrbitalDamageAudit17
    {
        private static readonly Dictionary<Thing, List<DamageInfo>> hits = new Dictionary<Thing, List<DamageInfo>>();
        private static void Record(Thing __instance, DamageInfo dinfo)
        {
            if (hits.TryGetValue(__instance, out var list)) list.Add(dinfo);
        }

        internal static void Run(Map map, string folder)
        {
            const string id = "PMP.Persona.OrbitalDamageAudit17";
            var harmony = new Harmony(id);
            var takeDamage = AccessTools.Method(typeof(Thing), nameof(Thing.TakeDamage));
            int baseline = Find.TickManager.TicksGame;
            var report = new List<string>();
            var center = new IntVec3(25, 0, 25);
            try
            {
                Check(OrbitalMixedDamage.Impact.armorCategory == null, "impact inherited an armor category");
                Check(OrbitalMixedDamage.Impact.defaultArmorPenetration == 2, "impact default AP");
                Check(Math.Abs(OrbitalMixedDamage.Blast.defaultArmorPenetration - 1.3f) < .001f
                    && OrbitalMixedDamage.Vaporize.defaultArmorPenetration == 1, "global vanilla AP changed");
                harmony.Patch(takeDamage, prefix: new HarmonyMethod(typeof(OrbitalDamageAudit17), nameof(Record)));
                // Clear obstructions only in this disposable quick-test map.
                foreach (var cell in CellRect.CenteredOn(center, 16).ClipInsideMap(map))
                {
                    cell.GetEdifice(map)?.Destroy(DestroyMode.Vanish);
                    map.roofGrid.SetRoof(cell, null);
                }
                string[] defs = { "PMP_VanguardNeutronIncoming", "PMP_AntiparticleShellIncoming",
                    "PMP_PrismLightParticleIncoming", "PMP_PrismHeavyParticleIncoming" };
                int[] amounts = { 55, 220, 88, 33 };
                for (int i = 0; i < defs.Length; i++)
                {
                    int offset = i == 1 || i == 2 ? 4 : 1;
                    var wall = Probe(map, ThingDefOf.Wall, center + new IntVec3(offset, 0, 0));
                    var round = (PMPH_OrbitalIncoming)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(defs[i]));
                    GenSpawn.Spawn(round, center, map);
                    Traverse.Create(round).Method("Impact").GetValue();
                    var blasts = map.listerThings.AllThings.OfType<Explosion>().ToArray();
                    Check(blasts.Length == 1, defs[i] + " must create exactly one explosion");
                    Check(blasts[0].armorPenetration == 2, "native explosion AP is not 200%");
                    for (int t = 0; t < 120 && blasts.Any(e => !e.Destroyed); t++)
                    {
                        Find.TickManager.DebugSetTicksGame(baseline + t);
                        foreach (var blast in blasts.Where(e => !e.Destroyed))
                            Traverse.Create(blast).Method("Tick").GetValue();
                    }
                    Check(blasts.All(e => e.Destroyed), "explosion did not complete");
                    Verify(wall, amounts[i]);
                    report.Add(defs[i] + ": PASS 3 x " + amounts[i] + ", AP 2, distance " + offset
                        + "; building HP loss " + (1000000 - wall.HitPoints));
                    wall.Destroy(DestroyMode.Vanish);
                    Find.TickManager.DebugSetTicksGame(baseline);
                }

                // A two-cell building must receive each channel only once per explosion.
                var bed = Probe(map, ThingDefOf.Bed, center);
                var explosion = (Explosion)ThingMaker.MakeThing(ThingDefOf.Explosion);
                explosion.damType = OrbitalMixedDamage.Impact;
                explosion.damAmount = 100; explosion.radius = 5; explosion.armorPenetration = 2;
                GenSpawn.Spawn(explosion, center, map);
                var damaged = new List<Thing>();
                var method = AccessTools.Method(typeof(DamageWorker_OrbitalImpact), "ExplosionDamageThing");
                foreach (var cell in bed.OccupiedRect())
                    method.Invoke(OrbitalMixedDamage.Impact.Worker, new object[] { explosion, bed, damaged, null, cell });
                Verify(bed, 100);
                Check(damaged.Count == 1, "multi-cell target deduplication");
                explosion.Destroy(DestroyMode.Vanish); bed.Destroy(DestroyMode.Vanish);
                report.Add("Two-cell building: PASS exactly three channels, no duplicate cell damage");

                var core = Probe(map, ThingDefOf.Wall, center);
                var edge = Probe(map, ThingDefOf.Wall, center + new IntVec3(12, 0, 0));
                var fracture = PrismFractureEffects.Get(map);
                fracture.Add(center, new OrbitalSupportConfig { fractureGuidanceTicks = 0, fractureDamage = 400, fractureRadius = 12 });
                fracture.MapComponentTick();
                Verify(core, 400); Verify(edge, 140);
                report.Add("Fracture: PASS 3 x 400 center / 3 x 140 at radius 12, all AP 2");
                Find.TickManager.DebugSetTicksGame(baseline + 71);
                fracture.MapComponentTick(); PrismFractureEffects.PruneIdle();
                report.Add("Impact armor category: null; BombSuper/Vaporize global defaults unchanged");
                File.WriteAllLines(Path.Combine(folder, "damage-results.txt"), report);
            }
            finally
            {
                harmony.Unpatch(takeDamage, HarmonyPatchType.Prefix, id);
                foreach (var target in hits.Keys) if (!target.Destroyed) target.Destroy(DestroyMode.Vanish);
                hits.Clear(); Find.TickManager.DebugSetTicksGame(baseline);
            }
        }

        private static Thing Probe(Map map, ThingDef def, IntVec3 cell)
        {
            var thing = ThingMaker.MakeThing(def, ThingDefOf.Steel);
            GenSpawn.Spawn(thing, cell, map); thing.HitPoints = 1000000;
            hits.Add(thing, new List<DamageInfo>()); return thing;
        }
        private static void Verify(Thing thing, float amount)
        {
            var list = hits[thing];
            Check(list.Count == 3, thing.def.defName + ": expected 3 hits, got " + list.Count);
            string[] expected = { "PMP_OrbitalImpact", "BombSuper", "Vaporize" };
            for (int i = 0; i < 3; i++)
                Check(list[i].Def.defName == expected[i] && Math.Abs(list[i].Amount - amount) < .01f
                    && list[i].ArmorPenetrationInt == 2, "incorrect channel/amount/AP: " + list[i]);
            Check(thing.HitPoints < 1000000, "damage recorded but not applied");
        }
        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception("Orbital mixed damage audit: " + message);
        }
    }
}
