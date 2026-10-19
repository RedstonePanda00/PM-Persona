using UnityEngine;
using Verse;

namespace PMP_Persona.AirPatrolLegion
{
    [StaticConstructorOnStartup]
    public static class AirPatrolOrbAndTailFx
    {
        private static Material glowOrb, orbTrail;
        public static bool TryGetMaterials(out Material glowOrbMat, out Material orbTrailMat)
        {
            if (glowOrb == null) glowOrb = PersonaMaterials.Create("PM17N_Mat_GlowOrb");
            if (orbTrail == null) orbTrail = PersonaMaterials.Create("PM17N_Mat_OrbTrail");
            glowOrbMat = glowOrb;
            orbTrailMat = orbTrail;
            return true;
        }
    }
}
