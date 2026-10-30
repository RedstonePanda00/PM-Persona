using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP_Persona.Romulus
{
    public sealed class Pawn_Legion : Pawn
    {
        private bool issuedEquipment;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref issuedEquipment, "legionEquipmentIssued"); }
        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            apparel ??= new Pawn_ApparelTracker(this);
            story ??= new Pawn_StoryTracker(this);
            story.bodyType ??= BodyTypeDefOf.Hulk;
            story.headType ??= DefDatabase<HeadTypeDef>.AllDefsListForReading.FirstOrDefault(h=>h!=HeadTypeDefOf.Skull&&h!=HeadTypeDefOf.Stump);
            if (!issuedEquipment)
            {
                issuedEquipment = true;
                if (def.defName == "PMP_LegionGuard")
                {
                    if (equipment.Primary == null) equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_LegionSpear")));
                    apparel.Wear((Apparel)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_LegionShield")));
                }
            }
            LegionDoctrine.Refresh(this);
        }
    }
    public sealed class CompProperties_LegionShield : CompProperties_Shield
    {
        public CompProperties_LegionShield() { compClass = typeof(CompLegionShield); }
    }
    // Native shield damage, recharge and EMP rules; only equipment artwork and initial charge are supplied here.
    public sealed class CompLegionShield : CompShield
    {
        public override void PostPostMake() { base.PostPostMake(); energy = 1; }
        public override void CompDrawWornExtras()
        {
            base.CompDrawWornExtras();
            var wearer = (parent as Apparel)?.Wearer;
            if (wearer == null || !wearer.Spawned || wearer.Dead) return;
            Vector3 at = wearer.DrawPos;
            float side = wearer.Rotation == Rot4.West ? .47f : -.47f;
            at.x += side;
            at.y += wearer.Rotation == Rot4.North ? -.01f : .04f;
            at.z -= .12f;
            parent.Graphic.Draw(at, Rot4.North, parent, 0);
        }
    }
}
