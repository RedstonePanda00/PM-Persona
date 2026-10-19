using System.Collections.Generic;
using Verse;

namespace PMP_Persona
{
    public class PMP_PersonaModSettings : ModSettings
    {
        public const string TabAirPatrol = "AirPatrol";

        public List<string> selectedExtraTargetDefNames = new List<string>();
        public string airPatrolPackageFilterPackageId = "";
        public string activeSettingsTab = TabAirPatrol;

        private HashSet<string> selectedSet;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref selectedExtraTargetDefNames, "selectedExtraTargetDefNames", LookMode.Value);
            Scribe_Values.Look(ref airPatrolPackageFilterPackageId, "airPatrolPackageFilterPackageId", "");
            Scribe_Values.Look(ref activeSettingsTab, "activeSettingsTab", TabAirPatrol);
            if (selectedExtraTargetDefNames == null)
                selectedExtraTargetDefNames = new List<string>();
            if (string.IsNullOrEmpty(activeSettingsTab))
                activeSettingsTab = TabAirPatrol;
            RebuildSet();
        }

        public void RebuildSet()
        {
            selectedSet = new HashSet<string>();
            for (int i = 0; i < selectedExtraTargetDefNames.Count; i++)
            {
                string n = selectedExtraTargetDefNames[i];
                if (!string.IsNullOrEmpty(n))
                    selectedSet.Add(n);
            }
        }

        public bool IsExtraTargetSelected(string defName)
        {
            if (selectedSet == null)
                RebuildSet();
            return selectedSet.Contains(defName);
        }

        public void SetExtraTargetSelected(string defName, bool value)
        {
            if (selectedSet == null)
                RebuildSet();
            bool cur = selectedSet.Contains(defName);
            if (cur == value)
                return;
            if (value)
            {
                selectedSet.Add(defName);
                selectedExtraTargetDefNames.Add(defName);
            }
            else
            {
                selectedSet.Remove(defName);
                selectedExtraTargetDefNames.Remove(defName);
            }

            Write();
        }

        public IEnumerable<string> EnumerateSelectedExtraTargetDefNames()
        {
            if (selectedExtraTargetDefNames == null)
                yield break;
            for (int i = 0; i < selectedExtraTargetDefNames.Count; i++)
            {
                string n = selectedExtraTargetDefNames[i];
                if (!string.IsNullOrEmpty(n))
                    yield return n;
            }
        }

        public void SetAirPatrolPackageFilter(string packageIdOrEmptyForAll)
        {
            airPatrolPackageFilterPackageId = packageIdOrEmptyForAll ?? "";
            Write();
        }

        public bool AirPatrolFilterAllPackages => string.IsNullOrEmpty(airPatrolPackageFilterPackageId);
    }
}
