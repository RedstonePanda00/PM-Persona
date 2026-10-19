using UnityEngine;
using Verse;

namespace PMP_Persona
{
    public class VerbProperties_WarbringerSweepLaser : VerbProperties
    {
        public float aimRotateSpeedDeg = 60f;
        public float damagePerTick = 8f;
        public int damageTickInterval = 5;
        public float armorPenetration = 0.5f;
        public float beamLineFleckChance = 0.35f;
        public float beamGroundFleckChance = 0.2f;
        public Color beamMoteColor = new Color(0f, 1f, 0.8f, 1f);

        public VerbProperties_WarbringerSweepLaser()
        {
            verbClass = typeof(Verb_WarbringerSweepLaser);
        }
    }
}
