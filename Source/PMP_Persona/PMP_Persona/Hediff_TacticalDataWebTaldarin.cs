using Verse;

namespace PMP_Persona
{
    public class Hediff_TacticalDataWebTaldarin : HediffWithComps
    {
        // Backup bodies for Taldarin = Purifier Immortals (PM_PurifierImmortal).
        private const string DefNamePurifierImmortal = "PM_PurifierImmortal";

        public override string LabelInBrackets
        {
            get
            {
                if (pawn?.Map == null || pawn?.Faction == null) return null;
                int n = SpawnedPawnCountCache.GetCountInFaction(pawn.Map, DefNamePurifierImmortal, pawn.Faction);
                return "PMP_TacticalDataWebTaldarin_BackupCount".Translate(n);
            }
        }
    }
}
