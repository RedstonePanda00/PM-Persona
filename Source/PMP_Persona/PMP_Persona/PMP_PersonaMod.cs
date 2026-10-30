using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    public class PMP_PersonaMod : Mod
    {
        public static PMP_PersonaMod Instance { get; private set; }

        public PMP_PersonaModSettings Settings { get; private set; }

        public PMP_PersonaMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = LoadSettingsOrNew(content);
            AssignBaseModSettingsField(Settings);
            BindModSettingsOwner(Settings);
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            PMP_PersonaModSettingsUI.Draw(inRect, Settings);
        }

        public override string SettingsCategory() => (string)"PMP.Settings.Category".Translate();

        // Same path as LoadedModManager.GetSettingsFilename (private in vanilla).
        private string ModSettingsFilePath(ModContentPack content)
        {
            return Path.Combine(GenFilePaths.ConfigFolderPath, GenText.SanitizeFilename($"Mod_{content.FolderName}_{GetType().Name}.xml"));
        }

        private PMP_PersonaModSettings LoadSettingsOrNew(ModContentPack content)
        {
            string path = ModSettingsFilePath(content);
            PMP_PersonaModSettings target = null;
            bool loggedFromCatch = false;
            try
            {
                if (File.Exists(path))
                {
                    Scribe.loader.InitLoading(path);
                    try
                    {
                        Scribe_Deep.Look(ref target, "ModSettings");
                    }
                    finally
                    {
                        Scribe.loader.FinalizeLoading();
                    }
                }
            }
            catch (Exception ex)
            {
                loggedFromCatch = true;
                Log.Error(
                    "[PMP_Persona] Failed to load mod settings (invalid or outdated XML). Delete this file, restart RimWorld, then reconfigure mod options.\n"
                    + path
                    + "\n\nException:\n"
                    + ex);
                target = null;
            }

            if (target == null)
            {
                if (File.Exists(path) && !loggedFromCatch)
                {
                    Log.Error(
                        "[PMP_Persona] Mod settings failed to load (empty or invalid). Delete this file, restart RimWorld, then reconfigure mod options.\n"
                        + path);
                }

                return new PMP_PersonaModSettings();
            }

            return target;
        }

        // Set private Mod.modSettings for WriteSettings().
        private void AssignBaseModSettingsField(PMP_PersonaModSettings s)
        {
            FieldInfo f = typeof(Mod).GetField("modSettings", BindingFlags.Instance | BindingFlags.NonPublic);
            f?.SetValue(this, s);
        }

        // Set the owning Mod reference on ModSettings.
        private void BindModSettingsOwner(PMP_PersonaModSettings s)
        {
            PropertyInfo p = typeof(ModSettings).GetProperty("Mod", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo setter = p?.GetSetMethod(true);
            setter?.Invoke(s, new object[] { this });
        }
    }
}

