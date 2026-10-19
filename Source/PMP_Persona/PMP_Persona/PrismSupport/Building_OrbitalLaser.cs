using PMP_Persona;
using RimWorld;
using RsPandaLibrary;
using Verse;

namespace PMP_Persona.PrismSupport
{
    // Orbital laser: moves from start to end, then destroys itself on arrival.
    public class Building_OrbitalLaser : Thing_MobileBeam
    {
        private IntVec3 endCell;

        public void SetDestinationAndEnd(IntVec3 end)
        {
            endCell = end;
            SetDestination(end);
        }

        protected override void Tick()
        {
            base.Tick();
            if (!Spawned || Destroyed)
                return;
            if (Position == endCell)
                Destroy(DestroyMode.Vanish);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref endCell, "endCell");
        }
    }
}
