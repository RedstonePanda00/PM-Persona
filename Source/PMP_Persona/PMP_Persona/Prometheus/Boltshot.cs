using GunSmithFramework;
using ProtossMech;
using Verse;

namespace PMP_Persona.Prometheus
{
    public sealed class BoltshotMode : DefModExtension
    {
        public int pelletCount = 1;
    }

    public sealed class Verb_Boltshot : Verb_ShootShotgun
    {
        protected override bool TryCastShot()
        {
            var original = verbProps;
            // Per-shot copy: attachments on this gun never mutate the shared ThingDef.
            var shot = (VerbProperties_Shotgun)original.MemberwiseClone();
            shot.pelletCount = CompModularGun.From(EquipmentSource)?.GetPart("Ammo")
                ?.GetModExtension<BoltshotMode>()?.pelletCount ?? 1;
            verbProps = shot;
            try { return base.TryCastShot(); }
            finally { verbProps = original; }
        }
    }
}
