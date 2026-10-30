using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PMP_Persona.Romulus;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Tests
{
    public sealed class LegionUpgradeAudit : GameComponent
    {
        int frames; bool done;
        bool ApparelAudit => GenCommandLine.CommandLineArgPassed("pmp-legion-apparel-audit");
        string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath,ApparelAudit?"LegionApparelAudit":"LegionUpgradeAudit");
        public LegionUpgradeAudit(Game game) { }
        void Check(bool ok,string why) { if(!ok)throw new Exception(why);File.AppendAllText(Path.Combine(Folder,"checks.txt"),"PASS "+why+"\n"); }
        Thing Make(ThingDef def,QualityCategory? quality=null)
        {
            var item=ThingMaker.MakeThing(def,def.MadeFromStuff?ThingDefOf.Steel:null);
            if(quality.HasValue)item.TryGetComp<CompQuality>().SetQuality(quality.Value,ArtGenerationContext.Outsider);
            return item;
        }
        Thing Produce(RecipeDef recipe,Pawn worker,List<Thing> inputs,int seed)
        {
            Rand.PushState(seed);
            try { return GenRecipe.MakeRecipeProducts(recipe,worker,inputs,inputs[0],null).Single(); }
            finally { Rand.PopState(); }
        }
        public override void GameComponentUpdate()
        {
            if(done||!ApparelAudit&&!GenCommandLine.CommandLineArgPassed("pmp-legion-upgrade-audit")||Find.CurrentMap==null||++frames<100)return;
            done=true;Directory.CreateDirectory(Folder);
            try
            {
                var map=Find.CurrentMap;Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;
                var worker=map.mapPawns.FreeColonistsSpawned.First();worker.skills.GetSkill(SkillDefOf.Crafting).Level=0;
                var fragment=DefDatabase<ThingDef>.GetNamed("PMP_ArchotechArmorFragment");
                foreach(var suffix in ApparelAudit?new[]{"LegionShield","BelieverArmor","BelieverHelmet","CenturionArmor","CenturionHelmet","ChampionArmor","ChampionHelmet"}:new[]{"Spearpoint","Cerberus","Opis","Absolution","LegionSpear"})
                {
                    var recipe=DefDatabase<RecipeDef>.GetNamed("PMP_Upgrade"+suffix);
                    Check(!recipe.ConfigErrors().Any(),recipe.defName+" native recipe config");
                    Check(recipe.Worker is RecipeWorker_LegionWeaponUpgrade,recipe.defName+" custom quality worker resolves");
                    foreach(var table in ApparelAudit?new[]{"ElectricSmithy","FueledSmithy"}:new[]{"TableMachining"})
                        Check(DefDatabase<ThingDef>.GetNamed(table).AllRecipes.Contains(recipe),recipe.defName+" appears at "+table);
                    if(ApparelAudit)Check(!DefDatabase<ThingDef>.GetNamed("TableMachining").AllRecipes.Contains(recipe),recipe.defName+" absent from machining table");
                    if(!ApparelAudit||!suffix.EndsWith("Helmet"))Check(!recipe.AvailableNow,recipe.defName+" locked before legion research");
                    Find.ResearchManager.FinishProject(recipe.researchPrerequisite,false,null,false);
                    Check(recipe.AvailableNow,recipe.defName+" unlocked after research");
                    var prototype=recipe.GetModExtension<LegionUpgradeRecipe>().prototype;
                    int fragmentCount=ApparelAudit?(suffix.StartsWith("Champion")?4:suffix.StartsWith("Centurion")?2:1):1;
                    Check(recipe.IngredientValueGetter.ValuePerUnitOf(prototype)==1f&&recipe.IngredientValueGetter.ValuePerUnitOf(fragment)==1f&&recipe.ingredients[0].GetBaseCount()==1f&&recipe.ingredients[1].GetBaseCount()==fragmentCount,recipe.defName+" exact prototype and fragment counts");
                    var source=Make(prototype,QualityCategory.Legendary);var shard=Make(fragment);
                    shard.stackCount=fragmentCount;
                    var inputs=new List<Thing>{source,shard};var product=Produce(recipe,worker,inputs,417);
                    if(ApparelAudit)Check(product is Apparel&&product.def==recipe.products.Single().thingDef,recipe.defName+" produces exact apparel item");
                    Check(product.TryGetQuality(out var high)&&high==QualityCategory.Legendary,recipe.defName+" consumed Legendary prototype preserved");
                    foreach(var item in inputs)recipe.Worker.ConsumeIngredient(item,recipe,map);
                    Check(source.Destroyed&&shard.Destroyed,recipe.defName+" prototype and fragment consumed");product.Destroy();
                    var low=Make(prototype,QualityCategory.Awful);var shard2=Make(fragment);shard2.stackCount=fragmentCount;
                    var unrelated=Make(prototype,QualityCategory.Legendary);GenSpawn.Spawn(unrelated,CellFinder.RandomClosewalkCellNear(worker.Position,map,8),map);
                    var baseline=new RecipeDef{defName="PMP_UpgradeAuditControl",products=recipe.products,workSkill=recipe.workSkill,useIngredientsForColor=false};
                    var lowInputs=new List<Thing>{low,shard2};var normal=Produce(baseline,worker,lowInputs,921);
                    normal.TryGetQuality(out var expected);normal.Destroy();
                    var actual=Produce(recipe,worker,lowInputs,921);
                    Check(actual.TryGetQuality(out var rolled)&&rolled==expected,recipe.defName+" native craft roll retained; unconsumed Legendary stock ignored");
                    Check(unrelated.Spawned&&!unrelated.Destroyed,recipe.defName+" unrelated inventory untouched");
                    actual.Destroy();low.Destroy();shard2.Destroy();unrelated.Destroy();
                }
                if(!ApparelAudit){
                var faction=FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(DefDatabase<FactionDef>.GetNamed("PMP_RomulusLegion")));Find.FactionManager.Add(faction);
                foreach(var pair in new[]{("PMP_BelieverCerberus","PMP_Cerberus"),("PMP_BelieverOpis","PMP_Opis")})
                {
                    var kind=DefDatabase<PawnKindDef>.GetNamed(pair.Item1);Check(!kind.ConfigErrors().Any(),pair.Item1+" native pawn kind config");
                    var pawn=PawnGenerator.GeneratePawn(kind,faction);GenSpawn.Spawn(pawn,CellFinder.RandomClosewalkCellNear(worker.Position,map,15),map);pawn.jobs.StopAll();
                    Check(pawn.equipment.Primary?.def.defName==pair.Item2,pair.Item1+" generates exact weapon");
                    Check(pawn.apparel.WornApparel.Any(a=>a.def.defName=="PMP_BelieverArmor")&&pawn.apparel.WornApparel.Any(a=>a.def.defName=="PMP_BelieverHelmet"),pair.Item1+" generates believer armor and helmet");
                    Check(LegionDoctrine.Record(pawn)?.Advanced==true,pair.Item1+" legion equipment eligibility");
                    int before=map.listerThings.ThingsOfDef(fragment).Sum(t=>t.stackCount);pawn.Kill(null);
                    Check(pawn.Dead&&map.listerThings.ThingsOfDef(fragment).Sum(t=>t.stackCount)==before+1,pair.Item1+" death drops one fragment");
                }
                }
                var errors=Log.Messages.Where(m=>m.type==LogMessageType.Error).Select(m=>m.text).ToArray();
                File.WriteAllLines(Path.Combine(Folder,"native-errors.txt"),errors);Check(errors.Length==0,"no native errors");
                File.WriteAllText(Path.Combine(Folder,"PASS.txt"),ApparelAudit?"Apparel smithy registration, quality, research and consumption passed.":"Upgrade quality, research, consumption and variant generation passed.");
            }
            catch(Exception ex){File.WriteAllText(Path.Combine(Folder,"FAIL.txt"),ex.ToString());}
            Application.Quit();
        }
    }
}
