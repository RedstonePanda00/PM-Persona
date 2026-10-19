using System.Collections.Generic;
using Verse;

namespace PMP_Persona.AirPatrolLegion
{
    // Merge all defs at runtime; optional XML files can add more via LoadFolders (e.g. compat patches).
    public class AirPatrolExtraTargetsDef : Def
    {
        public List<string> extraHostileTargetDefNames = new List<string>();
    }
}
