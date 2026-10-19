using Verse;

namespace PMP_Persona.RecomposerSupport
{
    public class GameComponent_RecomposerState : GameComponent
    {
        private int lastDeployTick = -999999;

        public GameComponent_RecomposerState(Game game)
        {
        }

        public int CooldownTicksRemaining
        {
            get { return 0; }
        }

        public void RecordDeploy()
        {
            // Kept as a no-op for binary and save compatibility with pre-resource versions.
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref lastDeployTick, "pmpRecomposerLastDeployTick", -999999);
        }
    }
}
