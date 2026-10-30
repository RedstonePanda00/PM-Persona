using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using PMP_Persona.ShipSupport;
using PMP_Persona.VanguardSupport;
using RimWorld;
using UnityEngine;
using UnityStubs.Rendering;
using Verse;
using Waylith.Rendering;
using Material = UnityEngine.Material;
using RenderTexture = UnityEngine.RenderTexture;
using MaterialPropertyBlock = UnityEngine.MaterialPropertyBlock;
using Graphics = UnityEngine.Graphics;
using Texture2D = UnityEngine.Texture2D;

namespace PMP.Tests
{
    public sealed class OrbitalSensorAudit : GameComponent
    {
        int frame;
        bool done;
        OrbitalSupportSession session;
        string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "OrbitalSensorAudit");
        public OrbitalSensorAudit(Game game) { }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp-orbital-sensor-audit") || done || Find.CurrentMap == null) return;
            try
            {
                if (++frame == 100)
                {
                    Directory.CreateDirectory(Folder);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    TestNativeCapture();
                    var map = Find.CurrentMap;
                    map.fogGrid.ClearAllFog();
                    foreach (var pair in new[] { ("ShieldGeneratorBullets", -10), ("ShieldGeneratorMortar", 10) })
                    {
                        var at = map.Center + new IntVec3(pair.Item2, 0, 0);
                        foreach (var thing in at.GetThingList(map).ToList()) if (!(thing is Pawn)) thing.Destroy();
                        var shield = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(pair.Item1));
                        shield.SetFaction(Faction.OfMechanoids);
                        GenSpawn.Spawn(shield, at, map, WipeMode.Vanish);
                        var interceptor = shield.TryGetComp<CompProjectileInterceptor>();
                        Traverse.Create(interceptor).Field("startedChargingTick").SetValue(-1);
                        Check(interceptor.Active, pair.Item1 + " active");
                        // Keep both vanilla shield meshes clearly visible in screenshots;
                        // distinct audit-only colors also exercise their shared material.
                        interceptor.Props.minAlpha = .4f;
                        interceptor.Props.color = pair.Item2 < 0 ? new Color(.2f, .8f, 1) : new Color(1, .3f, .1f);
                    }
                    Begin("PMP_Support_Prism");
                }
                if (frame == 120 || frame == 121 || frame == 122) Capture("Prism-IR-" + frame);
                if (frame == 130) Traverse.Create(session).Field("Infrared").SetValue(false);
                if (frame == 140) Capture("Prism-optical");
                if (frame == 150) { session.Close(); Begin("PMP_Support_Vanguard"); }
                if (frame == 170 || frame == 171 || frame == 172) Capture("Zeta-IR-" + frame);
                if (frame == 180) Traverse.Create(session).Field("Infrared").SetValue(false);
                if (frame == 190) Capture("Zeta-optical");
                if (frame == 205)
                {
                    session.Close();
                    Check(!Log.Messages.Any(m => m.type == LogMessageType.Error), "no game errors");
                    File.WriteAllText(Path.Combine(Folder, "PASS.txt"), "Native GPU capture matches reference across 8 poisoned arenas; old capture reproduced; Prism/Zeta IR and optical shield screenshots; no game errors.");
                    done = true; Root.Shutdown(); Application.Quit();
                }
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "FAIL.txt"), ex.ToString());
                done = true; Root.Shutdown(); Application.Quit();
            }
        }

        void Begin(string def)
        {
            session = (OrbitalSupportSession)AccessTools.Method(typeof(OrbitalSupportSession), "Attach")
                .Invoke(null, new object[] { Find.CurrentMap, DefDatabase<ShipSupportDef>.GetNamed(def) });
            var phase = AccessTools.Field(typeof(OrbitalSupportSession), "Phase");
            phase.SetValue(session, Enum.Parse(phase.FieldType, "FireControl"));
            AccessTools.Method(typeof(VanguardFireControl), "Begin").Invoke(Current.Game.GetComponent<VanguardFireControl>(), new object[] { session });
            Traverse.Create(session).Field("ViewPosition").SetValue(Find.CurrentMap.Center.ToVector3Shifted());
            AccessTools.Method(typeof(VanguardFireControl), "ApplyCamera").Invoke(Current.Game.GetComponent<VanguardFireControl>(), null);
        }

        void TestNativeCapture()
        {
            var snapshotField = AccessTools.Field(typeof(MeshDrawSubmission), "cachedSnapshot");
            var arenas = (DrawPropertyArena[])AccessTools.Field(typeof(MeshDrawSubmission), "arenas").GetValue(null);
            int index = (int)AccessTools.Field(typeof(MeshDrawSubmission), "currentArenaIndex").GetValue(null);
            var savedArena = arenas[index]; var savedSnapshot = snapshotField.GetValue(null);
            var obj = new GameObject("Orbital sensor GPU audit", typeof(Camera));
            var camera = obj.GetComponent<Camera>(); camera.enabled = false;
            camera.orthographic = true; camera.orthographicSize = 14; camera.aspect = 1;
            camera.transform.position = new Vector3(0, 20, 0); camera.transform.rotation = Quaternion.Euler(90, 0, 0);
            var target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32); target.Create();
            var fieldMaterial = MaterialPool.MatFrom("Other/ForceField", ShaderDatabase.MoteGlow);
            var materials = new List<Material>();
            try
            {
                var commands = new List<MeshDrawCommand>();
                var sourceArena = new DrawPropertyArena();
                var colors = new[] { new Color(.2f, .8f, 1, .09f), new Color(1, .3f, .1f, .42f) };
                using (MeshDrawSubmission.UsePropertyArena(sourceArena))
                using (MeshDrawSubmission.BeginCapture(commands))
                    for (int i = 0; i < 2; i++)
                    {
                        var block = new MaterialPropertyBlock(); block.SetColor(ShaderPropertyIDs.Color, colors[i]);
                        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(new Vector3(i == 0 ? -6 : 6, 0, 0), Quaternion.identity, new Vector3(12, 1, 12)), fieldMaterial, 0, null, 0, block);
                    }
                var reference = new List<MeshDrawCommand>();
                using (MeshDrawSubmission.BeginCapture(reference))
                    for (int i = 0; i < 2; i++)
                    {
                        var material = new Material(fieldMaterial); material.color = colors[i]; materials.Add(material);
                        Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(new Vector3(i == 0 ? -6 : 6, 0, 0), Quaternion.identity, new Vector3(12, 1, 12)), material, 0);
                    }
                var backend = CameraTextureRenderer.Current;
                var background = new Color(.025f, .03f, .035f, 1);
                backend.Render(target, reference, CameraViewFactory.GetView(camera), camera.rect, background);
                Check(backend.TryReadPixels(target, out var expected), "reference GPU pixels available");
                Check(expected.Where((b, i) => i % 4 != 3).Max() > 20, "reference contains visible tinted shields");
                Save(target, "reference-shields.png");
                snapshotField.SetValue(null, new MeshDrawSubmissionSnapshot(commands.ToArray(), commands.Count, sourceArena, new List<PersistentDrawCallSource>()));
                var captureType = typeof(VanguardFireControl).Assembly.GetType("PMP_Persona.VanguardSupport.OrbitalSensorCapture");
                var capture = Activator.CreateInstance(captureType, true);
                var render = AccessTools.Method(captureType, "Render");
                for (int pass = 0; pass < 8; pass++)
                {
                    var poison = new DrawPropertyArena(); var block = new MaterialPropertyBlock();
                    block.SetColor(ShaderPropertyIDs.Color, new Color(1, pass * .1f, 1, 1));
                    arenas[index] = poison;
                    for (int i = 0; i < 6 + pass; i++) MeshDrawSubmission.CopyProperties(block);
                    if (pass == 0)
                    {
                        backend.Render(target, commands, CameraViewFactory.GetView(camera), camera.rect, background);
                        Check(backend.TryReadPixels(target, out var broken) && !expected.SequenceEqual(broken), "old path reproduces lost tint/alpha");
                        Save(target, "old-capture-shields.png");
                    }
                    Check((bool)render.Invoke(capture, new object[] { camera, target, background }), "sensor rendered " + pass);
                    Check(backend.TryReadPixels(target, out var actual) && expected.SequenceEqual(actual), "exact GPU pixel match despite unrelated arena data " + pass);
                }
                Save(target, "fixed-shields.png");
            }
            finally
            {
                arenas[index] = savedArena; snapshotField.SetValue(null, savedSnapshot);
                foreach (var material in materials) UnityEngine.Object.Destroy(material);
                target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(obj);
            }
        }
        void Save(RenderTexture target, string name)
        {
            var previous = RenderTexture.active;
            var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            try { RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply(); File.WriteAllBytes(Path.Combine(Folder, name), texture.EncodeToPNG()); }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(texture); }
        }
        void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png"));
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            File.AppendAllText(Path.Combine(Folder, "checks.txt"), "PASS " + message + "\n");
        }
    }
}
