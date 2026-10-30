using Verse;

namespace PMP_Persona.Storyteller
{
    public class MasonKaldalisConfig : DefModExtension
    {
        public float draftRefusalChance = .02f;
        public float dangerRefusalChance = .04f;
        public int refusalTicks = 2500;
        public int recoveryCooldownTicks = 60000;
        public int pulseIncidents = 2;
        public int pulseDurationMin = 90000;
        public int pulseDurationMax = 120000;
        public int pulseGapTicks = 15000;
        public int restTicks = 120000;
        public int bigThreatGapTicks = 90000;
        public int retryTicks = 5000;
        public float battleDrivePerDay = 10f;
    }
}
