using RimWorld;
using Verse;

namespace PMP_Persona
{
    // Configurable summon event letter for support calls.
    public class DefModExtension_SupportSummonLetter : DefModExtension
    {
        public bool enableSummonEventLetter = true;
        public string summonEventLetterLabel;
        public string summonEventLetterDescription;
    }

    public static class SupportSummonLetterUtility
    {
        public static void TrySendSummonLetter(ThingDef supportDef, Thing spawnedThing)
        {
            if (supportDef == null || spawnedThing == null)
                return;

            DefModExtension_SupportSummonLetter ext = supportDef.GetModExtension<DefModExtension_SupportSummonLetter>();
            if (ext == null || !ext.enableSummonEventLetter)
                return;

            string label = ResolveText(ext.summonEventLetterLabel, supportDef.LabelCap);
            string description = ResolveText(ext.summonEventLetterDescription, supportDef.description);
            if (string.IsNullOrWhiteSpace(description))
                description = supportDef.LabelCap + " has been summoned.";

            Find.LetterStack.ReceiveLetter(
                label,
                description,
                LetterDefOf.PositiveEvent,
                new LookTargets(spawnedThing)
            );
        }

        private static string ResolveText(string maybeKeyOrText, string fallback)
        {
            if (!string.IsNullOrEmpty(maybeKeyOrText))
            {
                if (maybeKeyOrText.CanTranslate())
                    return maybeKeyOrText.Translate().ToString();
                return maybeKeyOrText;
            }
            return string.IsNullOrEmpty(fallback) ? string.Empty : fallback;
        }
    }
}
