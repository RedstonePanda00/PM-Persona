using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using PMP_Persona.ShipSupport;
using PMP_Persona.PrismSupport;

namespace PMP_Persona.VanguardSupport
{
    public sealed class OrbitalWeaponConfig
    {
        public string labelKey;
        public int magazine = 1;
        public float reloadSeconds = 8, magazineSeconds = 0;
        public ThingDef projectile;
    }

    public sealed class OrbitalSupportConfig : DefModExtension
    {
        public int supportSeconds = 60;
        public float entrySeconds = 7;
        public int fractureDamage = 400, fractureGuidanceTicks = 72;
        public float fractureRadius = 12;
        public List<OrbitalWeaponConfig> weapons;
    }

    // Added only while a support is active. CustomMapComponent is excluded from FillComponents.
    public sealed class OrbitalSupportSession : CustomMapComponent
    {
        internal enum SupportPhase { Arrival, FireControl, Departure }
        internal SupportPhase Phase;
        internal float EntryElapsed, ExitElapsed;
        internal Vector3 ReturnPosition, ViewPosition;
        internal float ReturnSize, Bearing, DesiredBearing;
        internal TimeSpeed ReturnSpeed;
        internal bool CameraSaved, Infrared = true;
        internal ShipSupportDef SupportDef;
        internal int SelectedWeapon = 1;
        internal bool FireHeld;
        internal IntVec3 AimCell;
        private int combatTicks, durationTicks = 3600;
        public bool Unlimited;
        private int weaponMask = 14;
        public bool WeaponAvailable(int weapon) => (weaponMask & (1 << weapon)) != 0;
        public bool FireControlReady => Ready;
        public void ConfigureWeapons(int mask) { weaponMask = mask; if (!WeaponAvailable(SelectedWeapon)) CycleWeapon(); }
        public static OrbitalSupportSession Start(Map map, bool unlimited = false, int weapons = 14) { var session = Attach(map, DefDatabase<ShipSupportDef>.GetNamed("PMP_Support_Prism")); session.Unlimited = unlimited; session.ConfigureWeapons(weapons); VanguardFireControl.CurrentController.Begin(session); return session; }
        public void Close() { if (VanguardFireControl.CurrentController?.Ship == this) VanguardFireControl.CurrentController.End(); map.components.Remove(this); }
        private int lastFireTick1 = -999999, lastFireTick2 = -999999;
        private List<int> ammunition = new List<int>(), reloadUntil = new List<int>();
        private List<float> nextShot = new List<float>();
        internal bool IsPrism => SupportDef?.defName == "PMP_Support_Prism";
        internal OrbitalSupportConfig Config => SupportDef.GetModExtension<OrbitalSupportConfig>();
        internal Map Map => map;
        internal IntVec3 Position => map.Center;
        internal bool Spawned => map.components.Contains(this);
        internal bool Destroyed => !Spawned;
        internal bool Ready => Phase == SupportPhase.FireControl && Spawned;
        internal int CombatTicks => combatTicks;
        internal float EntryDuration => Mathf.Max(1, Config?.entrySeconds ?? 7);
        internal float EntryEnd => IsPrism ? EntryDuration : EntryDuration * VanguardPresentation.EntryEnd / 7;
        public int RemainingLifeTicks => Unlimited ? int.MaxValue : Mathf.Max(0, durationTicks - combatTicks);
        internal float RemainingFraction => Unlimited ? 1 : (float)RemainingLifeTicks / durationTicks;

        public OrbitalSupportSession(Map map) : base(map) { }
        internal static OrbitalSupportSession Attach(Map map, ShipSupportDef def)
        {
            foreach (Map other in Find.Maps)
                if (other.GetComponent<OrbitalSupportSession>() != null)
                    throw new InvalidOperationException("An orbital fire-control session is already active.");
            var session = new OrbitalSupportSession(map) { SupportDef = def };
            session.SetupOrbit();
            map.components.Add(session);
            return session;
        }
        internal void SetupOrbit()
        {
            durationTicks = Mathf.Clamp(Config?.supportSeconds ?? 60, 1, 600) * 60;
            combatTicks = 0; EntryElapsed = ExitElapsed = 0; Phase = SupportPhase.Arrival;
            CameraSaved = false;
            ammunition.Clear(); reloadUntil.Clear(); nextShot.Clear();
            if (IsPrism)
                foreach (var weapon in Config.weapons)
                { ammunition.Add(weapon.magazine); reloadUntil.Add(0); nextShot.Add(0); }
        }
        public override void MapComponentTick()
        {
            RefreshReloads();
            if (Ready && IsPrism && FireHeld) TryFire(SelectedWeapon, AimCell);
            if (Ready && !Unlimited && ++combatTicks >= durationTicks) Phase = SupportPhase.Departure;
        }
        private void RefreshReloads()
        {
            int now = Find.TickManager.TicksGame;
            for (int i = 0; i < reloadUntil.Count; i++)
                if (reloadUntil[i] > 0 && now >= reloadUntil[i])
                { ammunition[i] = Config.weapons[i].magazine; reloadUntil[i] = 0; }
        }
        internal int Ammo(int weapon) => ammunition[weapon - 1];
        internal int Capacity(int weapon) => Config.weapons[weapon - 1].magazine;
        internal float ReloadFraction(int weapon) => Mathf.Clamp01((float)CooldownTicks(weapon)
            / Mathf.Max(1, Config.weapons[weapon - 1].reloadSeconds * 60));
        internal int CooldownTicks(int weapon) => IsPrism
            ? Mathf.Max(0, reloadUntil[weapon - 1] - Find.TickManager.TicksGame)
            : Mathf.Max(0, (weapon == 1 ? lastFireTick1 + 15 : lastFireTick2 + 300) - Find.TickManager.TicksGame);
        internal float CooldownFraction(int weapon) => IsPrism ? ReloadFraction(weapon)
            : (float)CooldownTicks(weapon) / (weapon == 1 ? 15 : 300);
        internal void CycleWeapon() { if (!IsPrism) return; for(int i=0;i<3;i++){ SelectedWeapon = SelectedWeapon % 3 + 1; if(WeaponAvailable(SelectedWeapon))return; } }
        internal bool TryFire(int weapon, IntVec3 target)
        {
            if (!WeaponAvailable(weapon) || !Ready || Find.TickManager.Paused || Find.CurrentMap != map || !target.InBounds(map)) return false;
            if (IsPrism) return TryFirePrism(weapon, target);
            if ((weapon != 1 && weapon != 2) || CooldownTicks(weapon) > 0) return false;
            var incoming = DefDatabase<ThingDef>.GetNamed(weapon == 1 ? "PMP_VanguardNeutronIncoming" : "PMP_AntiparticleShellIncoming");
            if (weapon == 1) lastFireTick1 = Find.TickManager.TicksGame;
            else lastFireTick2 = Find.TickManager.TicksGame;
            int spread = weapon == 1 ? 1 : 2;
            for (int i = 0; i < (weapon == 1 ? 1 : 5); i++)
            {
                IntVec3 cell = target + new IntVec3(Rand.RangeInclusive(-spread, spread), 0, Rand.RangeInclusive(-spread, spread));
                if (!cell.InBounds(map)) cell = target;
                var sky = PMPH_OrbitalIncoming.Spawn(incoming, cell, map);
                if (weapon == 2) sky.ticksToImpact += i * 15;
                if (sky is PMPH_OrbitalIncoming orbital) orbital.VanguardWeapon = weapon;
            }
            VanguardFireControl.CurrentController?.WeaponFired(this, weapon, Find.TickManager.TicksGame);
            return true;
        }
        private bool TryFirePrism(int weapon, IntVec3 target)
        {
            if (weapon < 1 || weapon > 3) return false;
            RefreshReloads();
            int i = weapon - 1, now = Find.TickManager.TicksGame;
            if (ammunition[i] <= 0 || now < nextShot[i]) return false;
            var spec = Config.weapons[i];
            if (weapon == 1) PrismFractureEffects.Get(map).Add(target, Config);
            else
            {
                var round = (PMPH_OrbitalIncoming)PMPH_OrbitalIncoming.Spawn(spec.projectile, target, map);
                round.VanguardWeapon = weapon == 2 ? 2 : 1;
            }
            // Carry fractional tick spacing: 40 rounds can span 90 ticks without rounding drift.
            float interval = spec.magazine > 1 ? spec.magazineSeconds * 60 / (spec.magazine - 1) : 1;
            float origin = now - nextShot[i] <= 1 && nextShot[i] > 0 ? nextShot[i] : now;
            nextShot[i] = origin + Mathf.Max(1, interval);
            if (--ammunition[i] == 0) reloadUntil[i] = now + Mathf.Max(1, Mathf.RoundToInt(spec.reloadSeconds * 60));
            VanguardFireControl.CurrentController?.WeaponFired(this, weapon, now);
            return true;
        }
        public override void MapRemoved()
        {
            if (VanguardFireControl.CurrentController?.Ship == this)
                VanguardFireControl.CurrentController.End(false, false);
        }
        public override void ExposeData()
        {
            Scribe_Defs.Look(ref SupportDef, "supportDef");
            Scribe_Values.Look(ref Unlimited,"unlimited"); Scribe_Values.Look(ref weaponMask,"weaponMask",14);
            Scribe_Values.Look(ref Phase, "supportPhase");
            Scribe_Values.Look(ref EntryElapsed, "entryElapsed");
            Scribe_Values.Look(ref ExitElapsed, "exitElapsed");
            Scribe_Values.Look(ref combatTicks, "combatTicks");
            Scribe_Values.Look(ref durationTicks, "supportDurationTicks", 3600);
            Scribe_Values.Look(ref lastFireTick1, "lastFireTick1", -999999);
            Scribe_Values.Look(ref lastFireTick2, "lastFireTick2", -999999);
            Scribe_Values.Look(ref ReturnPosition, "returnCameraPosition");
            Scribe_Values.Look(ref ViewPosition, "fireControlPosition");
            Scribe_Values.Look(ref ReturnSize, "returnCameraSize", 24);
            Scribe_Values.Look(ref ReturnSpeed, "returnTimeSpeed", TimeSpeed.Normal);
            Scribe_Values.Look(ref CameraSaved, "cameraSaved");
            Scribe_Values.Look(ref Infrared, "infraredSensor", true);
            Scribe_Values.Look(ref Bearing, "bearing");
            Scribe_Values.Look(ref DesiredBearing, "desiredBearing");
            Scribe_Values.Look(ref SelectedWeapon, "selectedWeapon", 1);
            Scribe_Collections.Look(ref ammunition, "ammunition", LookMode.Value);
            Scribe_Collections.Look(ref reloadUntil, "reloadUntil", LookMode.Value);
            Scribe_Collections.Look(ref nextShot, "nextShot", LookMode.Value);
        }
    }
}
