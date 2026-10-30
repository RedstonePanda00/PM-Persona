using System;
using System.IO;
using System.Linq;
using GunSmithFramework;
using ProtossMech.GunSmith;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 public sealed class HardlightArtAudit : GameComponent {
  int frames,exitFrames;bool done;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"HardlightArtAudit");
  public HardlightArtAudit(Game game){}
  void Check(bool ok,string text){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+text+"\n");if(!ok)throw new Exception(text);}
  void Bake(ThingWithComps gun,CompModularGun comp,string name){
   var tex=GunSmithTextureBaker.BakeComposite(comp.Platform,comp.GetPart,gun.def.graphicData.drawSize,out var size);
   Check(tex!=null&&tex.width==1024&&tex.height==512&&size==new Vector2(gun.def.graphicData.drawSize.x*2,gun.def.graphicData.drawSize.y),"native 2:1 composite and unchanged world size "+name);
   File.WriteAllBytes(Path.Combine(Folder,name+".png"),tex.EncodeToPNG());
  }
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-hardlight-layout-audit"))return;
   if(done){if(++exitFrames>60)Application.Quit();return;}
   if(Find.CurrentMap==null||++frames<60)return;
   Directory.CreateDirectory(Folder);
   try {
    var gun=(ThingWithComps)ThingMaker.MakeThing(ThingDef.Named("PMP_HardlightRifle"));var comp=gun.GetComp<CompModularGun>();
    Check(!gun.def.ConfigErrors().Any()&&!comp.Platform.ConfigErrors().Any(),"native weapon and platform definitions valid");
    Check(comp.GetPart("Optic")?.defName=="PMP_HardlightRifle_Optic","existing default optic preserved");
    Check(comp.GetPart("UnderBarrelMount")==null,"no armor without underbarrel");
    Check(!DefDatabase<GunSmithPartDef>.AllDefs.Any(p=>p.platformOverrides?.Any(o=>o.platform==comp.Platform)==true),"zero Z250 platform overrides on any part");
    foreach(var path in new[]{"Hardlight_Body","Hardlight_UnderbarrelMount_Rifle"}){
     var tex=ContentFinder<Texture2D>.Get("Things/Weapon/HardlightRifle/"+path);Check(tex.width==1024&&tex.height==512,"rifle source resolution "+path);
    }
    var optics=GunSmithPartQuery.GetPartsForPlatform(comp.Platform,"Optic").ToArray();
    var underbarrels=GunSmithPartQuery.GetPartsForPlatform(comp.Platform,"UnderBarrel").ToArray();
    Check(optics.Length==3&&underbarrels.Length==5,"all three existing optics and five existing underbarrels available");
    comp.SetPartDefName("Optic",null);Bake(gun,comp,"bare");
    foreach(var part in optics.Concat(underbarrels)){
     string slot=optics.Contains(part)?"Optic":"UnderBarrel";
     Check(part.GetPreviewDrawSizeFor(comp.Platform)==part.previewDrawSize&&part.GetLocalAttachmentOffsetFor(comp.Platform)==Vector2.zero,"shared part uses its unchanged defaults "+part.defName);
     comp.SetPartDefName(slot,part.defName);
     if(slot=="UnderBarrel")Check(comp.GetPart("UnderBarrelMount")?.texPath=="Things/Weapon/HardlightRifle/Hardlight_UnderbarrelMount_Rifle","rifle armor appears with "+part.defName);
     Bake(gun,comp,part.defName);comp.SetPartDefName(slot,null);
     Check(comp.GetPart("UnderBarrelMount")==null,"optional removal clears armor "+part.defName);
    }
    foreach(var optic in optics){
     comp.SetPartDefName("Optic",optic.defName);
     foreach(var underbarrel in underbarrels){
      comp.SetPartDefName("UnderBarrel",underbarrel.defName);Bake(gun,comp,optic.defName+"__"+underbarrel.defName);
     }
    }
    comp.SetPartDefName("UnderBarrel",null);comp.SetPartDefName("Optic","PMP_HardlightRifle_Optic");
    Check(GunSmithCostUtility.BuildWarpCosts(comp.Platform,comp).Single().count==35&&gun.def.Verbs[0].range==40&&comp.GetPart("Optic").rangeOffset==4,"default sight cost and range configuration unchanged");
    foreach(string name in new[]{"PMP_Boltshot","PMP_Suppressor","PMP_Scattershot","PMP_BinaryRifle"}){
     var other=(ThingWithComps)ThingMaker.MakeThing(ThingDef.Named(name));var oc=other.GetComp<CompModularGun>();
     oc.SetPartDefName("Optic",name=="PMP_Suppressor"||name=="PMP_Scattershot"?"PM_Common_ViperSight":"PMP_HardlightRifle_Optic");
     Check(oc.GetPart("Optic").GetPreviewDrawSizeFor(oc.Platform)==1,"other weapon retains default shared optic size "+name);
     if(name=="PMP_Boltshot")Check(ContentFinder<Texture2D>.Get(oc.GetPart("OpticArmor").texPath).width==512,"Z110 retains its original 512px armor layer");
     other.Destroy(DestroyMode.Vanish);
    }
    Check(!Log.Messages.Any(m=>m.type==LogMessageType.Error),"no game errors through native asset loading and all attachment combinations");
    gun.Destroy(DestroyMode.Vanish);File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Hardlight native visual audit passed.");
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());}
   done=true;
  }
 }
}
