using GunSmithFramework;

namespace PMP_Persona.Prometheus
{
    public sealed class Thing_HardlightRifle : Thing_ModularGun
    {
        public override void PostMake()
        {
            base.PostMake();
            // Initial issue equipment, not an empty-slot fallback. Loading an existing
            // weapon uses the framework's saved configuration and never refits the sight.
            GetComp<CompModularGun>().SetPartDefName("Optic", "PMP_HardlightRifle_Optic");
        }
    }
}
