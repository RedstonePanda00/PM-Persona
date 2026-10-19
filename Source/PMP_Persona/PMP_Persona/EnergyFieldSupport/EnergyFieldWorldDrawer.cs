using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using UnityEngine.Rendering;
using Verse;

namespace PMP_Persona.EnergyFieldSupport
{
    public static class EnergyFieldWorldDrawer
    {
        // Owned by PMC: reuse its native shader/material definition, never duplicate GLSL here.
        private static Material fieldMaterial;
        private static readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        private static readonly int PhaseId = Shader.PropertyToID("_Phase");
        private static readonly int RadiusId = Shader.PropertyToID("_Radius");

        public static void DrawForMapIfPlayerField(Map map)
        {
            if (map == null || Current.Game == null || map != Find.CurrentMap || !WorldRendererUtility.DrawingMap) return;
            Faction player = Faction.OfPlayerSilentFail;
            if (player == null) return;
            var manager = Current.Game.GetComponent<GameComponent_EnergyFieldManager>();
            if (manager == null || !manager.TryGetEnergyField(player, out Map fieldMap, out IntVec3 center) || fieldMap != map) return;
            DrawAt(map, center);
        }

        public static void DrawAt(Map map, IntVec3 center)
        {
            if (map == null || map != Find.CurrentMap || !WorldRendererUtility.DrawingMap || !center.InBounds(map)) return;
            fieldMaterial ??= PersonaMaterials.Create("PM17N_PylonEnergyField");
            float radius = EnergyFieldConstants.FieldRadiusCells;
            Properties.SetFloat(PhaseId, Time.realtimeSinceStartup % 400f);
            Properties.SetFloat(RadiusId, radius);
            Vector3 position = center.ToVector3ShiftedWithAltitude(AltitudeLayer.MetaOverlays);
            Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(position, Quaternion.identity,
                new Vector3(radius * 2, 1, radius * 2)), fieldMaterial, 0, Find.Camera, 0, Properties,
                ShadowCastingMode.Off, false);
        }

        internal static void ClearMaterial()
        {
            if (fieldMaterial != null) Object.Destroy(fieldMaterial);
            fieldMaterial = null;
        }
    }

    [HarmonyPatch(typeof(Game), nameof(Game.Dispose))]
    internal static class EnergyFieldMaterialCleanup
    { static void Prefix() => EnergyFieldWorldDrawer.ClearMaterial(); }
}
