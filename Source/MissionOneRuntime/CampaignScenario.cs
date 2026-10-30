using System;
using RimCampaign.Runtime;

namespace PMP.Campaign {
    // Optional DLC assemblies can add their setup after the campaign world exists.
    public sealed class ScenPart_ShadowOfWar : ScenPart_Campaign {
        public static event Action WorldGenerated;
        public override void PostWorldGenerate() {
            base.PostWorldGenerate();
            WorldGenerated?.Invoke();
        }
    }
}
