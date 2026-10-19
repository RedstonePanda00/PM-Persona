using System;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    internal static class PersonaMaterials
    {
        internal static Material Create(string name)
        {
            MaterialDef def = DefDatabase<MaterialDef>.GetNamed(name);
            Shader shader = def.shader?.Shader;
            if (shader == null || shader.name != def.shader.defName || !shader.isSupported)
                throw new InvalidOperationException("[PMP] Native shader unavailable for " + name);
            Material material = def.CreateMaterialInstance();
            material.enableInstancing = true;
            return material;
        }
    }
}
