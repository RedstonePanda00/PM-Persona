using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PMP_Persona.RecomposerSupport
{
    public static class RecomposerPawnUtility
    {
        private sealed class RelationSnapshot
        {
            public PawnRelationDef def;
            public Pawn other;
        }

        private sealed class Conversion
        {
            public Pawn oldPawn;
            public Pawn sentinel;
            public IntVec3 cell;
            public Rot4 rotation;
            public List<RelationSnapshot> relations = new List<RelationSnapshot>();
        }

        internal static List<Pawn> Targets(Map map, IntVec3 center) => map.mapPawns.AllPawnsSpawned
            .Where(p => IsLegalTarget(p, center)).ToList();

        internal static Pawn PrepareReplacement(Pawn source)
        {
            var kind = DefDatabase<PawnKindDef>.GetNamed(RecomposerConstants.SentinelKindDefName);
            Pawn result = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
            try { CopyCompatibleProperties(source, result); return result; }
            catch { RecomposerVanillaBypass.ForceDiscard(result); throw; }
        }

        public static void ConvertTargets(Map map, IntVec3 center)
        {
            if (map == null) return;
            var entries = new List<RecomposerSubject>();
            foreach (var source in Targets(map, center))
            {
                try { entries.Add(new RecomposerSubject { original = source, replacement = PrepareReplacement(source),
                    cell = source.Position, facing = source.Rotation, posture = source.GetPosture() }); }
                catch (Exception e) { Log.Error("[PMP Recomposer] Preparation failed; original retained: " + e); }
            }
            CompleteCaptured(map, entries);
        }

        // Originals stay intact until replacements have been prepared. Also used by the saved visual session.
        internal static void CompleteCaptured(Map map, List<RecomposerSubject> entries)
        {
            var conversions = new List<Conversion>();
            var replacements = new Dictionary<Pawn, Pawn>();
            foreach (var entry in entries)
            {
                if (entry.completed || entry.original == null || entry.replacement == null) continue;
                var conversion = new Conversion { oldPawn = entry.original, sentinel = entry.replacement,
                    cell = entry.cell, rotation = entry.facing };
                CaptureRelations(entry.original, conversion.relations);
                conversions.Add(conversion);
                replacements[entry.original] = entry.replacement;
            }
            foreach (var conversion in conversions)
                TransferPortableThings(conversion.oldPawn, conversion.sentinel, map, conversion.cell);
            foreach (var conversion in conversions)
                RecomposerVanillaBypass.PermanentlyRemove(conversion.oldPawn);
            RestoreRelations(conversions, replacements);
            foreach (var entry in entries)
            {
                if (entry.completed || entry.replacement == null || !entry.original.Discarded) continue;
                var pawn = entry.replacement;
                pawn.holdingOwner?.Remove(pawn);
                if (!TrySpawnSentinel(pawn, map, entry.cell, entry.facing))
                    map.GetComponent<MapComponent_RecomposerQueue>().Enqueue(pawn, entry.cell, entry.facing);
                entry.completed = true;
            }
        }

        public static bool TrySpawnSentinel(Pawn pawn, Map map, IntVec3 desiredCell, Rot4 rotation)
        {
            if (pawn == null || map == null)
                return false;

            IntVec3 spawnCell = FindSpawnCell(map, desiredCell);
            if (!spawnCell.IsValid)
                return false;

            try
            {
                GenSpawn.Spawn(pawn, spawnCell, map, rotation, WipeMode.Vanish, false);
                return pawn.Spawned;
            }
            catch (Exception e)
            {
                Log.Warning("[PMP Recomposer] Sentinel spawn attempt failed at " + spawnCell + ": " + e);
                return false;
            }
        }

        private static bool IsLegalTarget(Pawn pawn, IntVec3 center)
        {
            return pawn != null
                   && pawn.Spawned
                   && !pawn.Dead
                   && pawn.Faction != Faction.OfPlayer
                   && pawn.RaceProps != null
                   && !pawn.RaceProps.IsMechanoid
                   && pawn.Position.InHorDistOf(center, RecomposerConstants.TargetRadius);
        }

        private static IntVec3 FindSpawnCell(Map map, IntVec3 desiredCell)
        {
            foreach (IntVec3 cell in GenRadial.RadialCellsAround(desiredCell, RecomposerConstants.SpawnSearchRadius, true))
            {
                if (!cell.InBounds(map) || !cell.Standable(map))
                    continue;
                if (cell.GetFirstPawn(map) != null)
                    continue;
                return cell;
            }
            return IntVec3.Invalid;
        }

        private static void CopyCompatibleProperties(Pawn source, Pawn target)
        {
            if (source.Name != null)
                target.Name = source.Name;
            target.gender = source.gender;

            if (source.ageTracker != null && target.ageTracker != null)
            {
                target.ageTracker.AgeBiologicalTicks = source.ageTracker.AgeBiologicalTicks;
                target.ageTracker.BirthAbsTicks = source.ageTracker.BirthAbsTicks;
            }

            if (!(target is Prometheus.Pawn_Prometheus) && source.skills != null && target.skills != null)
            {
                for (int i = 0; i < source.skills.skills.Count; i++)
                {
                    SkillRecord sourceSkill = source.skills.skills[i];
                    SkillRecord targetSkill = target.skills.GetSkill(sourceSkill.def);
                    if (targetSkill == null)
                        continue;
                    targetSkill.levelInt = sourceSkill.levelInt;
                    targetSkill.passion = sourceSkill.passion;
                    targetSkill.xpSinceLastLevel = sourceSkill.xpSinceLastLevel;
                    targetSkill.xpSinceMidnight = sourceSkill.xpSinceMidnight;
                }
            }

            if (!(target is Prometheus.Pawn_Prometheus) && source.story?.traits != null && target.story?.traits != null)
            {
                for (int i = 0; i < source.story.traits.allTraits.Count; i++)
                {
                    Trait trait = source.story.traits.allTraits[i];
                    if (trait?.def != null && !target.story.traits.HasTrait(trait.def))
                        target.story.traits.GainTrait(new Trait(trait.def, trait.Degree, trait.ScenForced), true);
                }
            }

            if (source.records != null && target.records != null)
            {
                List<RecordDef> defs = DefDatabase<RecordDef>.AllDefsListForReading;
                for (int i = 0; i < defs.Count; i++)
                {
                    RecordDef def = defs[i];
                    if (def.type == RecordType.Int || def.type == RecordType.Float)
                    {
                        float value = source.records.GetValue(def);
                        if (value != 0f)
                            target.records.AddTo(def, value);
                    }
                }
            }
        }

        private static void CaptureRelations(Pawn source, List<RelationSnapshot> output)
        {
            if (source.relations == null)
                return;
            List<DirectPawnRelation> relations = source.relations.DirectRelations;
            for (int i = 0; i < relations.Count; i++)
            {
                DirectPawnRelation relation = relations[i];
                if (relation?.def != null && relation.otherPawn != null)
                    output.Add(new RelationSnapshot { def = relation.def, other = relation.otherPawn });
            }
        }

        private static void RestoreRelations(List<Conversion> conversions, Dictionary<Pawn, Pawn> replacements)
        {
            for (int i = 0; i < conversions.Count; i++)
            {
                Conversion conversion = conversions[i];
                if (conversion.sentinel?.relations == null)
                    continue;

                for (int j = 0; j < conversion.relations.Count; j++)
                {
                    RelationSnapshot relation = conversion.relations[j];
                    Pawn other = relation.other;
                    Pawn replacement;
                    if (replacements.TryGetValue(other, out replacement))
                        other = replacement;
                    if (other == null || other.Discarded || other == conversion.sentinel)
                        continue;
                    if (!conversion.sentinel.relations.DirectRelationExists(relation.def, other))
                        conversion.sentinel.relations.AddDirectRelation(relation.def, other);
                }
            }
        }

        private static void RebindEquipment(ThingWithComps item, Pawn source, Pawn target)
        {
            foreach (var comp in item.AllComps.OfType<CompBiocodable>())
                if (comp.Biocoded && comp.CodedPawn == source) comp.CodeFor(target);
        }

        internal static void TransferPortableThings(Pawn source, Pawn target, Map map, IntVec3 cell)
        {
            if (target is Prometheus.Pawn_Prometheus prometheus) prometheus.EnsureApparel();
            if (source.apparel != null && target.apparel != null)
                foreach (var apparel in source.apparel.WornApparel.ToList())
                {
                    bool locked = source.apparel.IsLocked(apparel);
                    RebindEquipment(apparel, source, target);
                    source.apparel.Remove(apparel);
                    try
                    {
                        if (ApparelUtility.HasPartsToWear(target, apparel.def) && apparel.PawnCanWear(target, true))
                            target.apparel.Wear(apparel, locked: locked);
                        // Preserve even race-restricted third-party gear without dropping or destroying it.
                        if (apparel.Wearer != target && !target.inventory.innerContainer.TryAdd(apparel, false))
                            throw new InvalidOperationException("Replacement could not retain apparel " + apparel);
                    }
                    catch
                    {
                        if (apparel.Wearer == null && apparel.holdingOwner == null)
                        { RebindEquipment(apparel, target, source); source.apparel.Wear(apparel, locked: locked); }
                        throw;
                    }
                }

            if (source.equipment != null && target.equipment != null)
            {
                List<ThingWithComps> equipment = source.equipment.AllEquipmentListForReading.ToList();
                for (int i = 0; i < equipment.Count; i++)
                {
                    ThingWithComps item = equipment[i];
                    RebindEquipment(item, source, target);
                    source.equipment.Remove(item);
                    try
                    {
                        target.equipment.AddEquipment(item);
                    }
                    catch
                    {
                        GenPlace.TryPlaceThing(item, cell, map, ThingPlaceMode.Near);
                    }
                }
            }

            if (source.inventory?.innerContainer != null && target.inventory?.innerContainer != null)
                source.inventory.innerContainer.TryTransferAllToContainer(target.inventory.innerContainer);

            if (source.inventory?.innerContainer != null && source.inventory.innerContainer.Any)
                source.inventory.innerContainer.TryDropAll(cell, map, ThingPlaceMode.Near);
        }
    }
}
