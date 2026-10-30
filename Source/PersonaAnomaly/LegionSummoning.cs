using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace PMP.Anomaly
{
    public sealed class PsychicRitualDef_LegionChallenge : PsychicRitualDef_InvocationCircle
    {
        public PawnKindDef challenger;
        public int defeatsRequired = 1;
        public float damagePerDefeat = 100f;
        public List<ThingDefCountClass> offerings = new List<ThingDefCountClass>();
        public override List<PsychicRitualToil> CreateToils(PsychicRitual ritual, PsychicRitualGraph graph)
        {
            var toils = base.CreateToils(ritual, graph);
            toils.Add(new PsychicRitualToil_LegionChallenge());
            return toils;
        }
        public override TaggedString TimeAndOfferingLabel() => base.TimeAndOfferingLabel()+"\n"+
            "PMP.LegionRitual.Offerings".Translate(string.Join(", ",offerings.Select(o=>o.count+" x "+o.thingDef.LabelCap)));
        public override IEnumerable<string> BlockingIssues(PsychicRitualRoleAssignments assignments, Map map)
        {
            foreach(var issue in base.BlockingIssues(assignments,map))yield return issue;
            if(!TryOfferings(map,assignments.AllAssignedPawns,false))yield return "PMP.LegionRitual.MissingOfferings".Translate();
        }
        public bool TryOfferings(Map map,IEnumerable<Pawn> handlers,bool consume)
        {
            var pawns=handlers.Where(p=>p!=null&&p.Spawned&&p.Map==map&&!p.Dead&&!p.Downed).ToList();
            var payment=new List<(Thing thing,int count)>();
            // Check every material before taking anything. Concurrent rituals cannot share a paid stack.
            foreach(var cost in offerings)
            {
                int remaining=cost.count;
                foreach(var item in map.listerThings.ThingsOfDef(cost.thingDef).ToList())
                {
                    if(remaining<=0)break;
                    if(item.Fogged()||!pawns.Any(p=>!item.IsForbidden(p)&&p.CanReserveAndReach(item,PathEndMode.Touch,Danger.Some)))continue;
                    int take=Math.Min(remaining,item.stackCount);payment.Add((item,take));remaining-=take;
                }
                if(remaining>0)return false;
            }
            if(consume)foreach(var entry in payment)entry.thing.SplitOff(entry.count).Destroy();
            return true;
        }
        public override IEnumerable<string> ConfigErrors()
        {
            foreach(var error in base.ConfigErrors())yield return error;
            if(challenger==null)yield return "Missing legion challenger.";
            else if(challenger.defaultFactionDef==null)yield return "Missing default challenger faction.";
            if(defeatsRequired<1||damagePerDefeat<=0)yield return "Invalid defeat count or damage threshold.";
            if(offerings.Count!=3||offerings.Any(o=>o.thingDef==null||o.count<=0)||offerings.Select(o=>o.thingDef).Distinct().Count()!=3)
                yield return "Legion challenge requires three distinct positive material costs.";
        }
    }

    public sealed class PsychicRitualToil_LegionChallenge : PsychicRitualToil
    {
        public override void Start(PsychicRitual ritual,PsychicRitualGraph parent)
        {
            base.Start(ritual,parent);
            var def=(PsychicRitualDef_LegionChallenge)ritual.def;
            var map=ritual.assignments.Target.Map;
            var handlers=ritual.assignments.AllAssignedPawns.ToList();
            if(!RCellFinder.TryFindRandomPawnEntryCell(out var cell,map,0f)||!def.TryOfferings(map,handlers,true))
            {
                ritual.CancelPsychicRitual("PMP.LegionRitual.MissingOfferings".Translate());return;
            }
            ritual.ReleaseAllPawnsAndBuildings();
            map.GetComponent<LegionChallengeMapComponent>().Summon(def,cell);
        }
    }

    public sealed class LegionChallengeMapComponent : MapComponent
    {
        public LegionChallengeMapComponent(Map map):base(map){}
        public Pawn Summon(PsychicRitualDef_LegionChallenge def,IntVec3 cell)
        {
            var factionDef=def.challenger.defaultFactionDef;
            var faction=Find.FactionManager.FirstFactionOfDef(factionDef);
            if(faction==null)
            {
                faction=FactionGenerator.NewGeneratedFaction(new FactionGeneratorParms(factionDef));
                Find.FactionManager.Add(faction);
            }
            var pawn=PawnGenerator.GeneratePawn(def.challenger,faction);
            GenSpawn.Spawn(pawn,cell,map);
            var challenge=(Hediff_LegionChallenge)HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("PMP_LegionChallenge"),pawn);
            challenge.remaining=def.defeatsRequired;challenge.damagePerDefeat=def.damagePerDefeat;challenge.Severity=challenge.remaining;
            pawn.health.AddHediff(challenge);
            LordMaker.MakeNewLord(faction,new LordJob_AssaultColony(faction),map,new[]{pawn});
            Find.LetterStack.ReceiveLetter("PMP.LegionRitual.ArrivedLabel".Translate(),
                "PMP.LegionRitual.Arrived".Translate(pawn.LabelCap),LetterDefOf.ThreatBig,pawn);
            return pawn;
        }
        public override void MapComponentTick()
        {
            if(Find.TickManager.TicksGame%60!=0)return;
            foreach(var pawn in map.mapPawns.AllPawnsSpawned.ToList())
                pawn.health.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().FirstOrDefault()?.EnsureChoice();
        }
    }

    public sealed class Hediff_LegionChallenge : Hediff
    {
        public int remaining=1,roundVersion,graceUntil;
        public float accumulatedDamage,damagePerDefeat=100f;
        public bool transitioning;
        public override string LabelInBrackets=>remaining.ToString();
        public override string TipStringExtra=>"PMP.LegionRitual.Progress".Translate(remaining,accumulatedDamage.ToString("0"),damagePerDefeat.ToString("0"));
        public override void Notify_PawnPostApplyDamage(DamageInfo damage,float totalDamageDealt)
        {
            base.Notify_PawnPostApplyDamage(damage,totalDamageDealt);
            RecordDamage(totalDamageDealt);
        }
        public void RecordDamage(float damage)
        {
            if(transitioning||remaining<=0||Find.TickManager.TicksGame<graceUntil||damage<=0||!float.IsFinite(damage))return;
            accumulatedDamage+=damage;
            if(accumulatedDamage>=damagePerDefeat)DefeatRound();
        }
        public void DefeatRound()
        {
            if(transitioning||remaining<=0||pawn.Dead)return;
            transitioning=true;
            try
            {
                remaining--;roundVersion++;accumulatedDamage=0;Severity=Math.Max(.001f,remaining);
                graceUntil=Find.TickManager.TicksGame+1;
                RestoreInjuries();
                if(remaining==0)
                {
                    pawn.GetLord()?.Notify_PawnLost(pawn,PawnLostCondition.ForcedToJoinOtherLord);
                    pawn.jobs?.StopAll();pawn.SetFaction(Faction.OfPlayer);
                }
                pawn.health.CheckForStateChange(null,null);
            }
            finally{transitioning=false;}
            if(remaining==0)EnsureChoice();
            else Messages.Message("PMP.LegionRitual.Round".Translate(pawn.LabelCap,remaining),pawn,MessageTypeDefOf.NeutralEvent);
        }
        public void RestoreInjuries()
        {
            bool previous=transitioning;transitioning=true;
            try
            {
                // Preserve the challenge marker while repairing injuries, including severed vital parts.
                foreach(var injury in pawn.health.hediffSet.hediffs.Where(h=>h!=this&&(h is Hediff_Injury||h is Hediff_MissingPart)).ToList())
                    pawn.health.RemoveHediff(injury);
                pawn.health.forceDowned=false;
                pawn.health.CheckForStateChange(null,null);
            }
            finally{transitioning=previous;}
        }
        public void EnsureChoice()
        {
            if(remaining!=0||transitioning||pawn.Dead||!pawn.Spawned)return;
            if(Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_LegionController>().Any(l=>l.mech==pawn))return;
            var label="PMP.LegionRitual.ControlTitle".Translate(pawn.LabelCap);
            var letter=(ChoiceLetter_LegionController)LetterMaker.MakeLetter(label,"PMP.LegionRitual.ControlText".Translate(pawn.LabelCap),DefDatabase<LetterDef>.GetNamed("PMP_LegionControllerChoice"),pawn);
            letter.mech=pawn;letter.title=label;
            Find.LetterStack.ReceiveLetter(letter);Find.TickManager.CurTimeSpeed=TimeSpeed.Paused;letter.OpenLetter();
        }
        public override void ExposeData()
        {
            base.ExposeData();Scribe_Values.Look(ref remaining,"remaining",1);Scribe_Values.Look(ref roundVersion,"roundVersion");
            Scribe_Values.Look(ref accumulatedDamage,"accumulatedDamage");Scribe_Values.Look(ref damagePerDefeat,"damagePerDefeat",100f);
            Scribe_Values.Look(ref graceUntil,"graceUntil");
        }
    }

    public sealed class ChoiceLetter_LegionController : ChoiceLetter
    {
        public Pawn mech;
        public void SelectController(Pawn controller)
        {
            if(mech==null||mech.Dead||mech.Faction!=Faction.OfPlayer)return;
            if(controller!=null)
            {
                if(!MechanitorUtility.IsMechanitor(controller)||!MechanitorUtility.CanControlMech(controller,mech).Accepted)return;
                var old=mech.GetOverseer();if(old!=null)mech.relations.RemoveDirectRelation(PawnRelationDefOf.Overseer,old);
                mech.relations.AddDirectRelation(PawnRelationDefOf.Overseer,controller);
            }
            var marker=mech.health.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().FirstOrDefault();
            if(marker!=null)mech.health.RemoveHediff(marker);
            Find.LetterStack.RemoveLetter(this);
        }
        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if(mech==null||mech.Dead){yield return Option_Close;yield break;}
                foreach(var controller in Find.Maps.SelectMany(m=>m.mapPawns.FreeColonists).Where(p=>!p.Dead&&MechanitorUtility.IsMechanitor(p)).Distinct())
                {
                    var selected=controller;
                    var option=new DiaOption(controller.LabelShortCap){action=()=>SelectController(selected),resolveTree=true};
                    var can=MechanitorUtility.CanControlMech(controller,mech);
                    if(!can.Accepted)option.Disable(can.Reason);
                    yield return option;
                }
                yield return new DiaOption("PMP.LegionRitual.Autonomous".Translate()){action=()=>SelectController(null),resolveTree=true};
                yield return Option_Postpone;
            }
        }
        public override void ExposeData(){base.ExposeData();Scribe_References.Look(ref mech,"legionMech");}
    }

    [HarmonyPatch(typeof(Pawn),nameof(Pawn.Kill))]
    static class LegionChallengeDefeat
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Pawn __instance)
        {
            var h=__instance.health?.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().FirstOrDefault();
            if(h==null)return true;
            if(h.transitioning||Find.TickManager.TicksGame<h.graceUntil)return false;
            if(h.remaining<=0)return true;
            h.DefeatRound();return false;
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.PostApplyDamage))]
    static class LegionChallengeFinishDamage
    {
        static void Postfix(Pawn __instance)
        {
            var h=__instance.health?.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().FirstOrDefault();
            // DamageWorker can add more wounds after a cancelled lethal Kill. Repair after it finishes.
            if(h!=null&&Find.TickManager.TicksGame<h.graceUntil)h.RestoreInjuries();
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.PreApplyDamage))]
    static class LegionChallengeTransitionProtection
    {
        [HarmonyPriority(Priority.First)]
        static bool Prefix(Pawn __instance,ref bool absorbed)
        {
            var h=__instance.health?.hediffSet.hediffs.OfType<Hediff_LegionChallenge>().FirstOrDefault();
            if(h==null||!h.transitioning&&Find.TickManager.TicksGame>=h.graceUntil)return true;
            absorbed=true;return false;
        }
    }
}
