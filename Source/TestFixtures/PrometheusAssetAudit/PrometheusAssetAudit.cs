using System;
using System.IO;
using System.Linq;
using GunSmithFramework;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests
{
    // Loaded only for the isolated quicktest; this assembly is never shipped.
    public sealed class PrometheusAssetAudit : GameComponent
    {
        int frames;
        bool finished;
        string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath,"AssetAudit");
        public PrometheusAssetAudit(Game game) { }
        void Check(bool condition,string message)
        {
            if (!condition) throw new Exception(message);
            File.AppendAllText(Path.Combine(Folder,"results.txt"),"PASS " + message + "\n");
        }
        Texture2D Bake(CompModularGun comp, string name)
        {
            var texture = GunSmithTextureBaker.BakeComposite(comp.Platform,comp.GetPart,comp.parent.def.graphicData.drawSize,out var size);
            Check(texture != null && size.x > 0 && size.y > 0,"GPU composite " + name);
            File.WriteAllBytes(Path.Combine(Folder,name + ".png"),texture.EncodeToPNG());
            return texture;
        }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp-prometheus-asset-audit") || finished || Find.CurrentMap == null || ++frames < 90) return;
            finished = true;
            Directory.CreateDirectory(Folder);
            try
            {
                var def = DefDatabase<ThingDef>.GetNamed("PMP_HardlightRifle");
                var gun = (ThingWithComps)ThingMaker.MakeThing(def);
                Check(!def.ConfigErrors().Any() && def.relicChance==0f,"rifle configuration valid and excluded from relic selection");
                foreach(var name in new[]{"PMP_HardlightRifle","PM_GunC7Templar","PM_GunC19Dragoon","PM_GunC31Tempest","PM_GunB9Zealot","PM_GunB12Vanguard","PM_GunB25Bulwark","PM_GunR42DeadEye"})
                {
                    var weapon=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name));
                    Check(weapon.TryGetComp<CompQuality>()==null,name+" has no quality component after all Def patches");
                }
                var comp = gun.TryGetComp<CompModularGun>();
                // Native AdjustedRange requires a wielder; an unheld item's Verb has no caster.
                var wielder=PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist,Faction.OfPlayer);
                wielder.equipment.DestroyAllEquipment();
                GenSpawn.Spawn(wielder,Find.CurrentMap.Center,Find.CurrentMap);
                wielder.equipment.AddEquipment(gun);
                Check(gun is Thing_ModularGun && comp != null,"native modular gun creation");
                Check(GunSmithPartQuery.GetVisibleSlotIds(comp).SequenceEqual(new[]{"Optic","UnderBarrel"}),"only optic and underbarrel selectors visible");
                Check(comp.GetPart("UnderBarrelMount")==null,"empty underbarrel has no floating armor plate");
                Check(!comp.Platform.ConfigErrors().Any(),"platform configuration valid");
                var optic = comp.GetPart("Optic");
                Check(optic != null && optic.defName == "PMP_HardlightRifle_Optic" && !optic.ConfigErrors().Any(),"original optic fitted by default");
                Check(Math.Abs(gun.GetStatValue(StatDefOf.Mass,true,-1)-2.2f)<0.001f && Math.Abs(gun.GetStatValue(StatDefOf.AccuracyLong,true,-1)-0.95f)<0.001f && Math.Abs(gun.TryGetComp<CompEquippable>().PrimaryVerb.EffectiveRange-44f)<0.001f,"fitted standard sight: 2.2 kg, 95 percent long accuracy, effective range 44");
                var fitted = Bake(comp,"with-optic");
                comp.SetPartDefName("Optic",null);
                Check(comp.GetPart("Optic") == null,"optic remains removed after slot resolution");
                Check(Math.Abs(gun.GetStatValue(StatDefOf.Mass,true,-1)-2f)<0.001f && Math.Abs(gun.GetStatValue(StatDefOf.AccuracyLong,true,-1)-0.90f)<0.001f && Math.Abs(gun.TryGetComp<CompEquippable>().PrimaryVerb.EffectiveRange-40f)<0.001f,"removing sight restores 2 kg, 90 percent long accuracy, effective range 40");
                var bare = Bake(comp,"without-optic");
                var a = fitted.GetPixels(); var b = bare.GetPixels();
                Check(a.Length == b.Length && a.Where((p,i)=>Math.Abs(p.a-b[i].a)>0.1f).Count()>100,"removal changes rendered silhouette");
                comp.SetPartDefName("Optic",optic.defName);
                Check(comp.GetPart("Optic") == optic,"optic can be refitted");
                var common = DefDatabase<GunSmithPartDef>.AllDefsListForReading.Where(p=>p.defName.StartsWith("PM_Common_") && (p.slotType=="Optic" || p.slotType=="UnderBarrel")).ToList();
                Check(common.Count==7,"five common underbarrels and two common optics discovered");
                foreach(var part in common)
                {
                    Check(GunSmithPartQuery.GetPartsForPlatform(comp.Platform,part.slotType).Contains(part),"compatible " + part.defName);
                    comp.SetPartDefName(part.slotType,part.defName);
                    Check(comp.GetRawPart(part.slotType)==part,"install " + part.defName);
                    if(part.slotType=="UnderBarrel") Check(comp.GetPart("UnderBarrelMount")?.defName=="PMP_HardlightRifle_UnderbarrelMount","mount appears with " + part.defName);
                    Bake(comp,part.defName);
                    comp.SetPartDefName(part.slotType,null);
                    Check(comp.GetPart("UnderBarrelMount")==null,"mount absent without underbarrel after " + part.defName);
                    comp.SetPartDefName("Optic",optic.defName);
                }
                Check(!DefDatabase<GunSmithPartDef>.AllDefsListForReading.Any(p=>p.defName.StartsWith("PM_") && p.defName.Contains("APEX") && p.IsCompatibleWith(comp.Platform)),"other platforms' APEX kits remain excluded");
                var finalBare = Bake(comp,"after-underbarrel-removal").GetPixels();
                Check(a.Zip(finalBare,(x,y)=>Math.Abs(x.a-y.a)<0.001f && Math.Abs(x.r-y.r)<0.001f && Math.Abs(x.g-y.g)<0.001f && Math.Abs(x.b-y.b)<0.001f).All(equal=>equal),"removal restores original rifle image pixel-for-pixel");
                Check(def.Verbs[0].burstShotCount==3 && def.Verbs[0].range==40 && def.Verbs[0].defaultProjectile.defName=="PMP_HardlightBolt","40-tile range with existing three-round burst");
                var projectile=def.Verbs[0].defaultProjectile.projectile;
                Check(projectile.damageDef==DamageDefOf.Burn && projectile.damageDef.armorCategory.defName=="Heat" && projectile.GetArmorPenetration(gun)==2f,"thermal damage and 200 percent penetration");
                Check(Math.Abs(gun.GetStatValue(StatDefOf.AccuracyTouch)-0.85f)<0.001f && Math.Abs(gun.GetStatValue(StatDefOf.AccuracyShort)-0.95f)<0.001f && Math.Abs(gun.GetStatValue(StatDefOf.AccuracyMedium)-0.95f)<0.001f && Math.Abs(gun.GetStatValue(StatDefOf.AccuracyLong,true,-1)-0.95f)<0.001f,"standard sight accuracy 85/95/95/95 percent after refitting");
                var mech=PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Mech_Scyther"),Faction.OfMechanoids);
                var hit=mech.TakeDamage(new DamageInfo(projectile.damageDef,projectile.GetDamageAmount(gun),projectile.GetArmorPenetration(gun)));
                Check(hit.totalDamageDealt>0,"thermal projectile damage penetrates a vanilla mechanoid");
                var turret=DefDatabase<ThingDef>.GetNamed("PMP_RomulusSpearTurret");
                Check(turret.uiIconPath==turret.building.turretGunDef.graphicData.texPath && turret.uiIcon!=null,"Romulus turret icon reuses its gun head texture");
                foreach(var direction in new[]{"south","east","north"})
                {
                    var texture=ContentFinder<Texture2D>.Get("Things/Pawn/Mechanoid/Prometheus_"+direction,false);
                    Check(texture != null && texture.width==512 && texture.height==512,"pawn direction " + direction);
                }
                Check(optic.iconPath.NullOrEmpty() && optic.Icon!=null,"optic icon reuses its single cropped composite texture");
                File.WriteAllText(Path.Combine(Folder,"complete.txt"),"Native RimWorld 1.7 asset and gunsmith checks passed.");
            }
            catch(Exception ex) {File.WriteAllText(Path.Combine(Folder,"failure.txt"),ex.ToString());Log.Error("[PrometheusAssetAudit] "+ex);}
            Application.Quit();
        }
    }
}
