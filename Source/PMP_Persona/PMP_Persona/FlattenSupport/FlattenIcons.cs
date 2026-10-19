using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.FlattenSupport
{
    // Inventory/transport lists must not inherit the transparent occupancy texture.
    [HarmonyPatch(typeof(Widgets), nameof(Widgets.ThingIcon), new Type[] {
        typeof(Rect),typeof(Thing),typeof(float),typeof(Rot4?),typeof(bool),typeof(float),typeof(bool) })]
    internal static class FlattenItemIcon
    {
        private static bool Prefix(Rect rect, Thing thing, float alpha, float scale)
        {
            if (!(thing is Thing_FlattenedPawn sheet) || sheet.HeldPawn == null) return true;
            var old = GUI.color;
            try
            {
                GUI.color = new Color(1,1,1,alpha);
                var portrait = PortraitsCache.Get(sheet.HeldPawn,new Vector2(96,96),sheet.DisplayFacing,Vector3.zero,1f);
                GUI.DrawTexture(rect.ContractedBy(rect.width*Mathf.Max(0,1-scale)*.5f),portrait,ScaleMode.ScaleToFit);
            }
            finally {GUI.color=old;}
            return false;
        }
    }
}
