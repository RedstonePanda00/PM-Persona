using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using PMP_Persona.Storyteller;
using PMP_Persona.SupernovaSupport;
using PMP_Persona.Rendering;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona
{
    // Command-line-enabled integration audit.
    public class PersonaAudit17 : GameComponent
    {
        private int stage, frames;
        private static readonly List<string> results = new List<string>();
        public PersonaAudit17(Game game) { }
        public override void ExposeData() => Scribe_Values.Look(ref stage, "pmp17AuditStage");
        public override void LoadedGame()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17audit") || stage != 1) return;
            // Verify the 30-tick intro before the 90-frame screenshot warmup.
            Check("pending warp clock survives save/reload", () => {
                var deploy = MapComponent_SupernovaWarpDeploy.Get(Find.CurrentMap);
                var pending = Traverse.Create(deploy).Field("pending").GetValue<System.Collections.IList>();
                Assert(pending.Count == 1, "pending intro lost on reload");
                Assert(Traverse.Create(pending[0]).Field("ticksRemaining").GetValue<int>() == 30, "intro clock jumped on reload");
                Assert(Traverse.Create(pending[0]).Field("totalTicks").GetValue<int>() == 60, "intro duration lost on reload");
            });
        }
        private static string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "PersonaAudit17");
        private static void Check(string name, Action action)
        {
            try { action(); results.Add("PASS " + name); }
            catch (Exception ex) { results.Add("FAIL " + name + ": " + ex); }
            File.WriteAllLines(Path.Combine(Folder, "results.txt"), results);
        }
        private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17audit") || Find.CurrentMap == null || stage == 3 || ++frames < 90) return;
            Directory.CreateDirectory(Folder);
            if (stage == 1)
            {
                stage = 3;
                Check("saved honor and anti-farm ledger", () => {
                    var state = MasonKaldalisState.Current;
                    Assert(Math.Abs(state.Honor - 15) < .001, "honor lost on load");
                    Assert(state.Paused && state.BattleDrive == 83, "pause/drive lost on load");
                    state.Paused = false;
                    state.Award("ReloadReward", 10);
                    Assert(state.Honor == 15, "daily reward cap lost on load");
                    Assert(!state.Once("audit-once"), "completion dedupe lost on load");
                });
                Check("game log has no errors", () => {
                    var errors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToArray();
                    File.WriteAllLines(Path.Combine(Folder, "errors.txt"), errors);
                    Assert(errors.Length == 0, string.Join("\n", errors));
                });
                File.WriteAllText(Path.Combine(Folder, "complete.txt"), results.Any(r => r.StartsWith("FAIL")) ? "FAIL" : "PASS");
                Application.Quit(); return;
            }
            if (stage != 0) return;
            stage = 2;
            Check("all Persona Harmony patches installed", () => {
                var classes = typeof(PersonaAudit17).Assembly.GetTypes().Where(t => t.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0).ToList();
                var patched = Harmony.GetAllPatchedMethods().SelectMany(m => Harmony.GetPatchInfo(m).Prefixes.Concat(Harmony.GetPatchInfo(m).Postfixes).Concat(Harmony.GetPatchInfo(m).Transpilers)).Select(p => p.PatchMethod.DeclaringType).ToHashSet();
                Assert(classes.All(patched.Contains), "Missing patches: " + string.Join(", ", classes.Where(t => !patched.Contains(t)).Select(t => t.Name)));
            });
            foreach (MaterialDef def in DefDatabase<MaterialDef>.AllDefsListForReading.Where(d => d.defName.StartsWith("PMP17_Mat_")))
                Check("Vulkan material " + def.defName, () => Capture(def));
            Check("3D astronomical volume: stages and camera angles", CaptureAstronomy);
            Check("queued volume animation and colour retention", CaptureQueuedStar);
            Check("RenderTree plasma: humanoid, animal, mech and downed poses", CaptureTargetPlasma);
            Check("building plasma and connected wall unions", () => BuildingPlasmaAudit17.Run(Folder));
            Check("PMC rupture and enlarging/trembling supernova intro", CaptureWarpIntro);
            Check("60-tick deployment and field handoff", () => {
                var map = Find.CurrentMap;
                var cell = map.AllCells.First(c => c.Standable(map) && !c.Fogged(map) && c.GetFirstBuilding(map) == null && c.DistanceTo(map.Center) > 40);
                var deploy = MapComponent_SupernovaWarpDeploy.Get(map);
                var field = MapComponent_SupernovaField.Get(map);
                Assert(deploy.ScheduleDeploy(cell), "schedule failed");
                for (int i = 0; i < 59; i++) deploy.MapComponentTick();
                Assert(deploy.TryGetPendingTarget(out _) && !field.Instances.Any(t => t.Position == cell), "field spawned before 60 ticks");
                deploy.MapComponentDraw();
                deploy.MapComponentTick();
                var star = field.Instances.Single(t => t.Position == cell);
                Assert(!deploy.TryGetPendingTarget(out _), "pending deployment not consumed");
                deploy.MapComponentTick();
                Assert(field.Instances.Count(t => t.Position == cell) == 1, "duplicate deployment");
                star.Destroy(DestroyMode.Vanish);
            });
            Check("single-schedule storyteller and honor rules", () => {
                var def = DefDatabase<StorytellerDef>.GetNamed(MasonKaldalisState.DefName);
                Assert(def != null, "missing storyteller definition");
                File.WriteAllLines(Path.Combine(Folder, "storyteller-comps.txt"), def.comps.Select(c => c == null ? "NULL" : c.GetType().FullName + " -> " + c.compClass));
                Current.Game.storyteller = new RimWorld.Storyteller(def, DifficultyDefOf.Rough);
                Assert(Find.Storyteller.storytellerComps.OfType<StorytellerComp_MasonKaldalis>().Count() == 1, "scheduler count");
                var state = MasonKaldalisState.Current;
                state.Honor = 0;
                state.Award("audit", 8); state.Award("audit", 8); state.Award("audit", 8); state.Award("audit", 8);
                Assert(state.Honor == 14, "diminishing returns");
                state.Award("audit-other", 8); Assert(state.Honor == 15, "daily cap");
                state.Award("audit-penalty", -400); Assert(state.Honor == -200, "penalties bypass positive cap and respect floor");
                state.Award("audit-restoration", 400); Assert(state.Honor == -200, "penalties restored daily reward allowance");
                state.Honor = 600;
                var pawn = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First();
                {
                    Assert(pawn.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Wander_Sad, forceWake: true, causedByMood: true), "create mental state");
                    state.AttemptRecovery(pawn);
                    Assert(!pawn.InMentalState && !state.RecoveryAvailable, "appeal must recover and use global cooldown");
                }
                state.Honor = 15; state.Once("audit-once");
            });
            Check("shared scheduling and pause", () => {
                var state = MasonKaldalisState.Current;
                var fields = Traverse.Create(state);
                state.Budget.nextTick = GenTicks.TicksGame - 1;
                fields.Field("scheduleChecked").SetValue(-1);
                Assert(state.ScheduleDue(.65185185f) && !state.ScheduleDue(.65185185f), "multiple targets duplicate an event");
                state.Paused = true;
                float honor = state.Honor;
                state.Award("pause", -20);
                Assert(state.Honor == honor && !state.Enabled, "pause allowed honor settlement");
                state.Paused = false;
            });
            Check("honor thresholds and forced refusal", () => {
                var state = MasonKaldalisState.Current;
                var pawn = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First();
                var hediffDef = DefDatabase<HediffDef>.GetNamed("PMP_CommandHonor");
                state.Honor = -1; Traverse.Create(state).Method("SyncEffects").GetValue();
                var hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                Assert(hediff.CurStage.statOffsets.Single().value > 0, "negative honor makes breaks harder");
                var config = MasonKaldalisState.Config; float chance = config.draftRefusalChance;
                try {
                    config.draftRefusalChance = 1;
                    state.Honor = -100;
                    pawn.drafter.Drafted = true;
                    Assert(!pawn.Drafted && !pawn.InMentalState && pawn.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("PMP_CommandRefusalTimer")), "draft was not refused");
                } finally { config.draftRefusalChance = chance; }
                state.Honor = 600; Traverse.Create(state).Method("SyncEffects").GetValue();
                Assert(hediff.CurStage.statOffsets.Single(s => s.stat == StatDefOf.MentalBreakThreshold).value < 0 && hediff.CurStage.statFactors.Count == 2, "high tier effects missing");
                state.Honor = 0; Traverse.Create(state).Method("SyncEffects").GetValue();
                Assert(!pawn.health.hediffSet.HasHediff(hediffDef), "normal honor retains modifier");
                Assert(HonorRules.Decay(-.25f) == 0 && HonorRules.Decay(.5f) == 0, "decay crossed zero");
                state.Honor = 15;
            });
            Check("RimCampaign Roslyn extension compilation", () => {
                var projectType = AccessTools.TypeByName("RimCampaign.Core.Project");
                var project = Activator.CreateInstance(projectType);
                projectType.GetProperty("PackageId").SetValue(project, "audit.persona17");
                string source = Path.Combine(Folder, "Extension.cs");
                File.WriteAllText(source, "public static class AuditExtension { public static int Answer => 42; }");
                string output = (string)AccessTools.Method(AccessTools.TypeByName("RimCampaign.Editor.ExtensionCompiler"), "Compile").Invoke(null, new[] { project, source });
                Assert(File.Exists(output) && new FileInfo(output).Length > 0, "extension DLL missing");
            });
            Check("save and reload", () => {
                MasonKaldalisState.Current.BattleDrive = 83;
                MasonKaldalisState.Current.Paused = true;
                // Save a paused deployment for exact-clock verification.
                Find.TickManager.Pause();
                var deploy = MapComponent_SupernovaWarpDeploy.Get(Find.CurrentMap);
                var cell = Find.CurrentMap.AllCells.First(c => c.Standable(Find.CurrentMap) && !c.Fogged(Find.CurrentMap) && c.GetFirstBuilding(Find.CurrentMap) == null);
                deploy.ScheduleDeploy(cell);
                for (int i = 0; i < 30; i++) deploy.MapComponentTick();
                stage = 1;
                GameDataSaveLoader.SaveGame("PMP17Audit");
                LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("PMP17Audit"));
            });
        }
        private static void Capture(MaterialDef def)
        {
            var material = PersonaMaterials.Create(def.defName);
            var camera = TargetOutlineCaptureCamera.Create();
            var target = RenderTexture.GetTemporary(256, 256, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            bool visible = false;
            try
            {
                for (int frame = 0; frame < 3; frame++)
                {
                    float t = .2f + frame * .3f;
                    material.SetFloat("_LocalTime", t * 4); material.SetFloat("_LifePhase", t);
                    material.SetFloat("_SN_Progress", t); material.SetFloat("_SN_PhaseExpansion", 1);
                    material.SetFloat("_SN_PhaseInteraction", .6f); material.SetFloat("_SN_ShockFront01", 1);
                    material.SetFloat("_SN_Visibility", 1); material.SetFloat("_FadeIn", 1);
                    material.SetVector("_Time", new Vector4(t / 20, t, t * 2, t * 3));
                    camera.RenderTo(target, .55f, () => {
                        Assert(material.SetPass(0), "SetPass failed");
                        Graphics.DrawMeshNow(MeshPool.plane10, Matrix4x4.identity);
                    });
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); image.Apply();
                    visible |= image.GetPixels32().Any(c => c.r + c.g + c.b > 3);
                    File.WriteAllBytes(Path.Combine(Folder, def.defName + "-" + frame + ".png"), image.EncodeToPNG());
                }
                Assert(visible, "all sampled frames blank");
            }
            finally
            {
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(camera.gameObject);
            }
        }
        private static void CaptureWarpIntro()
        {
            Assert(WarpBlackHoleVisual.TryDraw(Find.CurrentMap.Center.ToVector3Shifted(), 2, Rot4.North, .05f, 0, .4f, new MaterialPropertyBlock()), "PMC rupture asset unavailable");
            var rupture = new Material(Traverse.Create(typeof(WarpBlackHoleVisual)).Field("ruptureMaterial").GetValue<Material>());
            var mesh = Traverse.Create(typeof(WarpBlackHoleVisual)).Field("mesh").GetValue<Mesh>();
            var star = PersonaMaterials.Create(AstroSupernovaRenderer.MaterialName);
            var capture = TargetOutlineCaptureCamera.Create(); var camera = capture.GetComponent<Camera>();
            camera.nearClipPlane = .1f; camera.farClipPlane = 100;
            var target = RenderTexture.GetTemporary(384,384,24,RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(384,384,TextureFormat.RGBA32,false);
            var previous = RenderTexture.active;
            float largestScale = 0, largestOffset = 0;
            try {
                for (int tick = 0; tick <= 60; tick += 3) {
                    float progress = tick/60f;
                    float ruptureProgress = SupernovaWarpVisuals.RuptureProgress(progress);
                    float shrink = SupernovaWarpVisuals.RuptureShrink(progress);
                    WarpBlackHoleEnvelope.Evaluate(ruptureProgress,shrink,out float blackHoleScale,out _,out _);
                    Assert(blackHoleScale == 0, "intro entered black-hole phase");
                    SupernovaWarpVisuals.Appearance(progress,2,out float scale,out float visibility,out Vector3 offset);
                    largestScale = Math.Max(largestScale,scale); largestOffset = Math.Max(largestOffset,offset.magnitude);
                    rupture.SetFloat("_Progress",ruptureProgress); rupture.SetFloat("_WarpTime",tick/60f);
                    rupture.SetFloat("_Visibility",WarpBlackHoleEnvelope.RuptureVisibility(ruptureProgress,shrink));
                    capture.RenderTo(target,3.4f,() => {
                        Assert(rupture.SetPass(0),"rupture SetPass failed");
                        Graphics.DrawMeshNow(mesh,Matrix4x4.TRS(Vector3.zero,camera.transform.rotation,Vector3.one*5.8f));
                        if (scale > .001f) {
                            var phase = new SupernovaShellStageState { localTime=(tick-60)/60f,totalDuration=1,visibility=0 };
                            AstroSupernovaRenderer.DrawPreview(star,camera,phase,1f/1.8f,27,
                                Matrix4x4.TRS(offset,Quaternion.identity,Vector3.one*(scale*1.8f)),visibility);
                        }
                    });
                    RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,384,384),0,0); pixels.Apply();
                    if (tick == 24) Assert(pixels.GetPixels32().Any(c => c.r+c.g+c.b>20),"rupture invisible");
                    File.WriteAllBytes(Path.Combine(Folder,"Intro-"+tick.ToString("00")+".png"),pixels.EncodeToPNG());
                }
                Assert(largestScale>1.1f && largestOffset>.001f,"PMC overshoot or tremor missing");
                SupernovaWarpVisuals.Appearance(1,2,out float finalScale,out float finalVisibility,out Vector3 finalOffset);
                Assert(Math.Abs(finalScale-1)<.001 && finalVisibility==1 && finalOffset==Vector3.zero,"handoff not settled");
            } finally {
                RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(pixels); UnityEngine.Object.Destroy(star); UnityEngine.Object.Destroy(rupture); UnityEngine.Object.Destroy(capture.gameObject);
            }
        }
        private static void CaptureQueuedStar()
        {
            var material = PersonaMaterials.Create(AstroSupernovaRenderer.MaterialName);
            var capture = TargetOutlineCaptureCamera.Create();
            var camera = capture.GetComponent<Camera>();
            var target = RenderTexture.GetTemporary(384,384,24,RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(384,384,TextureFormat.RGBA32,false);
            var previous = RenderTexture.active;
            try
            {
                Color32[] first = null;
                for (int i=0;i<8;i++)
                {
                    var state = new SupernovaShellStageState {localTime=13+i*.25f,totalDuration=1,visibility=0};
                    var block = new MaterialPropertyBlock();
                    AstroSupernovaRenderer.Configure(block,Matrix4x4.identity,camera,1,.55f,state,27,false);
                    block.SetFloat("_Appearance",1);
                    // Verify queued DrawMesh/MPB submissions on separate layers.
                    int layer=23+i; camera.cullingMask=1<<layer;
                    Graphics.DrawMesh(AstroSupernovaRenderer.BoundsMesh,Matrix4x4.identity,material,layer,null,0,block);
                    block.Clear(); // Verify submission retains values after block reuse.
                    capture.RenderTo(target,1.08f,()=>{});
                    RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,384,384),0,0); pixels.Apply();
                    var colors=pixels.GetPixels32();
                    Assert(colors.Count(c=>c.r>c.b*1.3f && c.r>70)>2500,"stellar surface lost warm structure");
                    if(first==null) first=colors;
                    else Assert(colors.Where((c,j)=>Math.Abs(c.r-first[j].r)+Math.Abs(c.g-first[j].g)+Math.Abs(c.b-first[j].b)>25).Count()>1200,"queued shader clock not visibly animating");
                    File.WriteAllBytes(Path.Combine(Folder,"Star-motion-"+i+".png"),pixels.EncodeToPNG());
                }
                camera.cullingMask=0;
                // Render the preview sequence.
                for(int i=0;i<48;i++)
                {
                    var state=new SupernovaShellStageState {localTime=13+i*.1f,totalDuration=1,visibility=0};
                    capture.RenderTo(target,1.08f,()=>AstroSupernovaRenderer.DrawPreview(material,camera,state,.55f,27));
                    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,384,384),0,0);pixels.Apply();
                    File.WriteAllBytes(Path.Combine(Folder,"Star-sequence-"+i.ToString("00")+".png"),pixels.EncodeToPNG());
                    state=SupernovaShellStageEvaluator.Evaluate(i*.14f,SupernovaShellStageConfig.Default);
                    capture.RenderTo(target,1.08f,()=>AstroSupernovaRenderer.DrawPreview(material,camera,state,.035f,27));
                    RenderTexture.active=target;pixels.ReadPixels(new Rect(0,0,384,384),0,0);pixels.Apply();
                    File.WriteAllBytes(Path.Combine(Folder,"Shell-sequence-"+i.ToString("00")+".png"),pixels.EncodeToPNG());
                }
            }
            finally { RenderTexture.active=previous; RenderTexture.ReleaseTemporary(target); UnityEngine.Object.Destroy(pixels); UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(capture.gameObject); }
        }

        private static void CaptureTargetPlasma()
        {
            var capture = TargetOutlineCaptureCamera.Create();
            var target = RenderTexture.GetTemporary(384,384,24,RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(384,384,TextureFormat.RGBA32,false);
            var previous = RenderTexture.active;
            var pawns = new List<Pawn>();
            try
            {
                foreach(string kind in new[]{"Colonist","Muffalo","Mech_CentipedeBlaster"})
                {
                    var pawn=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind)); pawns.Add(pawn);
                    GenSpawn.Spawn(pawn,Find.CurrentMap.Center,Find.CurrentMap);
                    if(pawn.RaceProps.Humanlike) pawn.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_Duster"),ThingDefOf.Cloth));
                    foreach(int rotation in new[]{0,1,2,3,4})
                    {
                        if(rotation==4) HealthUtility.DamageUntilDowned(pawn);
                        else pawn.Rotation=new Rot4(rotation);
                        using(var silhouette=TargetPlasmaRenderer.CaptureForAudit(pawn))
                        {
                            Assert(silhouette.NodeCount>0,"no tree requests for "+kind);
                            RenderTexture.active=silhouette.Texture;
                            var maskPixels=new Texture2D(256,256,TextureFormat.RGBA32,false);
                            Color32[] maskColors;
                            try {
                                maskPixels.ReadPixels(new Rect(0,0,256,256),0,0);maskPixels.Apply();
                                maskColors=maskPixels.GetPixels32();
                                Assert(maskColors.Count(c=>c.a>50)>100,"empty silhouette "+kind);
                                File.WriteAllBytes(Path.Combine(Folder,"Mask-"+kind+"-"+rotation+".png"),maskPixels.EncodeToPNG());
                            } finally { UnityEngine.Object.Destroy(maskPixels); }
                            var block=new MaterialPropertyBlock();
                            TargetPlasmaRenderer.Configure(block,new Vector3(1,0,.4f),1,1,2.5f,27,silhouette.HalfSize);
                            silhouette.Material.SetVector("_Flow",block.GetVector("_Flow"));
                            silhouette.Material.SetVector("_Clock",block.GetVector("_Clock"));
                            capture.RenderTo(target,silhouette.HalfSize,()=>{
                                Assert(silhouette.Material.SetPass(0),"plasma shader unavailable");
                                Graphics.DrawMeshNow(MeshPool.plane10,Matrix4x4.Scale(new Vector3(silhouette.HalfSize*2,1,silhouette.HalfSize*2)));
                            });
                            RenderTexture.active=target; pixels.ReadPixels(new Rect(0,0,384,384),0,0);pixels.Apply();
                            Assert(pixels.GetPixels32().Count(c=>c.r+c.g+c.b>40)>150,"empty plasma "+kind);
                            // Verify surface coverage independently of the luminous outline.
                            var plasmaColors=pixels.GetPixels32();
                            int interior=0, covered=0;
                            for(int y=4;y<252;y++) for(int x=4;x<252;x++) {
                                int index=y*256+x;
                                if(maskColors[index].a<230 || maskColors[index-4].a<230 || maskColors[index+4].a<230 ||
                                   maskColors[index-4*256].a<230 || maskColors[index+4*256].a<230) continue;
                                interior++;
                                var pixel=plasmaColors[(int)((y+.5f)*1.5f)*384+(int)((x+.5f)*1.5f)];
                                if(pixel.a>60 && pixel.r+pixel.g+pixel.b>60) covered++;
                            }
                            Assert(interior>10 && covered>=interior*.95f,"plasma does not cover RenderTree interior: "+kind+" "+rotation+" "+covered+"/"+interior);
                            File.WriteAllBytes(Path.Combine(Folder,"Plasma-"+kind+"-"+rotation+".png"),pixels.EncodeToPNG());
                        }
                    }
                    pawn.Destroy(DestroyMode.Vanish);
                }
            }
            finally
            {
                foreach(var pawn in pawns) if(!pawn.Destroyed) pawn.Destroy(DestroyMode.Vanish);
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);UnityEngine.Object.Destroy(pixels);UnityEngine.Object.Destroy(capture.gameObject);
            }
        }

        private static void CaptureAstronomy()
        {
            var material = PersonaMaterials.Create(AstroSupernovaRenderer.MaterialName);
            var capture = TargetOutlineCaptureCamera.Create();
            var camera = capture.GetComponent<Camera>();
            camera.nearClipPlane = .1f; camera.farClipPlane = 20;
            var target = RenderTexture.GetTemporary(384, 384, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(384, 384, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                foreach (int angle in new[] { 90, 35 })
                foreach (float time in new[] { 0f, .12f, .35f, 1.6f, 3.2f, 5.7f, 6.6f })
                {
                    camera.transform.rotation = Quaternion.Euler(angle, 25, 0);
                    camera.transform.position = -camera.transform.forward * 5;
                    var stage = SupernovaShellStageEvaluator.Evaluate(time, SupernovaShellStageConfig.Default);
                    capture.RenderTo(target, 1.08f, () => AstroSupernovaRenderer.DrawPreview(material, camera, stage, .035f, 27));
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,384,384),0,0); pixels.Apply();
                    Assert(pixels.GetPixels32().Any(c => c.r+c.g+c.b>20), "blank volume at "+time);
                    File.WriteAllBytes(Path.Combine(Folder, "Astro-"+angle+"-"+time.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+".png"),pixels.EncodeToPNG());
                }
                var corona = new SupernovaShellStageState { localTime = 13, totalDuration = 1, visibility = 0 };
                capture.RenderTo(target, 1.08f, () => AstroSupernovaRenderer.DrawPreview(material, camera, corona, .55f, 27));
                RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,384,384),0,0); pixels.Apply();
                Assert(pixels.GetPixels32().Count(c => c.r+c.g+c.b>20)>5000, "stellar surface blank");
                File.WriteAllBytes(Path.Combine(Folder,"Astro-star.png"),pixels.EncodeToPNG());
                // Verify exit-face rendering from outside and inside the cube.
                camera.orthographic = false; camera.fieldOfView = 55;
                foreach (float distance in new[] { 2.5f, .8f })
                {
                    camera.transform.rotation = Quaternion.Euler(35, 70, 0);
                    camera.transform.position = -camera.transform.forward * distance;
                    var stage = SupernovaShellStageEvaluator.Evaluate(3.2f, SupernovaShellStageConfig.Default);
                    capture.RenderTo(target, 1.08f, () => AstroSupernovaRenderer.DrawPreview(material, camera, stage, .035f, 27));
                    RenderTexture.active = target; pixels.ReadPixels(new Rect(0,0,384,384),0,0); pixels.Apply();
                    Assert(pixels.GetPixels32().Count(c => c.r+c.g+c.b>20)>500, "blank perspective volume at "+distance);
                    File.WriteAllBytes(Path.Combine(Folder,"Astro-perspective-"+distance.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+".png"),pixels.EncodeToPNG());
                }
            }
            finally
            {
                RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.Destroy(pixels);UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(capture.gameObject);
            }
        }
    }
}
