using Verse;

namespace PMP_Persona
{
    public class Hediff_TacticalDataWebWarbringer : HediffWithComps
    {
        private const string DefNameColossusPrototype = "PM_ColossusPrototype";

        public override string LabelInBrackets
        {
            get
            {
                if (pawn?.Map == null || pawn?.Faction == null) return null;
                int n = SpawnedPawnCountCache.GetCountInFaction(pawn.Map, DefNameColossusPrototype, pawn.Faction);
                return "PMP_TacticalDataWebWarbringer_BackupCount".Translate(n);
            }
        }
    }
}
