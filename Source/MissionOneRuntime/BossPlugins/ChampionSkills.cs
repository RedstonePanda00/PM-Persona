using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;

namespace PMP.Campaign.BossPlugins
{
    [CampaignModule("persona.champion.skills","BossComponent","冠军技能组：抓取建筑／物品并向目标投掷")]
    public sealed class ChampionSkills : BossComponentPlugin
    {
        public override Dictionary<string,string> DefaultParameters => new Dictionary<string,string>{{"grabRange","6"},{"throwRange","30"},{"cooldownTicks","600"},{"speed","18"},{"impactRadius","1.9"},{"autoUse","1"}};
        static float Grab(BossComponentContext c)=>(float)Math.Clamp(c.Number("grabRange",6),1,30);
        static float Range(BossComponentContext c)=>(float)Math.Clamp(c.Number("throwRange",30),1,100);
        public static bool CanGrab(BossComponentContext c,Thing thing) => c.Available&&thing!=null&&thing.Spawned&&thing.Map==c.Map&&!(thing is Pawn)&&!(thing is Blueprint)&&!(thing is Frame)&&thing.def.useHitPoints&&thing.def.destroyable
            &&(thing.def.category==ThingCategory.Building||thing.def.category==ThingCategory.Item)
            &&(thing.Faction==null||thing.HostileTo(c.Pawn))&&thing.Position.DistanceTo(c.Pawn.Position)<=Grab(c)
            &&GenSight.LineOfSight(c.Pawn.Position,thing.Position,c.Map,skipFirstCell:true);
        public static bool TryThrow(BossComponentContext c,Thing payload,IntVec3 target)
        {
            int now=Find.TickManager.TicksGame;
            if(now<c.Read("throwReady")||!CanGrab(c,payload)||!target.InBounds(c.Map)||target==payload.Position||target.DistanceTo(c.Pawn.Position)>Range(c))return false;
            if(!ChampionThrownThing.Launch(c.Pawn,payload,target,Range(c),(float)Math.Clamp(c.Number("speed",18),1,60),(float)Math.Clamp(c.Number("impactRadius",1.9),0,8)))return false;
            c.Write("throwReady",now+Math.Clamp(c.Number("cooldownTicks",600),1,int.MaxValue-now));return true;
        }
        public override IEnumerable<Gizmo> Gizmos(BossComponentContext c)
        {
            var command=new Command_Action{defaultLabel="PMP.Champion.Throw".Translate(),defaultDesc="PMP.Champion.ThrowDesc".Translate(),icon=TexCommand.Attack,
                action=()=>Find.Targeter.BeginTargeting(new TargetingParameters{canTargetBuildings=true,canTargetItems=true,canTargetPawns=false,validator=t=>CanGrab(c,t.Thing)},grab=>{
                    var payload=grab.Thing;
                    Find.Targeter.BeginTargeting(new TargetingParameters{canTargetLocations=true,canTargetBuildings=true,canTargetItems=true,canTargetPawns=true,validator=t=>c.Available&&t.Cell.InBounds(c.Map)&&t.Cell.DistanceTo(c.Pawn.Position)<=Range(c)},target=>{
                        if(!TryThrow(c,payload,target.Cell))Messages.Message("PMP.Champion.ThrowRejected".Translate(),MessageTypeDefOf.RejectInput,false);
                    });
                })};
            if(!c.Available)command.Disable("PMP.Champion.Unavailable".Translate());
            else if(Find.TickManager.TicksGame<c.Read("throwReady"))command.Disable("PMP.Champion.Cooldown".Translate(Math.Ceiling((c.Read("throwReady")-Find.TickManager.TicksGame)/60)));
            yield return command;
        }
        public override void Tick(BossComponentContext c,Thing target)
        {
            if(c.Pawn.Faction==Faction.OfPlayer||c.Number("autoUse",1)==0||target==null||Find.TickManager.TicksGame<c.Read("throwReady")||target.Position.DistanceTo(c.Pawn.Position)>Range(c))return;
            var payload=GenRadial.RadialDistinctThingsAround(c.Pawn.Position,c.Map,Grab(c),true).Where(t=>CanGrab(c,t)&&t.Position!=target.Position)
                .OrderBy(t=>t.Position.DistanceToSquared(target.Position)).FirstOrDefault();
            if(payload!=null)TryThrow(c,payload,target.Position);
        }
    }
    public sealed class ChampionThrownThing : ThingWithComps,ISuspendableThingHolder
    {
        ThingOwner<Thing> contents;
        Pawn launcher;
        Vector3 start,direction;
        IntVec3 origin,lastSafe;
        Rot4 payloadRotation;
        float travelled,range,speed,radius,impactDamage;
        bool impacted;
        public bool IsContentsSuspended=>true;
        public Thing Payload=>contents.FirstOrDefault();
        public ChampionThrownThing(){contents=new ThingOwner<Thing>(this,false,LookMode.Deep);}
        public static bool Launch(Pawn launcher,Thing payload,IntVec3 target,float range,float speed,float radius)
        {
            var map=payload.Map;var origin=payload.Position;
            var flight=(ChampionThrownThing)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("PMP_ChampionThrownThing"));
            flight.launcher=launcher;flight.origin=flight.lastSafe=origin;flight.start=origin.ToVector3Shifted();flight.direction=(target.ToVector3Shifted()-flight.start).normalized;
            flight.payloadRotation=payload.Rotation;flight.range=range;flight.speed=speed;flight.radius=radius;flight.impactDamage=payload.MaxHitPoints*.5f;
            // Spawn the harmless holder before transferring ownership; a spawn failure leaves payload intact.
            GenSpawn.Spawn(flight,origin,map);
            payload.DeSpawn();
            if(!flight.contents.TryAdd(payload,false)){GenSpawn.Spawn(payload,origin,map,flight.payloadRotation);flight.Destroy();return false;}
            return true;
        }
        protected override void Tick()
        {
            base.Tick();
            if(Payload==null){Destroy();return;}
            if(impacted){if(this.IsHashIntervalTick(60))TryLand();return;}
            // Sweep every quarter-cell, including the full footprint, to avoid skipping walls.
            float end=Math.Min(range,travelled+speed/60f);
            while(travelled<end){
                travelled=Math.Min(end,travelled+.25f);
                var cell=(start+direction*travelled).ToIntVec3();
                if(!Footprint(cell).All(c=>c.InBounds(Map))){Impact(lastSafe);return;}
                Position=cell;
                var collision=Footprint(cell).FirstOrDefault(c=>c.GetThingList(Map).Any(t=>t!=this&&t!=launcher&&(t is Pawn||t.def.category==ThingCategory.Building||t.def.category==ThingCategory.Item)),IntVec3.Invalid);
                if(collision.IsValid){Impact(collision);return;}
                lastSafe=cell;
            }
            if(travelled>=range)Impact(lastSafe);
        }
        IEnumerable<IntVec3> Footprint(IntVec3 at)=>GenAdj.OccupiedRect(at,payloadRotation,Payload?.def.size??IntVec2.One);
        void Impact(IntVec3 cell)
        {
            if(impacted)return;impacted=true;Position=cell;
            // Materialize targets first: destruction and explosions can mutate the map's thing lists.
            var victims=GenRadial.RadialDistinctThingsAround(cell,Map,radius,true).Where(t=>t!=this&&(t is Pawn||t.def.category==ThingCategory.Building)).ToList();
            foreach(var victim in victims)if(!victim.Destroyed&&victim.Spawned)
                victim.TakeDamage(new DamageInfo(DamageDefOf.Blunt,impactDamage,instigator:launcher));
            // The projectile's own damage is not an offensive hit and cannot spend retaliation.
            using(BossComponents.SuppressOutgoing())Payload?.TakeDamage(new DamageInfo(DamageDefOf.Blunt,impactDamage,instigator:launcher));
            TryLand();
        }
        bool SafeLanding(IntVec3 cell)
        {
            var payload=Payload;
            return Footprint(cell).All(c=>c.InBounds(Map)&&(payload.def.terrainAffordanceNeeded==null||c.GetTerrain(Map).affordances.Contains(payload.def.terrainAffordanceNeeded))
                &&c.GetThingList(Map).All(t=>t==this||t.def.category==ThingCategory.Filth||t.def.category==ThingCategory.Mote||t.def.category==ThingCategory.Ethereal));
        }
        public bool TryLand()
        {
            var payload=Payload;
            if(payload==null||payload.Destroyed){if(payload!=null)contents.Remove(payload);Destroy();return true;}
            var candidates=GenRadial.RadialCellsAround(lastSafe,8,true).Concat(new[]{origin});
            foreach(var cell in candidates){
                if(!SafeLanding(cell))continue;
                contents.Remove(payload);
                try{GenSpawn.Spawn(payload,cell,Map,payloadRotation);Destroy();return true;}
                catch{if(!payload.Spawned&&!payload.Destroyed)contents.TryAdd(payload,false);throw;}
            }
            // Keep the original object in a persistent holder until a safe footprint becomes available.
            return false;
        }
        protected override void DrawAt(Vector3 drawLoc,bool flip=false)
        {
            if(Payload==null)return;
            var pos=impacted?Position.ToVector3Shifted():start+direction*travelled;
            pos.y=AltitudeLayer.Projectile.AltitudeFor();Payload.Graphic.Draw(pos,payloadRotation,Payload);
        }
        public override string GetInspectString()=>Payload==null?base.GetInspectString():Payload.LabelCap+": "+(impacted?"PMP.Champion.AwaitingLanding":"PMP.Champion.InFlight").Translate();
        public override void Destroy(DestroyMode mode=DestroyMode.Vanish){if(contents.Count==0)base.Destroy(mode);}
        public override void PreApplyDamage(ref DamageInfo damage,out bool absorbed){absorbed=true;}
        public ThingOwner GetDirectlyHeldThings()=>contents;
        public void GetChildHolders(List<IThingHolder> output)=>ThingOwnerUtility.AppendThingHoldersFromThings(output,contents);
        public override void ExposeData()
        {
            base.ExposeData();Scribe_Deep.Look(ref contents,"payload",this);Scribe_References.Look(ref launcher,"launcher");
            Scribe_Values.Look(ref start,"start");Scribe_Values.Look(ref direction,"direction");Scribe_Values.Look(ref origin,"origin");Scribe_Values.Look(ref lastSafe,"lastSafe");Scribe_Values.Look(ref payloadRotation,"payloadRotation");
            Scribe_Values.Look(ref travelled,"travelled");Scribe_Values.Look(ref range,"range");Scribe_Values.Look(ref speed,"speed");Scribe_Values.Look(ref radius,"radius");Scribe_Values.Look(ref impactDamage,"impactDamage");Scribe_Values.Look(ref impacted,"impacted");
        }
    }
}
