using System;

namespace PMP_Persona.Storyteller
{
    // Shared event deadline, including pulse advances; overdue opportunities do not accumulate.
    public sealed class IncidentBudget
    {
        public int nextTick, retryTick;
        public bool Due(int now, bool pulse, int pulseNext) => now >= retryTick && (pulse ? now >= pulseNext : now >= nextTick);
        public void Attempt(int now, int delay) => retryTick = now + Math.Max(1000, delay);
        public void Spend(int now, int interval) => nextTick = Math.Max(now, nextTick) + Math.Max(1000, interval);
        public void Freeze(int elapsed)
        {
            nextTick = HonorRules.FreezeDeadline(nextTick, elapsed);
            retryTick = HonorRules.FreezeDeadline(retryTick, elapsed);
        }
        public static int Interval(float days, double random) => Math.Max(1000,
            (int)Math.Round(60000 * days * (.8 + .4 * random) / 1000, MidpointRounding.AwayFromZero) * 1000);
    }
}
