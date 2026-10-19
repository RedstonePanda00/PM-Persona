using PMP_Persona.ShipSupport;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace PMP_Persona.VanguardSupport
{
    public abstract class OrbitalShipSupportWorker : ShipSupportWorker
    {
        public override bool TryGetActiveTarget(out GlobalTargetInfo target)
        {
            foreach (Map map in Find.Maps)
            {
                var active = map.GetComponent<OrbitalSupportSession>();
                if (active?.SupportDef != def) continue;
                target = new GlobalTargetInfo(map.Center, map); return true;
            }
            target = GlobalTargetInfo.Invalid; return false;
        }
        protected override string GetActivationDisabledReason(ShipSupportContext context)
        {
            string reason = base.GetActivationDisabledReason(context);
            if (reason != null) return reason;
            foreach (Map map in Find.Maps)
                if (map.GetComponent<OrbitalSupportSession>() != null) return "PMP.Support.Status.Active".Translate();
            return null;
        }
        protected override void BeginActivation(ShipSupportContext context)
        {
            if (TryCommit(context.Map, () =>
            {
                var session = OrbitalSupportSession.Attach(context.Map, def);
                try { VanguardFireControl.CurrentController.Begin(session); }
                catch { VanguardFireControl.CurrentController.End(); context.Map.components.Remove(session); throw; }
                return true;
            })) context.Window?.Close();
        }
    }
}
