using System;

namespace PMP_Persona.Storyteller
{
    // Pure arithmetic shared by the game and regression tests.
    public static class HonorRules
    {
        public static int Tier(float honor) => honor < 0 ? -1 : honor < 200 ? 0 : honor < 600 ? 1 : 2;
        public static float Reward(float amount, int previousInDay, float awardedToday)
        {
            float factor = previousInDay == 0 ? 1f : previousInDay == 1 ? .5f : previousInDay == 2 ? .25f : 0f;
            return Math.Max(0, Math.Min(amount * factor, 15f - awardedToday));
        }
        public static float Decay(float honor) => honor < 0 ? Math.Min(0, honor + .5f) : Math.Max(0, honor - 1f);
        public static float RaidReward(float points) => Math.Max(8f, Math.Min(20f, 8f + points / 250f));
        public static float Apply(float honor, float delta) => Math.Max(-200f, honor + delta);
    }
}
