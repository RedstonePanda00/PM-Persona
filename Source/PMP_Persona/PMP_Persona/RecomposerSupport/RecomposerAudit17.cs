using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using ProtossMech;
using PMP_Persona.FlattenSupport;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona.RecomposerSupport
{
    // Explicit native-game regression fixture, never runs in an ordinary game.
    public sealed class RecomposerAudit17 : GameComponent
    {
        private int stage, frames, shot = -1, firstId, secondId, playerId, mechId, outsideId, weaponId, ageOnSave;
        private long frozenAge;
        private int parkaId, helmetId, vestId, wearJobTick;
        private Apparel manualApparel;
        private bool loading, done;
        private IntVec3 center;
        private Thing_RecomposerMarker marker;
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "RecomposerAudit17");
        public RecomposerAudit17(Game game) { }
        public override void LoadedGame()
        { if (GenCommandLine.CommandLineArgPassed("pmp17recomposeraudit")) Find.TickManager.CurTimeSpeed = TimeSpeed.Paused; }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage,"recomposerAuditStage"); Scribe_Values.Look(ref center,"recomposerAuditCenter");
            Scribe_Values.Look(ref firstId,"recomposerAuditFirst"); Scribe_Values.Look(ref secondId,"recomposerAuditSecond");
            Scribe_Values.Look(ref playerId,"recomposerAuditPlayer"); Scribe_Values.Look(ref mechId,"recomposerAuditMech");
            Scribe_Values.Look(ref outsideId,"recomposerAuditOutside"); Scribe_Values.Look(ref weaponId,"recomposerAuditWeapon");
            Scribe_Values.Look(ref frozenAge,"recomposerAuditFrozenAge"); Scribe_Values.Look(ref ageOnSave,"recomposerAuditAge");
            Scribe_References.Look(ref marker,"recomposerAuditMarker");
            Scribe_Values.Look(ref parkaId,"recomposerParka");Scribe_Values.Look(ref helmetId,"recomposerHelmet");Scribe_Values.Look(ref vestId,"recomposerVest");
        }
        private static void Check(bool good, string message) { if (!good) throw new Exception(message); }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17recomposeraudit") || done || loading || Find.CurrentMap == null || ++frames < 90) return;
            try
            {
                Directory.CreateDirectory(Folder);
                var map = Find.CurrentMap;
                if (stage == 0)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    center = map.Center;
                    var rect = CellRect.CenteredOn(center, 23).ClipInsideMap(map); GenDebug.ClearArea(rect,map);
                    foreach (var cell in rect) { map.fogGrid.Unfog(cell); map.terrainGrid.SetTerrain(cell,TerrainDefOf.Soil); }
                    var first = Spawn(PawnKindDefOf.Colonist,Faction.OfAncientsHostile,new IntVec3(-3,0,0)); firstId=first.thingIDNumber;
                    first.Name=new NameSingle("Recomposer first"); first.Rotation=Rot4.South;
                    first.apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_Parka"),ThingDefOf.Cloth));
                    parkaId=first.apparel.WornApparel.Single(a=>a.def.defName=="Apparel_Parka").thingIDNumber;
                    var helmet=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_SimpleHelmet"),ThingDefOf.Steel);
                    first.apparel.Wear(helmet);helmetId=helmet.thingIDNumber;
                    var vest=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_FlakVest"));
                    first.apparel.Wear(vest);vestId=vest.thingIDNumber;
                    first.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Gun_Autopistol")));
                    weaponId=first.equipment.Primary.thingIDNumber;
                    first.equipment.Primary.TryGetComp<CompBiocodable>()?.CodeFor(first);
                    first.inventory.innerContainer.TryAdd(ThingMaker.MakeThing(ThingDefOf.Steel));
                    var second=Spawn(PawnKindDefOf.Colonist,Faction.OfAncientsHostile,new IntVec3(3,0,0));secondId=second.thingIDNumber;
                    second.Name=new NameSingle("Recomposer second");second.Rotation=Rot4.East;
                    first.relations.AddDirectRelation(PawnRelationDefOf.Spouse,second);
                    playerId=Spawn(PawnKindDefOf.Colonist,Faction.OfPlayer,new IntVec3(0,0,-6)).thingIDNumber;
                    mechId=Spawn(DefDatabase<PawnKindDef>.GetNamed("Mech_Scyther"),Faction.OfMechanoids,new IntVec3(6,0,4)).thingIDNumber;
                    outsideId=Spawn(PawnKindDefOf.Colonist,Faction.OfAncientsHostile,new IntVec3(22,0,0)).thingIDNumber;
                    var doomed=Spawn(PawnKindDefOf.Colonist,Faction.OfAncientsHostile,new IntVec3(0,0,5));
                    // Cancellation must restore the exact original and discard only its staged replacement.
                    var cancel=MakeMarker(map);
                    AccessTools.Field(typeof(Thing_RecomposerMarker),"age").SetValue(cancel,cancel.LockTick-1);
                    AccessTools.Method(typeof(Thing_RecomposerMarker),"Tick").Invoke(cancel,null);
                    Check(!doomed.Spawned && doomed.Suspended,"Cancellation fixture did not lock");
                    var staged=cancel.Subjects.Select(s=>s.replacement).ToList();
                    AccessTools.Field(typeof(Thing_RecomposerMarker),"age").SetValue(cancel,cancel.CoreTick-1);
                    AccessTools.Method(typeof(Thing_RecomposerMarker),"Tick").Invoke(cancel,null);
                    Check(first.equipment.Primary==null && staged.Any(p=>p.equipment.Primary?.thingIDNumber==weaponId),"Gear transfer not aligned to disassembly");
                    Check(staged.Single(p=>p.equipment.Primary?.thingIDNumber==weaponId).apparel.WornApparel.Any(a=>a.thingIDNumber==parkaId),"Apparel not transferred");
                    Check(!map.listerThings.AllThings.Any(t=>t.thingIDNumber==parkaId || t.thingIDNumber==helmetId || t.thingIDNumber==vestId),"Apparel dropped during transformation");
                    cancel.Destroy();
                    Check(first.equipment.Primary?.thingIDNumber==weaponId && first.apparel.WornApparel.Any(a=>a.def.defName=="Apparel_Parka"),"Cancellation lost staged equipment/apparel");
                    Check(doomed.Spawned && first.Spawned && second.Spawned && staged.All(p=>p.Discarded),"Cancellation lost original or leaked replacement");
                    RecomposerVanillaBypass.PermanentlyRemove(doomed);
                    CheckShader(first);
                    marker=MakeMarker(map);
                    Find.CameraDriver.SetRootPosAndSize(center.ToVector3Shifted(),14);
                    stage=1;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;return;
                }
                if(stage==1)
                {
                    int next=marker.Age/24;
                    if(next!=shot){shot=next;ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"Live-"+shot.ToString("00")+".png"));}
                    if(marker.Age<marker.CoreTick+15)return;
                    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                    Check(marker.Locked && marker.Subjects.Any(s=>s.original.thingIDNumber==firstId),"Scan did not lock target");
                    Check(marker.Subjects.All(s=>!s.original.Spawned && s.original.Suspended && !s.replacement.Spawned && s.replacement.Suspended),"A locked pawn is still acting");
                    Check(marker.Subjects.All(s=>s.original.thingIDNumber!=playerId && s.original.thingIDNumber!=mechId && s.original.thingIDNumber!=outsideId),"Target filter changed");
                    Check(RecomposerVisual.ActiveCount>=marker.Subjects.Count*2,"Both RenderTrees not captured");
                    frozenAge=marker.Subjects.Single(s=>s.original.thingIDNumber==firstId).original.ageTracker.AgeBiologicalTicks;
                    ageOnSave=marker.Age;stage=2;loading=true;
                    GameDataSaveLoader.SaveGame("PMPRecomposerTransition");
                    LongEventHandler.ExecuteWhenFinished(()=>GameDataSaveLoader.LoadGame("PMPRecomposerTransition"));return;
                }
                if(stage==2)
                {
                    Check(marker.Age==ageOnSave && marker.Locked,"Timeline reset on reload: " + marker.Age + " vs " + ageOnSave);
                    Check(marker.Subjects.All(s=>s.original.Suspended && s.replacement.Suspended),"Holder lost on reload");
                    var first=marker.Subjects.Single(s=>s.original.thingIDNumber==firstId).original;
                    Check(first.ageTracker.AgeBiologicalTicks==frozenAge && marker.Subjects.Single(s=>s.original==first).replacement.equipment.Primary.thingIDNumber==weaponId,"Original/gear lost on reload");
                    stage=3;Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;return;
                }
                if(stage==3)
                {
                    int next=marker.Age/24;
                    if(next!=shot){shot=next;ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"Restored-"+shot.ToString("00")+".png"));}
                    if(!marker.Destroyed)
                    {
                        Check(marker.Subjects.Single(s=>s.original.thingIDNumber==firstId).original.ageTracker.AgeBiologicalTicks==frozenAge,"Locked target ticked");
                        return;
                    }
                    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                    var first=map.mapPawns.AllPawnsSpawned.Single(p=>p.Name?.ToStringShort=="Recomposer first");
                    var second=map.mapPawns.AllPawnsSpawned.Single(p=>p.Name?.ToStringShort=="Recomposer second");
                    Check(first.def.defName=="PMP_Prometheus" && second.def==first.def,"Wrong conversion result");
                    Check(first.relations.DirectRelationExists(PawnRelationDefOf.Spouse,second),"Cross-target relationship lost");
                    Check(first.equipment.Primary.thingIDNumber==weaponId && first.inventory.innerContainer.Any(t=>t.def==ThingDefOf.Steel),"Equipment or inventory lost");
                    Check(first.apparel.WornApparel.Any(a=>a.thingIDNumber==parkaId)
                        && first.apparel.WornApparel.Any(a=>a.thingIDNumber==helmetId)
                        && first.apparel.WornApparel.Any(a=>a.thingIDNumber==vestId),"Original worn apparel not preserved");
                    Check(!map.listerThings.AllThings.Any(t=>t.thingIDNumber==parkaId||t.thingIDNumber==helmetId||t.thingIDNumber==vestId),"Converted apparel dropped or duplicated");
                    Check(first.equipment.Primary.TryGetComp<CompBiocodable>()?.CodedPawn==first,"Biocode not transferred to reconstructed identity");
                    Check(first.apparel.WornApparel.Single(a=>a.thingIDNumber==vestId).def.apparel.CoversBodyPart(first.RaceProps.body.corePart),"Armor does not cover mechanical torso");
                    Check(first.apparel.WornApparel.Single(a=>a.thingIDNumber==helmetId).def.apparel.CoversBodyPart(
                        first.RaceProps.body.AllParts.Single(b=>b.def.defName=="MechanicalHead")),"Helmet does not cover mechanical head");
                    CheckApparelRender(first);
                    Check(marker.Subjects.All(s=>s.original.Discarded && s.completed),"Old pawn or session leaked");
                    Check(marker.GetDirectlyHeldThings().Count==0 && RecomposerVisual.ActiveCount==0,"Container/GPU resources leaked");
                    stage=4;frames=0;return;
                }
                if(stage==4)
                {
                    var first=map.mapPawns.AllPawnsSpawned.Single(p=>p.Name?.ToStringShort=="Recomposer first");
                    manualApparel=(Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("Apparel_CowboyHat"),ThingDefOf.Cloth);
                    GenSpawn.Spawn(manualApparel,first.Position+IntVec3.South,map);
                    Check(first.TryGetComp<RsPandaLibrary.CompEquippableMechanoid>().AllowsApparelCategory(manualApparel.def),"Panda clothing admission disabled");
                    first.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Wear,manualApparel),JobTag.Misc);
                    wearJobTick=Find.TickManager.TicksGame;stage=5;Find.TickManager.CurTimeSpeed=TimeSpeed.Superfast;return;
                }
                if(stage==5)
                {
                    var first=map.mapPawns.AllPawnsSpawned.Single(p=>p.Name?.ToStringShort=="Recomposer first");
                    if(manualApparel.Wearer!=first)
                    {Check(Find.TickManager.TicksGame-wearJobTick<3000,"Native wear job failed: "+first.CurJob);return;}
                    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                    Check(first.apparel.WornApparel.Any(a=>a.thingIDNumber==parkaId),"Changing hat removed unrelated gear");
                    Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),string.Join("\n",Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text)));
                    File.WriteAllText(Path.Combine(Folder,"results.txt"),"PASS: six-stage native GLSL rendering; original and replacement RenderTree capture; scan target filter; suspended original/replacement; mid-transition save/load; preserved clock/progress/identity/equipment; exact staged cancellation rollback; final conversion/relationships/inventory; preserved worn apparel IDs; biocode ownership; armor coverage; clothed RenderTree three facings; native player-ordered Wear job; holder and GPU cleanup; zero Verse errors.");
                    Finish("PASS");
                }
            }
            catch(Exception e){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"results.txt"),e.ToString());Finish("FAIL");}
        }
        private Pawn Spawn(PawnKindDef kind,Faction faction,IntVec3 offset)
        {var p=PawnGenerator.GeneratePawn(kind,faction);GenSpawn.Spawn(p,center+offset,Find.CurrentMap);return p;}
        private Thing_RecomposerMarker MakeMarker(Map map)
        {var m=(Thing_RecomposerMarker)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(RecomposerConstants.MarkerDefName));m.Configure(center,map);GenSpawn.Spawn(m,center,map);return m;}
        private void CheckShader(Pawn original)
        {
            var replacement=RecomposerPawnUtility.PrepareReplacement(original);
            using(var before=FlattenVisual.CapturePawn(original,RecomposerVisual.Parameters(original,Rot4.South,PawnPosture.Standing),RecomposerVisual.MaterialName))
            using(var after=FlattenVisual.CapturePawn(replacement,RecomposerVisual.Parameters(replacement,Rot4.South,PawnPosture.Standing),RecomposerVisual.MaterialName))
            {
                var camera=TargetOutlineCaptureCamera.Create();var rt=RenderTexture.GetTemporary(512,512,24,RenderTextureFormat.ARGB32);
                var image=new Texture2D(512,512,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
                try
                {
                    int[] phases={1,1,1,1,2,3,3,3,4,4};float[] progress={0,.3f,.65f,1,1,0,.4f,1,.5f,1};
                    Color32[] corePixels=null;int intact=0;
                    for(int i=0;i<phases.Length;i++)
                    {
                        var capture=phases[i]>=3?after:before;
                        capture.Material.SetVector("_Geometry",new Vector4(capture.HalfSize,0,0,0));
                        RecomposerVisual.Configure(capture.Material,phases[i],progress[i],original.thingIDNumber,0);
                        camera.RenderTo(rt,2,()=>{capture.Material.SetPass(0);Graphics.DrawMeshNow(MeshPool.plane10,Matrix4x4.Scale(new Vector3(capture.HalfSize*4,1,capture.HalfSize*4)));});
                        RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();
                        var pixels=image.GetPixels32();int visible=pixels.Count(c=>c.a>15);
                        Check(visible>30,"Invisible shader phase "+phases[i]+"/"+progress[i]);
                        if(i==0)intact=visible;
                        if(i==3){Check(visible<intact,"Body did not collapse into core");corePixels=pixels;}
                        if(i==4||i==5)Check(pixels.Where((p,k)=>Math.Abs(p.a-corePixels[k].a)>8).Count()<60,"Core jumps across stage boundary");
                        Check(pixels.Take(512).All(c=>c.a==0),"Opaque background leaked outside effect");
                        File.WriteAllBytes(Path.Combine(Folder,"Shader-"+i.ToString("00")+".png"),image.EncodeToPNG());
                    }
                }
                finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.Destroy(image);UnityEngine.Object.Destroy(camera.gameObject);}
            }
            RecomposerVanillaBypass.ForceDiscard(replacement);
        }
        private void CheckApparelRender(Pawn pawn)
        {
            var orientations=new[]{Rot4.South,Rot4.East,Rot4.North};
            foreach(var facing in orientations)
            {
                var parms=RecomposerVisual.Parameters(pawn,facing,PawnPosture.Standing);
                using(var clothed=FlattenVisual.CapturePawn(pawn,parms,RecomposerVisual.MaterialName))
                {
                    parms.flags &= ~(PawnRenderFlags.Clothes | PawnRenderFlags.Headgear);
                    using(var bare=FlattenVisual.CapturePawn(pawn,parms,RecomposerVisual.MaterialName))
                    {
                        var image=new Texture2D(256,256,TextureFormat.RGBA32,false);var previous=RenderTexture.active;
                        try
                        {
                            RenderTexture.active=clothed.Texture;image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                            var dressed=image.GetPixels32();File.WriteAllBytes(Path.Combine(Folder,"Apparel-"+facing.AsInt+".png"),image.EncodeToPNG());
                            RenderTexture.active=bare.Texture;image.ReadPixels(new Rect(0,0,256,256),0,0);image.Apply();
                            var naked=image.GetPixels32();
                            Check(dressed.Where((c,k)=>Math.Abs(c.r-naked[k].r)+Math.Abs(c.g-naked[k].g)+Math.Abs(c.b-naked[k].b)>50).Count()>200,"Apparel not rendered: "+facing);
                        }
                        finally{RenderTexture.active=previous;UnityEngine.Object.Destroy(image);}
                    }
                }
            }
        }
        private void Finish(string result)
        {done=true;File.WriteAllText(Path.Combine(Folder,"complete.txt"),result);Application.Quit();}
    }
}
