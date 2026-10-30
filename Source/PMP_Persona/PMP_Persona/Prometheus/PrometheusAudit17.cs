using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using ProtossMech;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace PMP_Persona.Prometheus
{
    // Opt-in integration audit. Inert in ordinary games; exercises the real death/bill/save lifecycle.
    public sealed class PrometheusAudit17 : GameComponent
    {
        private int stage, frames, pawnId, weaponId, startTick;
        private bool loading, done;
        private IntVec3 center;
        private string Folder => Path.Combine(GenFilePaths.SaveDataFolderPath, "PrometheusAudit17");
        public PrometheusAudit17(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref stage, "prometheusAuditStage");
            Scribe_Values.Look(ref pawnId, "prometheusAuditPawn");
            Scribe_Values.Look(ref weaponId, "prometheusAuditWeapon");
            Scribe_Values.Look(ref center, "prometheusAuditCenter");
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        public override void GameComponentUpdate()
        {
            if (!GenCommandLine.CommandLineArgPassed("pmp17prometheusaudit") || done || loading || Find.CurrentMap == null || ++frames < 90) return;
            Map map = Find.CurrentMap;
            try
            {
                Directory.CreateDirectory(Folder);
                if (stage == 0)
                {
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    center = map.Center + new IntVec3(25, 0, 25);
                    foreach (var c in GenRadial.RadialCellsAround(center, 12, true).Where(c => c.InBounds(map)))
                    {
                        foreach (Thing t in c.GetThingList(map).ToList())
                            if (!(t is Pawn))
                            {
                                if (t.def.destroyable) t.Destroy();
                                else t.DeSpawn(); // Despawn indestructible fixture obstacles.
                            }
                        map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                        map.fogGrid.Unfog(c);
                    }
                    var pawn = (Pawn_Prometheus)PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_Prometheus"), Faction.OfPlayer);
                    GenSpawn.Spawn(pawn, center, map);
                    pawn.Name = new NameSingle("Fireseed identity test");
                    pawnId = pawn.thingIDNumber; weaponId = pawn.equipment.Primary.thingIDNumber;
                    Check(pawn.skills == null && pawn.RaceProps.mechFixedSkillLevel == 10, "Mechanical skill policy");
                    Check(!pawn.def.inspectorTabsResolved.Any(t => t is ITab_Pawn_Character), "Human character tab remains");
                    Check(DefDatabase<ThingDef>.GetNamedSilentFail("PMP_" + "Firestealer") == null, "Retired Def remains loaded");
                    Check(Math.Abs(pawn.GetStatValue(StatDefOf.MoveSpeed) - 7) < .01f, "Move speed");
                    Check(Math.Abs(pawn.GetStatValue(StatDefOf.WorkSpeedGlobal) - 1) < .01f, "Work speed");
                    AuditMechanicalWork(pawn, map);
                    Check(pawn.GetComp<ProtossMechShield>().Energy == 125, "Initial shield");
                    Check(pawn.health.capacities.CapableOf(PawnCapacityDefOf.Talking) && SocialInteractionUtility.CanInitiateInteraction(pawn), "Social work cannot initiate speech");
                    Check(!pawn.AllComps.Any(c => c.GetType().Name.Contains("PurifierReconstruct")), "Reconstruct comp inherited");
                    Check(ProtossMechWorkUtility.WorkTypesFor(pawn).Count == DefDatabase<WorkTypeDef>.AllDefs.Count(w => w.VisibleCurrently), "PMC work panel missing types");
                    Check(DefDatabase<WorkTypeDef>.AllDefs.Where(w => pawn.workSettings.GetPriority(w) > 0).All(w => new[] { "Hauling", "Cleaning", "Firefighter" }.Contains(w.defName)), "Unexpected initial work");
                    pawn.workSettings.SetPriority(WorkTypeDefOf.Research, 2);
                    pawn.workSettings.SetPriority(WorkTypeDefOf.Crafting, 4);
                    pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart).Severity = 2;
                    var phase = pawn.abilities.GetAbility(DefDatabase<AbilityDef>.GetNamed("PMP_PrometheusPhase"));
                    var effect = phase.CompOfType<CompAbilityEffect_PrometheusPhase>();
                    Check(effect.Valid(center + IntVec3.East * 4), "Phase target rejected");
                    effect.Apply(center + IntVec3.East * 4, center + IntVec3.East * 4);
                    Check(pawn.Position == center + IntVec3.East * 4, "Phase failed");
                    var prisoner = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
                    GenSpawn.Spawn(prisoner, pawn.Position + IntVec3.North, map);
                    prisoner.guest.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
                    prisoner.guest.resistance = 50;
                    Check(pawn.interactions.TryInteractWith(prisoner, InteractionDefOf.RecruitAttempt), "Warden interaction failed");
                    prisoner.Name = new NameSingle("Recomposer target");
                    prisoner.skills.GetSkill(SkillDefOf.Crafting).Level = 19;
                    AuditHumanAndTeaching(pawn, prisoner);
                    RecomposerSupport.RecomposerPawnUtility.ConvertTargets(map, prisoner.Position);
                    var converted = map.mapPawns.AllPawnsSpawned.OfType<Pawn_Prometheus>().Single(p => p.Name?.ToStringShort == "Recomposer target");
                    Check(prisoner.Discarded && converted.skills == null && converted.RaceProps.mechFixedSkillLevel == 10
                        && !converted.health.hediffSet.HasHediff(DefDatabase<HediffDef>.GetNamed("Hediff_Reconstruct")), "Recomposer result or skill/reconstruct policy wrong");
                    pawn.Kill(null);
                    var seed = Seeds(map).Single(s => s.HeldPawn.thingIDNumber == pawnId);
                    Check(pawn.Dead && ReferenceEquals(seed.HeldPawn, pawn) && pawn.Suspended, "Original pawn not suspended in seed");
                    Check(!map.listerThings.AllThings.OfType<Corpse>().Any(c => c.InnerPawn == pawn), "Visible corpse remains");
                    Check(pawn.equipment.Primary == null && map.listerThings.AllThings.Any(t => t.thingIDNumber == weaponId), "Weapon did not drop");
                    seed.TakeDamage(new DamageInfo(DamageDefOf.Bomb, 99999));
                    Check(!seed.Destroyed && !seed.def.useHitPoints, "Fireseed damaged");
                    stage = 1; loading = true;
                    GameDataSaveLoader.SaveGame("PMPFireseedAudit");
                    LongEventHandler.ExecuteWhenFinished(() => GameDataSaveLoader.LoadGame("PMPFireseedAudit"));
                    return;
                }
                if (stage == 1)
                {
                    var seed = Seeds(map).Single(s => s.HeldPawn.thingIDNumber == pawnId);
                    Check(seed.HeldPawn.Dead && seed.HeldPawn.Suspended && seed.HeldPawn.Name.ToStringShort == "Fireseed identity test", "Identity/holder lost on load");
                    var cells = GenRadial.RadialCellsAround(center, 6, true).ToList();
                    foreach (var c in cells) map.terrainGrid.SetTerrain(c, DefDatabase<TerrainDef>.GetNamed("WaterDeep"));
                    Check(!seed.TryRestore(map, center) && seed.HeldPawn != null, "Blocked restore lost seed/pawn");
                    foreach (var c in cells) map.terrainGrid.SetTerrain(c, TerrainDefOf.Soil);
                    var forge = (Building_WorkTable)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("ProtossForge"));
                    forge.SetFaction(Faction.OfPlayer); GenSpawn.Spawn(forge, center + IntVec3.North * 4, map);
                    var fuel = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PM_Solarite")); fuel.stackCount = 7;
                    GenSpawn.Spawn(fuel, center + IntVec3.West * 2, map);
                    var worker = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("PMP_Prometheus"), Faction.OfPlayer);
                    GenSpawn.Spawn(worker, center + IntVec3.South * 2, map);
                    worker.workSettings.SetPriority(WorkTypeDefOf.Smithing, 1);
                    var recipe = DefDatabase<RecipeDef>.GetNamed("PMP_RestorePrometheus");
                    Check(forge.def.AllRecipes.Contains(recipe), "Forge recipe missing");
                    var bill = recipe.MakeNewBill(); forge.BillStack.AddBill(bill);
                    var giver = (WorkGiver_DoBill)DefDatabase<WorkGiverDef>.GetNamed("ProtossForgeBill").Worker;
                    var job = giver.JobOnThing(worker, forge, true);
                    Check(job != null, "No forge job: " + JobFailReason.Reason);
                    job.workGiverDef = giver.def;
                    worker.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                    startTick = Find.TickManager.TicksGame; stage = 2;
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Superfast;
                    return;
                }
                if (stage == 2)
                {
                    var pawn = map.mapPawns.AllPawnsSpawned.OfType<Pawn_Prometheus>().FirstOrDefault(p => p.thingIDNumber == pawnId);
                    if (pawn == null)
                    {
                        Check(Find.TickManager.TicksGame - startTick < 8000, "Timed out on forge job");
                        return;
                    }
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    Check(!pawn.Dead && pawn.Name.ToStringShort == "Fireseed identity test", "Restored a different individual");
                    Check(pawn.skills == null && !pawn.def.inspectorTabsResolved.Any(t => t is ITab_Pawn_Character), "Revival restored human skill/UI data");
                    Check(pawn.equipment.Primary == null, "Revival regenerated weapon");
                    Check(pawn.GetComp<ProtossMechShield>().Energy == 125, "Shield not full after revival");
                    Check(!pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(), "Revival left injuries");
                    Check(pawn.workSettings.GetPriority(WorkTypeDefOf.Research) == 2 && pawn.workSettings.GetPriority(WorkTypeDefOf.Crafting) == 4, "Work priorities reset");
                    Check(Seeds(map).All(s => s.HeldPawn?.thingIDNumber != pawnId), "Seed not consumed");
                    Check(map.listerThings.AllThings.Where(t => t.def.defName == "PM_Solarite").Sum(t => t.stackCount) == 2, "Did not consume exactly five of seven solarite");
                    pawn.Kill(null);
                    Check(Seeds(map).Count(s => s.HeldPawn?.thingIDNumber == pawnId) == 1, "Second death duplicated/lost fireseed");
                    var errors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToList();
                    Check(errors.Count == 0, string.Join("\n", errors));
                    File.WriteAllText(Path.Combine(Folder, "results.txt"), "PASS: pawn stats; work skill/defaults/PMC list; phase move; death and equipment drop; no visible corpse; fireseed damage immunity; identity and suspended holder save/load; blocked restore; real forge bill consumes one fireseed and exactly five solarite; original pawn restored with work settings and full shield; no weapon duplication; repeated death; zero Verse errors.");
                    Finish("PASS");
                }
            }
            catch (Exception e) { Directory.CreateDirectory(Folder); File.WriteAllText(Path.Combine(Folder, "results.txt"), e.ToString()); Finish("FAIL"); }
        }
        private static System.Collections.Generic.List<Thing_Fireseed> Seeds(Map map) => map.listerThings.AllThings.OfType<Thing_Fireseed>().ToList();
        private void AuditMechanicalWork(Pawn_Prometheus pawn, Map map)
        {
            Check(!pawn.GetInspectTabs().Any(t => t is ITab_Pawn_Character), "Character tab injected outside Def");
            var stats = new[] { "MiningSpeed", "PlantWorkSpeed", "ConstructionSpeed", "GeneralLaborSpeed",
                "ResearchSpeed", "MedicalTendSpeed", "AnimalGatherSpeed", "NegotiationAbility", "ShootingAccuracyPawn", "MeleeHitChance" };
            File.WriteAllLines(Path.Combine(Folder, "work-stats.txt"), stats.Select(n => n + "=" + pawn.GetStatValue(DefDatabase<StatDef>.GetNamed(n))));
            foreach (var name in stats.Take(7))
                Check(Math.Abs(pawn.GetStatValue(DefDatabase<StatDef>.GetNamed(name)) - 1) < .01f, "Unexpected work speed: " + name);
            foreach (var skill in DefDatabase<SkillDef>.AllDefsListForReading)
            {
                Check(new SkillRequirement { skill = skill, minLevel = 10 }.PawnSatisfies(pawn), "Level 10 gate rejected: " + skill);
                Check(!new SkillRequirement { skill = skill, minLevel = 11 }.PawnSatisfies(pawn), "Level 11 gate accepted: " + skill);
            }
            var learn = Toils_Misc.Learn(SkillDefOf.Animals, 100);
            learn.actor = pawn; learn.initAction();
            Check(pawn.skills == null, "Learning toil created a skill tracker");
            Rand.PushState(105);
            var expected = QualityUtility.GenerateQualityCreatedByPawn(10, false);
            Rand.PopState(); Rand.PushState(105);
            var actual = QualityUtility.GenerateQualityCreatedByPawn(pawn, SkillDefOf.Crafting, false);
            Rand.PopState(); Check(actual == expected, "Quality does not use mechanical fixed level");
            var cow = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("Cow"), Faction.OfPlayer);
            GenSpawn.Spawn(cow, pawn.Position + IntVec3.West * 3, map);
            Check(WorkGiver_InteractAnimal.CanInteractWithAnimal(pawn, cow, out var reason, true,
                canInteractWhileSleeping: true, canInteractWhileRoaming: true), "Animal work rejected: " + reason);
            RecomposerSupport.RecomposerVanillaBypass.PermanentlyRemove(cow);
            File.WriteAllLines(Path.Combine(Folder, "work-adapters.txt"), PrometheusWorkCompatibility.PatchedMethods);
        }
        private static void AuditHumanAndTeaching(Pawn_Prometheus teacher, Pawn human)
        {
            Check(human.GetInspectTabs().Any(t => t is ITab_Pawn_Character), "Human character tab was removed");
            Check(new SkillRequirement { skill = SkillDefOf.Crafting, minLevel = 19 }.PawnSatisfies(human), "Human skill gate regressed");
            // Select a trainable human skill for the learning fixture.
            var record = human.skills.skills.First(s => !s.TotallyDisabled && s.Level < 19);
            // XpTotalEarned counts completed levels only in 1.7.
            float before = record.XpTotalEarned + record.xpSinceLastLevel;
            var learn = Toils_Misc.Learn(record.def, 1000);
            learn.actor = human; learn.initAction();
            Check(record.XpTotalEarned + record.xpSinceLastLevel > before, "Human learning was suppressed");
            var driver = new JobDriver_Lessongiving { pawn = teacher };
            var chosen = (SkillDef)AccessTools.Method(typeof(JobDriver_Lessongiving), "ChooseSkill")
                .Invoke(driver, new object[] { human });
            Check(chosen?.lessonInteraction != null && !human.skills.GetSkill(chosen).TotallyDisabled,
                "Mechanical teacher could not select an eligible lesson");
            Check(teacher.skills == null, "Teacher manufactured a skill tracker");
        }
        private void Finish(string result) { done = true; File.WriteAllText(Path.Combine(Folder, "complete.txt"), result); Application.Quit(); }
    }
}
