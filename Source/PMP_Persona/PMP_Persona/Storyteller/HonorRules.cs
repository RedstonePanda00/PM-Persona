using System;

namespace PMP_Persona.Storyteller
{
    // Honor and pulse calculations.
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

        // Exact precept IDs for conditional rules.
        public static float IdeologyFactor(string name, bool trade, bool guilty)
        {
            switch (name)
            {
                case "OrganUse_HorribleSellOK": return trade ? .5f : 2f;
                case "Execution_HorribleIfInnocent": return guilty ? 1f : 2f;
                case "Execution_RespectedIfGuilty": return guilty ? .5f : 1f;
                case "Cannibalism_Acceptable": case "Cannibalism_Preferred":
                case "Cannibalism_RequiredStrong": case "Cannibalism_RequiredRavenous":
                case "OrganUse_Acceptable": case "Slavery_Acceptable": case "Slavery_Honorable":
                case "Execution_DontCare": case "Execution_Required": return .5f;
                case "Cannibalism_Abhorrent": case "Cannibalism_Horrible": case "Cannibalism_Disapproved":
                case "OrganUse_Abhorrent": case "OrganUse_HorribleNoSell":
                case "Slavery_Abhorrent": case "Slavery_Horrible": case "Slavery_Disapproved":
                case "Execution_Abhorrent": case "Execution_Horrible": return 2f;
                default: return 1f;
            }
        }

        public static bool PulseExpired(int remaining, int end, int now) => remaining > 0 && now >= end;
        public static int FreezeDeadline(int deadline, int elapsed) => deadline > 0 ? deadline + elapsed : deadline;
    }
}
