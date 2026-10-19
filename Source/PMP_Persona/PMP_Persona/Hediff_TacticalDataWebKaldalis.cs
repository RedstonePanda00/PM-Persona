using Verse;

namespace PMP_Persona
{
    public class Hediff_TacticalDataWebKaldalis : HediffWithComps
    {
        private const string DefNameLegionnaire = "PM_Legionnaire";

        public override string LabelInBrackets
        {
            get
            {
                if (pawn?.Map == null) return null;
                int n = SpawnedPawnCountCache.GetCountInFaction(pawn.Map, DefNameLegionnaire, pawn.Faction);
                return "PMP_TacticalDataWebKaldalis_BackupCount".Translate(n);
            }
        }
    }
}
