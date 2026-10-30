using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityStubs.Rendering;
using Waylith.Rendering;
using RenderTexture = UnityEngine.RenderTexture;

namespace PMP_Persona.VanguardSupport
{
    internal sealed class OrbitalSensorCapture
    {
        private static readonly FieldInfo CachedSnapshot = AccessTools.Field(typeof(MeshDrawSubmission), "cachedSnapshot");
        private readonly List<MeshDrawCommand> commands = new List<MeshDrawCommand>();

        internal bool Render(Camera source, RenderTexture target, Color background)
        {
            var renderer = CameraTextureRenderer.Current;
            if (renderer == null) return false;
            commands.Clear();
            try
            {
                var snapshot = (MeshDrawSubmissionSnapshot?)CachedSnapshot.GetValue(null);
                if (snapshot.HasValue)
                {
                    // Map submission flips ActiveArena. Camera.Render copies the old commands
                    // but reads their ranges from the new arena: shields lose tint/alpha and
                    // unrelated per-draw properties can flicker across their whole quad.
                    var frame = snapshot.Value;
                    var destination = MeshDrawSubmission.ActiveArena;
                    for (int i = 0; i < frame.CommandCount; i++)
                    {
                        var draw = frame.Commands[i];
                        if ((uint)draw.Layer >= 32 || (source.cullingMask & (1 << draw.Layer)) == 0) continue;
                        var properties = destination.Copy(frame.PropertyArena.GetProperties(draw.PropertyRange));
                        commands.Add(new MeshDrawCommand(draw.Mesh, draw.Material, properties, draw.Matrix,
                            draw.SubmeshIndex, draw.PassId, draw.Instancing, draw.RenderQueueOverride, draw.Layer, draw.Storage));
                    }
                }
                else
                {
                    // Before submission, pending commands already refer to ActiveArena.
                    MeshDrawSubmission.CopyPendingCommands(commands, source.cullingMask);
                }
                renderer.Render(target, commands, CameraViewFactory.GetView(source), source.rect, background);
                return true;
            }
            finally { commands.Clear(); }
        }
    }
}
