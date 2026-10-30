using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests {
 public sealed class ProtossMarkerAudit : GameComponent {
  int frames,exit;bool done;
  string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"ProtossMarkerAudit");
  public ProtossMarkerAudit(Game game){}
  void Check(bool ok,string why){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+why+"\n");if(!ok)throw new Exception(why);}
  public override void GameComponentUpdate(){
   if(!GenCommandLine.CommandLineArgPassed("pmp-protoss-marker-audit"))return;
   if(done){if(++exit>40)Application.Quit();return;}
   if(Find.CurrentMap==null||++frames<90)return;
   Directory.CreateDirectory(Folder);
   try{
    Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
    var marker=GenTypes.GetTypeInAnyAssembly("ProtossMech.CompProtossBuilding");
    Check(marker!=null,"shared Protoss marker exists in the base mod");
    var consumers=DefDatabase<ThingDef>.AllDefsListForReading.Where(d=>d.category==ThingCategory.Building&&
     (d.modContentPack?.PackageIdPlayerFacing?.ToLowerInvariant().Contains("protossmechcore")==true||d.modContentPack?.PackageIdPlayerFacing?.ToLowerInvariant().Contains("pmpersona")==true)&&d.GetCompProperties<CompProperties_Power>()?.PowerConsumption>0).ToList();
    File.WriteAllLines(Path.Combine(Folder,"coverage.txt"),consumers.Select(d=>d.defName+" marker="+d.HasComp(marker)));
    Check(consumers.Count>0&&consumers.All(d=>d.HasComp(marker)),"all loaded Protoss powered buildings carry the marker");
    var map=Find.CurrentMap;var center=map.Center;
    Thing Spawn(string name,IntVec3 pos){foreach(var t in pos.GetThingList(map).Where(t=>t is not Pawn).ToList())t.Destroy();var x=ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed(name));x.SetFaction(Faction.OfPlayer);return GenSpawn.Spawn(x,pos,map);}
    var gen=Spawn("VanometricPowerCell",center+new IntVec3(0,0,5)).TryGetComp<CompPowerTrader>();
    var cannon=Spawn("Photon_Cannon",center).TryGetComp<CompPowerTrader>();
    var lamp=Spawn("StandingLamp",center+new IntVec3(3,0,0)).TryGetComp<CompPowerTrader>();
    gen.connectChildren=new List<CompPower>{cannon,lamp};cannon.connectParent=gen;lamp.connectParent=gen;
    var net=new PowerNet(new[]{gen}){powerNetManager=map.powerNetManager};gen.PowerOutput=10000;gen.PowerOn=true;cannon.PowerOutput=-150;cannon.PowerOn=true;lamp.PowerOutput=-30;lamp.PowerOn=true;
    var flare=GameConditionMaker.MakeCondition(DefDatabase<GameConditionDef>.GetNamed("SolarFlare"),60000);map.gameConditionManager.RegisterCondition(flare);
    Find.TickManager.DebugSetTicksGame((Find.TickManager.TicksGame/20+1)*20);
    for(int i=0;i<100;i++)net.PowerNetTick();
    Check(cannon.PowerOn,"marked building remains powered during a real solar flare");
    Check(!lamp.PowerOn,"ordinary unmarked lamp still loses power");
    gen.PowerOutput=0;cannon.PowerOn=false;net.PowerNetTick();
    Check(!cannon.PowerOn,"flare exemption does not create power after supply loss");
    var battery=Spawn("PM_SolariteBattery",center+new IntVec3(-5,0,0));battery.TryGetComp<CompPowerTrader>().PowerOn=true;
    var storage=((ThingWithComps)battery).AllComps.First(c=>c.GetType().Name=="CompSolariteBatteryStorage");
    Check((bool)AccessTools.Method(storage.GetType(),"CanInteractWithGrid").Invoke(storage,null),"marked solarite battery can operate during flare");
    flare.End();
    var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);Check(errors.Length==0,"no native errors");
    File.WriteAllText(Path.Combine(Folder,"PASS.txt"),"Protoss building marker and solar flare checks passed");done=true;
   }catch(Exception e){File.WriteAllText(Path.Combine(Folder,"ERROR.txt"),e.ToString());File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text));done=true;}
  }
 }
}
