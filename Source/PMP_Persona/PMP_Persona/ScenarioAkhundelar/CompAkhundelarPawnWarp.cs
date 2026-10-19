using RimWorld;
using ProtossMech;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace PMP_Persona.ScenarioAkhundelar
{
    public class CompAkhundelarPawnWarp : ThingComp
    {
        public Pawn pawnToSpawn;
        private int ticksRemaining = -1;
        private MaterialPropertyBlock warpBlock;

        private CompProperties_AkhundelarPawnWarp PropsTyped => (CompProperties_AkhundelarPawnWarp)props;

        public void BeginWarp(Pawn pawn)
        {
            pawnToSpawn = pawn;
            ticksRemaining = PropsTyped.warpTicks;
        }

        public override void PostDraw()
        {
            base.PostDraw();
            CompWarpConstructionBeacon.DrawWarpEffect(parent.DrawPos, parent.Rotation, 1f,
                PropsTyped.warpTicks, ticksRemaining, ref warpBlock);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (ticksRemaining <= 0 || pawnToSpawn == null || pawnToSpawn.Destroyed)
            {
                return;
            }

            ticksRemaining--;
            if (ticksRemaining == 0)
            {
                GenSpawn.Spawn(pawnToSpawn, parent.Position, parent.Map, WipeMode.Vanish);
                SoundDefOf.Psycast_Skip_Exit.PlayOneShot(new TargetInfo(pawnToSpawn.Position, pawnToSpawn.Map));
                FleckMaker.Static(pawnToSpawn.Position, pawnToSpawn.Map, FleckDefOf.PsycastAreaEffect, 2f);
                parent.Destroy(DestroyMode.Vanish);
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref pawnToSpawn, "pawnToSpawn");
            Scribe_Values.Look(ref ticksRemaining, "ticksRemaining", -1);
        }
    }
}
