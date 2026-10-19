using Verse;
using RimWorld;
using System.Collections.Generic;
using System.Text;

namespace PMP_Persona
{
    public class Hediff_AvengingProtocol : HediffWithComps
    {
        private HediffStage cachedStage;
        private float lastSeverity = -1f;

        public override HediffStage CurStage
        {
            get
            {
                if (lastSeverity != Severity)
                {
                    lastSeverity = Severity;

                    cachedStage = new HediffStage();
                    var statOffsets = new List<StatModifier>();
                    var statFactors = new List<StatModifier>();

                    // 1. Move Speed (as a Factor)
                    // Each level adds 30% to the multiplier. (e.g., Lvl 2 = 1 + 0.3*2 = 1.6x multiplier)
                    statFactors.Add(new StatModifier
                    {
                        stat = StatDefOf.MoveSpeed,
                        value = 1f + (Severity * 0.3f)
                    });

                    // 2. Ranged & Melee Speed (as an Offset)
                    // A 60% speed increase per level. The final cooldown multiplier is 1 / (1 + 0.6 * n).
                    // The offset is this value minus 1. This prevents stats from becoming absurdly high.
                    float totalSpeedBonus = 1f + (0.6f * Severity);
                    float cooldownMultiplier = 1f / totalSpeedBonus;
                    float cooldownOffset = cooldownMultiplier - 1f;

                    statOffsets.Add(new StatModifier
                    {
                        stat = StatDefOf.RangedCooldownFactor,
                        value = cooldownOffset
                    });

                    statOffsets.Add(new StatModifier
                    {
                        stat = StatDefOf.MeleeCooldownFactor,
                        value = cooldownOffset
                    });

                    /*
                    statOffsets.Add(new StatModifier
                    {
                        stat = StatDefOf.MeleeHitChance,
                        value = Severity * 0.15f
                    });
                    */


                    cachedStage.statOffsets = statOffsets;
                    cachedStage.statFactors = statFactors;
                }
                return cachedStage;
            }
        }

        public override string TipStringExtra
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(base.TipStringExtra);
                sb.AppendLine("Move Speed Multiplier: x".ToString() + (1f + Severity * 0.3f).ToString("F2"));
                
                float cooldownPercent = (1f - (1f / (1f + 0.6f * Severity))) * 100f;
                sb.AppendLine("Attack Cooldown Reduction: ".ToString() + cooldownPercent.ToString("F0") + "%");
                
                return sb.ToString();
            }
        }
    }
}