using Verse;

namespace PMP_Persona.Storyteller
{
    public class MasonKaldalisConfig : DefModExtension
    {
        public float draftRefusalChance = .02f;
        public float dangerRefusalChance = .04f;
        public float idlingChancePerDay = .01f;
        public int refusalTicks = 5000;
        public float recoveryChance = .1f;
        public int recoveryCooldownTicks = 60000;
        public int pulseIncidents = 2;
        public int pulseDurationMin = 60000;
        public int pulseDurationMax = 120000;
        public int rescueGraceTicks = 6000;
        public int inactivityDays = 10;
    }
}
