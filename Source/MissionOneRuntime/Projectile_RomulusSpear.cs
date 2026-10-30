using RimWorld;
using Verse;

namespace PMP.Campaign {
    // All native termination paths converge here: hit, miss, interception and map-edge cleanup.
    public sealed class Projectile_RomulusSpear : Bullet {
        public override void Destroy(DestroyMode mode=DestroyMode.Vanish){
            if(Destroyed)return;
            var lastMap=Map;
            var lastCell=Position;
            base.Destroy(mode);
            if(lastMap!=null&&lastCell.InBounds(lastMap))
                lastMap.weatherManager.eventHandler.AddEvent(new WeatherEvent_LightningStrike(lastMap,lastCell));
        }
    }
}
