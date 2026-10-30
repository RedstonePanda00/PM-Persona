using System;
using System.Collections.Generic;
using System.Linq;
using ProtossMech;
using RimWorld;
using Verse;
using UnityEngine;

namespace PMP_Persona.Prometheus
{
    public sealed class Pawn_Prometheus : Pawn
    {
        private bool initialized;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref initialized, "prometheusInitialized");
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            interactions ??= new Pawn_InteractionsTracker(this);
            EnsureApparel();
            if (initialized) return;
            initialized = true;
            workSettings?.EnableAndInitialize();
            workSettings?.DisableAll();
            foreach (string name in new[] { "Hauling", "Cleaning", "Firefighter" })
                workSettings?.SetPriority(DefDatabase<WorkTypeDef>.GetNamed(name), name == "Firefighter" ? 1 : 3);
            // Disable generated weapons on repair and resurrection.
            if (equipment != null && equipment.Primary == null)
                equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_HardlightRifle")));
            GetComp<ProtossMechShield>()?.RestoreShield();
        }

        internal void EnsureApparel()
        {
            apparel ??= new Pawn_ApparelTracker(this);
            // Initialize body and head metadata for native apparel rendering.
            story ??= new Pawn_StoryTracker(this);
            story.bodyType ??= BodyTypeDefOf.Male;
            story.headType ??= DefDatabase<HeadTypeDef>.AllDefsListForReading
                .FirstOrDefault(h => h != HeadTypeDefOf.Skull && h != HeadTypeDefOf.Stump);
            story.skinColorOverride ??= Color.white;
        }

        public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
        {
            if (Dead || Destroyed) return;
            var priorities = new Dictionary<WorkTypeDef, int>();
            if (workSettings?.Initialized == true)
                foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
                    priorities[work] = workSettings.GetPriority(work);
            base.Kill(dinfo, exactCulprit);
            if (!Dead || Discarded || Corpse == null) return;
            // Wrap the corpse after vanilla death handling; retain it for resurrection.
            Thing_Fireseed.ReplaceRemains(Corpse, priorities);
        }
    }

    [StaticConstructorOnStartup]
    public static class PrometheusDefinitions
    {
        static PrometheusDefinitions()
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("PMP_Prometheus");
            var race = def?.race;
            if (race == null) return;
            race.mechEnabledWorkTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading.ToList();
            race.mechFixedSkillLevel = 10;
            // Copy inspect tabs once and exclude the human character card.
            def.inspectorTabsResolved = def.inspectorTabsResolved?
                .Where(tab => !(tab is ITab_Pawn_Character)).ToList();
        }
    }
}
