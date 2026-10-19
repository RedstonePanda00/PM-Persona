using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PMP_Persona.ScenarioAkhundelar
{
    public class MapComponent_AkhundelarProbeDelay : CustomMapComponent
    {
        private static readonly IntVec2[] ProbeOffsets =
        {
            new IntVec2(5, 0),
            new IntVec2(-5, 0),
            new IntVec2(0, 5),
            new IntVec2(0, -5),
            new IntVec2(5, -5)
        };

        private IntVec3 warpHubCell;
        private List<Pawn> probePawns = new List<Pawn>();
        private int ticksRemaining = -1;

        public MapComponent_AkhundelarProbeDelay(Map map)
            : base(map)
        {
        }

        public void Begin(IntVec3 warpHub, List<Pawn> probes, int delayTicks)
        {
            warpHubCell = warpHub;
            probePawns = probes ?? new List<Pawn>();
            ticksRemaining = delayTicks;
        }

        public override void MapComponentTick()
        {
            base.MapComponentTick();
            if (ticksRemaining < 0)
            {
                return;
            }

            ticksRemaining--;
            if (ticksRemaining > 0)
            {
                return;
            }

            SpawnProbeBeacons();
            ticksRemaining = -1;
        }

        private void SpawnProbeBeacons()
        {
            var used = new HashSet<IntVec3>();
            int pi = 0;
            foreach (Pawn probe in probePawns)
            {
                if (probe == null || probe.Destroyed)
                {
                    continue;
                }

                IntVec2 off = ProbeOffsets[Mathf.Min(pi, ProbeOffsets.Length - 1)];
                IntVec3 cell = AkhundelarArrivalUtility.OffsetStandableExclusive(map, warpHubCell, off.x, off.z, used);
                Thing beacon = ThingMaker.MakeThing(ThingDef.Named("PMPH_AkhundelarPawnWarpBeacon"));
                GenSpawn.Spawn(beacon, cell, map, WipeMode.Vanish);
                beacon.TryGetComp<CompAkhundelarPawnWarp>()?.BeginWarp(probe);
                SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(cell, map));
                pi++;
            }

            probePawns.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref warpHubCell, "hubCell");
            Scribe_Collections.Look(ref probePawns, "probePawns", LookMode.Reference);
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", -1);
            if (probePawns == null)
            {
                probePawns = new List<Pawn>();
            }
        }
    }
}
