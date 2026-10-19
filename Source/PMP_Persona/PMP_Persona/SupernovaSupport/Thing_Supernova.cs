using UnityEngine;
using Verse;

namespace PMP_Persona.SupernovaSupport
{
    public class Thing_Supernova : ThingWithComps
    {
        private CompStarVisual StarVisual => GetComp<CompStarVisual>();
        private CompSupernovaShell ShellVisual => GetComp<CompSupernovaShell>();

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            MapComponent_SupernovaField.Get(map)?.RegisterInstance(this);
        }

        public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
        {
            Map map = Map;
            base.DeSpawn(mode);
            if (map != null)
                MapComponent_SupernovaField.Get(map)?.UnregisterInstance(this);
        }

        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            if (Spawned && Map != null)
            {
                ShellVisual?.DrawShellVisual(DrawPos);
            }

            base.DrawAt(drawLoc, flip);

            if (Spawned && Map != null && ShellVisual == null)
                StarVisual?.DrawStarVisual(DrawPos);
        }
    }
}
