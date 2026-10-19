using System.Collections.Generic;
using RimWorld;
using ProtossMech;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PMP_Persona.ScenarioAkhundelar
{
    public class CompAkhundelarAnchorBeacon : ThingComp
    {
        public IntVec3 pylonBottomLeft;
        public Pawn colonist;
        public Pawn kaldalis;
        public List<Pawn> probes = new List<Pawn>();

        private int ticksRemaining = -1;
        private MaterialPropertyBlock warpBlock;

        private CompProperties_AkhundelarAnchorBeacon PropsTyped => (CompProperties_AkhundelarAnchorBeacon)props;

        public void ConfigureScenarioData(IntVec3 pylonBl, Pawn zero, Pawn kaldalisPawn, List<Pawn> probePawns)
        {
            pylonBottomLeft = pylonBl;
            colonist = zero;
            kaldalis = kaldalisPawn;
            probes = probePawns ?? new List<Pawn>();
            ticksRemaining = PropsTyped.ticksToPylon;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            ThingDef pylonDef = DefDatabase<ThingDef>.GetNamedSilentFail(PropsTyped.buildingDefName);
            IntVec2 size = pylonDef?.size ?? parent.def.size;
            IntVec3 cell = pylonBottomLeft.IsValid ? pylonBottomLeft : parent.Position;
            Vector3 center = GenThing.TrueCenter(cell, Rot4.North, size, AltitudeLayer.MoteOverhead.AltitudeFor());
            CompWarpConstructionBeacon.DrawWarpEffect(center, Rot4.North, Mathf.Max(size.x, size.z),
                PropsTyped.ticksToPylon, ticksRemaining, ref warpBlock);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (ticksRemaining <= 0)
            {
                return;
            }

            ticksRemaining--;
            if (ticksRemaining != 0)
            {
                return;
            }

            CompleteAnchor();
        }

        private void CompleteAnchor()
        {
            Map map = parent.Map;
            ThingDef pylonDef = DefDatabase<ThingDef>.GetNamedSilentFail(PropsTyped.buildingDefName);
            if (pylonDef == null)
            {
                Log.Error("[PMP_Persona] Akhundelar anchor: missing building def " + PropsTyped.buildingDefName);
                parent.Destroy(DestroyMode.Vanish);
                return;
            }

            IntVec3 bl = pylonBottomLeft.IsValid ? pylonBottomLeft : parent.Position;
            if (!GenConstruct.CanPlaceBlueprintAt(pylonDef, bl, Rot4.North, map).Accepted
                && !AkhundelarArrivalUtility.TryFindPylonBottomLeftNear(map, parent.Position, pylonDef, out bl))
            {
                Log.Warning("[PMP_Persona] Akhundelar anchor: could not place Pylon; using anchor cell.");
                bl = parent.Position;
            }

            parent.Destroy(DestroyMode.Vanish);
            Building pylon = (Building)GenSpawn.Spawn(ThingMaker.MakeThing(pylonDef), bl, map, WipeMode.FullRefund);
            pylon.SetFactionDirect(Faction.OfPlayer);
            SoundDefOf.Psycast_Skip_Exit.PlayOneShot(new TargetInfo(bl, map));
            FleckMaker.Static(bl.ToVector3Shifted(), map, FleckDefOf.PsycastAreaEffect, 2f);

            IntVec3 warpHub = AkhundelarArrivalUtility.PylonWarpHub(bl, map);
            var reserved = new HashSet<IntVec3>();
            SpawnPawnBeacon(map, AkhundelarArrivalUtility.OffsetStandableExclusive(map, warpHub, -4, 0, reserved), colonist);
            SpawnPawnBeacon(map, AkhundelarArrivalUtility.OffsetStandableExclusive(map, warpHub, 4, 0, reserved), kaldalis);

            MapComponent_AkhundelarProbeDelay delay = new MapComponent_AkhundelarProbeDelay(map);
            delay.Begin(warpHub, new List<Pawn>(probes), 60);
            map.components.Add(delay);
        }

        private static void SpawnPawnBeacon(Map map, IntVec3 cell, Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            Thing beacon = ThingMaker.MakeThing(ThingDef.Named("PMPH_AkhundelarPawnWarpBeacon"));
            GenSpawn.Spawn(beacon, cell, map, WipeMode.Vanish);
            beacon.TryGetComp<CompAkhundelarPawnWarp>()?.BeginWarp(pawn);
            SoundDefOf.Psycast_Skip_Entry.PlayOneShot(new TargetInfo(cell, map));
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref pylonBottomLeft, "pylonBottomLeft");
            Scribe_References.Look(ref colonist, "colonist");
            Scribe_References.Look(ref kaldalis, "kaldalis");
            Scribe_Collections.Look(ref probes, "probes", LookMode.Reference);
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", -1);
            if (probes == null)
            {
                probes = new List<Pawn>();
            }
        }
    }
}
