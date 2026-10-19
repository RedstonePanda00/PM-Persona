using UnityEngine;
using Verse;

namespace PMP_Persona.ScenarioAkhundelar
{
    // The beacon is rendered by its warp component; it has no static sprite.
    public class Thing_AkhundelarWarpBeacon : ThingWithComps
    {
        protected override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            foreach (ThingComp comp in AllComps) comp.PostDraw();
        }
    }
}
