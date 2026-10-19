using Verse;
using System.Collections.Generic;
using RimWorld;
using RsPandaLibrary;

namespace PMP_Persona
{
    public class CompProperties_AvengerProtocolManager : CompProperties
    {
        public string hediffDef;
        public int chargeResetTicks;

        public CompProperties_AvengerProtocolManager()
        {
            compClass = typeof(CompAvengerProtocolManager);
        }
    }

    public class CompAvengerProtocolManager : ThingComp
    {
        private static readonly HashSet<CompAvengerProtocolManager> registeredInstances =
            new HashSet<CompAvengerProtocolManager>();
        private static Game registeredGame;

        private int currentCharges = 0;
        private int lastChargeTick = -999999;

        public CompProperties_AvengerProtocolManager Props => (CompProperties_AvengerProtocolManager)props;

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);

            if (!ReferenceEquals(registeredGame, Current.Game))
            {
                registeredInstances.Clear();
                registeredGame = Current.Game;
            }

            registeredInstances.Add(this);
            RsPandaLibrary.CompAutoTransform.OnPawnTransformed -= HandleAnyTransformation;
            RsPandaLibrary.CompAutoTransform.OnPawnTransformed += HandleAnyTransformation;
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            registeredInstances.Remove(this);
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);

            CompAvengerProtocolManager manager = ResolveManager(parent.Map);
            if (manager != null && manager != this)
            {
                currentCharges = manager.currentCharges;
                lastChargeTick = manager.lastChargeTick;
            }
            else
            {
                SynchronizeStateOnMap(parent.Map, currentCharges, lastChargeTick);
            }
        }

        private static void HandleAnyTransformation(Pawn newPawn)
        {
            if (!PersonaHeroLifecycleBootstrap.IsManagedHero(newPawn))
                return;

            Map map = newPawn?.Map;
            CompAvengerProtocolManager manager = ResolveManager(map);
            manager?.HandleTransformation(newPawn);
        }

        private static CompAvengerProtocolManager ResolveManager(Map map)
        {
            if (map == null)
                return null;

            CompAvengerProtocolManager chosen = null;
            List<CompAvengerProtocolManager> stale = null;

            foreach (CompAvengerProtocolManager candidate in registeredInstances)
            {
                if (candidate?.parent == null || candidate.parent.Destroyed)
                {
                    if (stale == null)
                        stale = new List<CompAvengerProtocolManager>();
                    stale.Add(candidate);
                    continue;
                }

                if (candidate.parent.Map != map)
                    continue;

                if (chosen == null || candidate.parent.thingIDNumber < chosen.parent.thingIDNumber)
                    chosen = candidate;
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                    registeredInstances.Remove(stale[i]);
            }

            return chosen;
        }

        private static void SynchronizeStateOnMap(Map map, int charges, int chargeTick)
        {
            if (map == null)
                return;

            foreach (CompAvengerProtocolManager manager in registeredInstances)
            {
                if (manager?.parent == null || manager.parent.Destroyed || manager.parent.Map != map)
                    continue;

                manager.currentCharges = charges;
                manager.lastChargeTick = chargeTick;
            }
        }

        private void HandleTransformation(Pawn newPawn)
        {
            if (newPawn?.Map == null || newPawn.Map != parent.Map)
                return;

            HediffDef hediffToApply = HediffDef.Named(Props.hediffDef);
            if (hediffToApply == null)
            {
                Log.Error($"[Avenger Protocol] HediffDef '{Props.hediffDef}' not found!");
                return;
            }

            if (currentCharges > 0)
            {
                Hediff hediff = HediffMaker.MakeHediff(hediffToApply, newPawn);
                hediff.Severity = currentCharges;
                newPawn.health.AddHediff(hediff);
                Messages.Message($"Avenging Protocol activated on {newPawn.LabelShortCap}. Level: {currentCharges}", newPawn, MessageTypeDefOf.PositiveEvent);
            }

            currentCharges++;
            lastChargeTick = Find.TickManager.TicksGame;
            SynchronizeStateOnMap(parent.Map, currentCharges, lastChargeTick);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (ResolveManager(parent.Map) != this)
                return;

            if (Find.TickManager.TicksGame % 250 == 0)
            {
                if (currentCharges > 0 && Find.TickManager.TicksGame > lastChargeTick + Props.chargeResetTicks)
                {
                    currentCharges = 0;
                    SynchronizeStateOnMap(parent.Map, currentCharges, lastChargeTick);
                    Messages.Message($"{parent.LabelCap}'s avenging protocol charge has reset.", parent, MessageTypeDefOf.NeutralEvent);
                }
            }
        }

        public override string CompInspectStringExtra()
        {
            return $"Avenging Protocol Charge: {currentCharges}";
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref currentCharges, "currentCharges", 0);
            Scribe_Values.Look(ref lastChargeTick, "lastChargeTick", -999999);
        }
    }
}
