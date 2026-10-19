using System.Collections.Generic;
using Verse;

namespace PMP_Persona.CarrierAircraftLegion
{
    public class CarrierLegionVariantConfig
    {
        public string variantId;
        public string droneTexPath;
        // Optional: ThingDef for instanced body draw (e.g. PMP_LegionShadow_AA). Falls back to droneTexPath + Mote shader.
        public string droneGlowDefName;
        public string bombardmentHandlerClass;
    }

    public class DefModExtension_CarrierLegionSwarm : DefModExtension
    {
        public List<CarrierLegionVariantConfig> variants = new List<CarrierLegionVariantConfig>();
    }
}
