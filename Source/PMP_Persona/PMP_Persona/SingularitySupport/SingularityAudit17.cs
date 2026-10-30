using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;
using PMP_Persona.ShipSupport;

namespace PMP_Persona.SingularitySupport
{
    // Opt-in isolated native-rendering regression, never active in a normal game.
    public sealed class SingularityAudit17 : GameComponent
    {
        private int frame, stage, heldCount, pawnId, tableId;
        private bool loading, done;
        private IntVec3 center;
        private Thing outside, lateArrival;
        private List<Thing> consumedProbe;
        private int movieIndex, lastMovieAge = -4, movieFinishedFrames;
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "SingularityAudit17");
        public SingularityAudit17(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "singularityAuditStage"); Scribe_Values.Look(ref center, "singularityAuditCenter");
            Scribe_Values.Look(ref heldCount, "singularityAuditHeld"); Scribe_Values.Look(ref pawnId, "singularityAuditPawn");
            Scribe_Values.Look(ref tableId, "singularityAuditTable");
            Scribe_References.Look(ref outside, "singularityAuditOutside");
            Scribe_References.Look(ref lateArrival, "singularityAuditLate");
        }
        public override void LoadedGame()
        { if (GenCommandLine.CommandLineArgPassed("pmp17singularityaudit")) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17singularityaudit") || loading || done || Find.CurrentMap == null || ++frame < 90) return;
            try
            {
                int f = frame - 90; Map map = Find.CurrentMap;
                var effect = map.GetComponent<SingularitySession>();
                if (stage == 2)
                {
                    if (effect != null)
                    {
                        if (effect.Age >= lastMovieAge + 3)
                        { lastMovieAge = effect.Age; Shot("Live-" + (movieIndex++).ToString("000")); }
                        return;
                    }
                    if (++movieFinishedFrames == 1) Shot("Live-" + (movieIndex++).ToString("000"));
                    if (movieFinishedFrames < 30) return;
                    Check(!Log.Messages.Any(m => m.type == LogMessageType.Error), Errors());
                    Finish("PASS"); return;
                }
                if (stage == 1)
                {
                    if (f == 0)
                    {
                        Check(effect != null && effect.Captured && effect.HeldCount == heldCount && effect.Age == 210, "Held state/age lost on load");
                        var held = effect.GetDirectlyHeldThings();
                        var pawn = held.OfType<Pawn>().Single(p => p.thingIDNumber == pawnId);
                        Check(pawn.Faction == Faction.OfPlayer && pawn.inventory.innerContainer.Any(t => t.def == ThingDefOf.Steel), "Pawn identity or nested inventory lost");
                        Check(held.Any(t => t.thingIDNumber == tableId), "Boundary building lost on load");
                        Check(lateArrival.Spawned && outside.Spawned, "Non-target changed on load");
                    }
                    if (f == 10) Shot("Restored");
                    if (f == 15)
                    {
                        consumedProbe = effect.GetDirectlyHeldThings().ToList();
                        consumedProbe.AddRange(consumedProbe.OfType<Pawn>().SelectMany(p => p.inventory.innerContainer).ToList());
                        Check(Find.CameraDriver.shaker.CurShakeMag > .1f, "Absorption did not request native camera shake");
                        CheckTransparency(effect.Snapshot);
                    }
                    if (f >= 20 && f <= 124)
                    {
                        TickTo(effect, 210 + (f - 20) * 2);
                        Shot("Frame-" + (f - 20).ToString("000"));
                    }
                    if (f == 50) Shot("Absorbing-late");
                    if (f == 100)
                        Check(effect.HeldCount == 0 && consumedProbe.All(t => t.Destroyed), "Objects or nested contents survived consumption");
                    if (f == 110) Shot("Closing");
                    if (f == 125) TickTo(effect, 420);
                    if (f == 130)
                    {
                        Check(map.GetComponent<SingularitySession>() == null, "Finished component retained");
                        Check(lateArrival.Spawned && outside.Spawned, "Late arrival/outside object was cleared");
                        Check(map.terrainGrid.TerrainAt(center).defName == "Concrete", "Ground terrain altered");
                        Check(!center.Roofed(map), "Roof not removed");
                        Shot("Finished");
                    }
                    if (f < 145) return;
                    Check(!Log.Messages.Any(m => m.type == LogMessageType.Error), Errors());
                    File.WriteAllText(Path.Combine(Folder, "results.txt"), "PASS actual screen framebuffer snapshot and native GLSL; transparent gaps and whole-effect closure verified by alpha pixel counts; native camera shake; all factions; footprint boundary building; held pawn identity and inventory; circle roofs removed with terrain retained; exact-once sampling; save/load at absorption; nested contents destroyed; later entrant and outside object preserved; completed component removed; zero Verse errors.");
                    StartLivePreview(map); return;
                }
                if (f == 0)
                {
                    Directory.CreateDirectory(Folder); Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    center = map.Center;
                    var rect = CellRect.CenteredOn(center, 25).ClipInsideMap(map);
                    GenDebug.ClearArea(rect, map);
                    foreach (var cell in rect) { map.fogGrid.Unfog(cell); map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil); }
                    foreach (var cell in CellRect.CenteredOn(center, 7)) map.terrainGrid.SetTerrain(cell, DefDatabase<TerrainDef>.GetNamed("Concrete"));
                    for (int i = -5; i <= 4; i++)
                    {
                        GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), center + new IntVec3(i, 0, 4), map);
                        if (i < 2) GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog), center + new IntVec3(-5, 0, i - 2), map);
                    }
                    var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
                    pawnId = pawn.thingIDNumber; pawn.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(ThingDefOf.Steel));
                    GenSpawn.Spawn(pawn, center + new IntVec3(-2, 0, 0), map);
                    GenSpawn.Spawn(PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile), center + new IntVec3(2, 0, 1), map);
                    var table = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Table2x2c"), ThingDefOf.WoodLog);
                    tableId = table.thingIDNumber; GenSpawn.Spawn(table, center + new IntVec3(8, 0, 0), map);
                    Check(table.OccupiedRect().Cells.Any(c => c.DistanceTo(center) > 8), "Test building not straddling boundary");
                    outside = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel), center + new IntVec3(12, 0, 0), map);
                    map.roofGrid.SetRoof(center, RoofDefOf.RoofConstructed);
                    Find.CameraDriver.config.sizeRange = new FloatRange(4, 100);
                    Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(), 18);
                    var def = DefDatabase<ShipSupportDef>.GetNamed("PMP_Support_Singularity");
                    Check(def.costList.Single().count == 500, "Cost mismatch");
                    Check(SingularitySession.Start(map, center, def.GetModExtension<SingularityConfig>()), "Session refused");
                    heldCount = SingularitySession.Collect(map, center, 8).Count;
                }
                if (f == 10) TickTo(effect, 30);
                if (f == 15) Shot("Rupture");
                if (f == 20) TickTo(effect, 60);
                if (f == 25)
                {
                    Check(effect.Captured && effect.HeldCount == heldCount, "Targets not captured");
                    Check(effect.Snapshot != null && effect.Snapshot.GetPixels32().Any(p => p.a > 0 && p.r > 10), "Snapshot blank");
                    File.WriteAllBytes(Path.Combine(Folder, "Snapshot.png"), effect.Snapshot.EncodeToPNG());
                    Check(!center.Roofed(map), "Roof retained");
                    lateArrival = GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Steel), center, map);
                    Shot("Captured");
                }
                if (f == 35) TickTo(effect, 105);
                if (f == 40) Shot("Absorbing-early");
                if (f == 50) TickTo(effect, 210);
                if (f == 55) Shot("Absorbing-middle");
                if (f == 65)
                {
                    Check(lateArrival.Spawned && effect.HeldCount == heldCount, "Recaptured later entrant");
                    Check(!Log.Messages.Any(m => m.type == LogMessageType.Error), Errors());
                    stage = 1; loading = true; GameDataSaveLoader.SaveGame("PMPSingularityAudit");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("PMPSingularityAudit"));
                }
            }
            catch (Exception e) { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "results.txt"), e.ToString()); Finish("FAIL"); }
        }
        private void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png"));
        private void StartLivePreview(Map map)
        {
            for (int i=-4;i<=4;i++) GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.Steel),center+new IntVec3(i,0,4),map);
            for (int i=-4;i<=0;i++) GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Wall,ThingDefOf.WoodLog),center+new IntVec3(-5,0,i),map);
            GenSpawn.Spawn(PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfAncientsHostile),center+new IntVec3(2,0,1),map);
            GenSpawn.Spawn(PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer),center+new IntVec3(-2,0,0),map);
            GenSpawn.Spawn(ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Table2x2c"),ThingDefOf.WoodLog),center+new IntVec3(8,0,0),map);
            SingularitySession.Start(map,center,new SingularityConfig());
            stage=2; frame=89; Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
        }
        private void CheckTransparency(Texture2D source)
        {
            var camera = TargetOutlineCaptureCamera.Create();
            var rt = RenderTexture.GetTemporary(256, 256, 24, RenderTextureFormat.ARGB32);
            var material = PersonaMaterials.Create("PMP17_SingularityMaterial");
            var previous = RenderTexture.active;
            try
            {
                material.SetTexture("_MainTex", source);
                int[] opaque = new int[4];
                var states = new[] {new Vector4(0,0,0,1), new Vector4(.5f,0,150f/360,1), new Vector4(1,.5f,330f/360,1), new Vector4(1,1,1,1)};
                for (int i=0; i<states.Length; i++)
                {
                    material.SetVector("_State", states[i]);
                    camera.RenderTo(rt, 1, () => {
                        material.SetPass(0);
                        Graphics.DrawMeshNow(MeshPool.plane10, Matrix4x4.Scale(new Vector3(2,1,2)));
                    });
                    RenderTexture.active = rt;
                    var image = new Texture2D(256,256,TextureFormat.RGBA32,false);
                    try
                    {
                        image.ReadPixels(new Rect(0,0,256,256),0,0); image.Apply();
                        opaque[i] = image.GetPixels32().Count(p => p.a > 10);
                        File.WriteAllBytes(Path.Combine(Folder,"Alpha-"+i+".png"),image.EncodeToPNG());
                    }
                    finally { UnityEngine.Object.Destroy(image); }
                }
                Check(opaque[0]>10000 && opaque[1]>300 && opaque[1]<opaque[0]*.6f, "Strands are blank or lack transparent gaps: "+string.Join(",",opaque));
                Check(opaque[2]<opaque[1]*.4f && opaque[2]>5 && opaque[3]==0, "Closure did not contract to transparent: "+string.Join(",",opaque));
                File.WriteAllText(Path.Combine(Folder,"alpha-counts.txt"),string.Join(",",opaque));
                CheckUniformContraction(material, camera, rt);
            }
            finally
            {
                RenderTexture.active=previous; RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(material); UnityEngine.Object.Destroy(camera.gameObject);
            }
        }
        private void CheckUniformContraction(Material material, TargetOutlineCaptureCamera camera, RenderTexture rt)
        {
            // Use a solid circular fixture to measure the rendered outer radius.
            var source = new Texture2D(256,256,TextureFormat.RGBA32,false);
            var image = new Texture2D(256,256,TextureFormat.RGBA32,false);
            try
            {
                var pixels = new Color32[256*256];
                for(int y=0;y<256;y++) for(int x=0;x<256;x++)
                {
                    float dx=x-127.5f,dy=y-127.5f;
                    pixels[y*256+x]=new Color32(255,255,255,(byte)(dx*dx+dy*dy<=127*127?255:0));
                }
                source.SetPixels32(pixels); source.Apply(); material.SetTexture("_MainTex",source);
                int[] ages={0,60,120,180,240,299,300,301,330,359,360};
                var report=new List<string>(); float initial=0;
                foreach(int elapsed in ages)
                {
                    float progress=elapsed/360f;
                    material.SetVector("_State",new Vector4(Mathf.Clamp01(elapsed/300f),Mathf.Clamp01((elapsed-300)/60f),progress,1));
                    camera.RenderTo(rt,1,()=>{material.SetPass(0);Graphics.DrawMeshNow(MeshPool.plane10,Matrix4x4.Scale(new Vector3(2,1,2)));});
                    RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,256,256),0,0); image.Apply();
                    var result=image.GetPixels32(); float measured=0;
                    for(int y=0;y<256;y++) for(int x=0;x<256;x++) if(result[y*256+x].a>32)
                    {float dx=x-127.5f,dy=y-127.5f;measured=Mathf.Max(measured,Mathf.Sqrt(dx*dx+dy*dy));}
                    if(elapsed==0) initial=measured;
                    float expected=initial*(1-progress);
                    Check(Mathf.Abs(measured-expected)<2f,"Nonlinear contraction at "+elapsed+": "+measured+" expected "+expected);
                    report.Add(elapsed+" ticks: radius="+measured.ToString("F3")+", linear="+expected.ToString("F3"));
                }
                File.WriteAllLines(Path.Combine(Folder,"contraction-radius.txt"),report);
            }
            finally {UnityEngine.Object.Destroy(image); UnityEngine.Object.Destroy(source);}
        }
        private static void TickTo(SingularitySession effect, int age) { while (effect.Age < age) effect.MapComponentTick(); }
        private static string Errors() => string.Join("\n", Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text));
        private static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        private void Finish(string result) { done = true; File.WriteAllText(Path.Combine(Folder, "complete.txt"), result); Application.Quit(); }
    }
}
