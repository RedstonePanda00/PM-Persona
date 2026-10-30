using System;
using System.Collections.Generic;
using HarmonyLib;
using System.Reflection;
using System.Diagnostics;
using RimWorld;
using UnityEngine;
using Verse;
using PMP_Persona.PrismSupport;
using PMP_Persona.VanguardSupport;
using PMP_Persona.AirPatrolLegion;
using PMP_Persona.EnergyFieldSupport;
using PMP_Persona.SupernovaSupport;
using PMP_Persona.RecomposerSupport;
using ProtossMech;

namespace PMP_Persona
{
    [StaticConstructorOnStartup]
    public static class HarmonyPatches
    {
        static HarmonyPatches()
        {
            var harmony = new Harmony("com.protossmech.persona.splashcomp");
            harmony.PatchAll();

            TryPatchPersonaHeroSeverityRegistrations(harmony);

            TryRegisterPersonaHeroReviveHandler();
        }

        private static void TryRegisterPersonaHeroReviveHandler()
        {
            try
            {
                RsPandaLibrary.CompAutoTransform.OnPawnTransformed -= OnPersonaHeroActivated;
                RsPandaLibrary.CompAutoTransform.OnPawnTransformed += OnPersonaHeroActivated;
            }
            catch
            {
                // ignore
            }
        }

        private static void OnPersonaHeroActivated(Pawn pawn)
        {
            if (!IsPersonaHeroPawn(pawn))
                return;

            try
            {
                // Restore full shield on revive/activate.
                ProtossMechShield shield = pawn.GetComp<ProtossMechShield>();
                shield?.RestoreShield();
            }
            catch
            {
                // ignore
            }

            try
            {
                // Reset ability cooldowns.
                var tracker = pawn.abilities;
                if (tracker != null)
                {
                    List<RimWorld.Ability> abs = tracker.AllAbilitiesForReading;
                    for (int i = 0; i < abs.Count; i++)
                    {
                        abs[i]?.ResetCooldown();
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        private static void TryPatchPersonaHeroSeverityRegistrations(Harmony harmony)
        {
            try
            {
                // Block Persona registration in void (Pawn, float) methods named with Doom and Severity.

                MethodInfo prefix = typeof(HarmonyPatches).GetMethod(
                    nameof(Prefix_BlockPersonaHeroDoomSeverity),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null)
                    return;

                var patched = new HashSet<MethodInfo>();
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int a = 0; a < assemblies.Length; a++)
                {
                    Assembly asm = assemblies[a];
                    if (asm == null)
                        continue;

                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch
                    {
                        continue;
                    }

                    for (int t = 0; t < types.Length; t++)
                    {
                        Type type = types[t];
                        if (type == null)
                            continue;

                        MethodInfo[] methods;
                        try
                        {
                            methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                        }
                        catch
                        {
                            continue;
                        }

                        for (int m = 0; m < methods.Length; m++)
                        {
                            MethodInfo mi = methods[m];
                            if (mi == null || patched.Contains(mi))
                                continue;

                            // Only patch "void AddXxxDoomXxxSeverity(Pawn, float)"-like methods.
                            if (mi.ReturnType != typeof(void))
                                continue;
                            if (mi.Name == null)
                                continue;
                            if (!mi.Name.Contains("Doom") || !mi.Name.Contains("Severity"))
                                continue;

                            ParameterInfo[] ps = mi.GetParameters();
                            if (ps.Length != 2)
                                continue;
                            if (ps[0].ParameterType != typeof(Pawn))
                                continue;

                            // float can be represented as System.Single.
                            if (ps[1].ParameterType != typeof(float) && ps[1].ParameterType != typeof(Single))
                                continue;

                            patched.Add(mi);
                            harmony.Patch(mi, prefix: new HarmonyMethod(prefix));
                        }
                    }
                }
            }
            catch
            {
                // Ignore if MiliraImperium types are missing or reflection fails.
            }
        }

        internal static bool IsPersonaHeroPawn(Pawn pawn)
        {
            return pawn?.def?.defName != null && pawn.def.defName.StartsWith("PMPH_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Prefix_BlockPersonaHeroDoomSeverity(Pawn target, float amount)
        {
            if (IsPersonaHeroPawn(target))
                return false;
            return true;
        }

        internal static bool IsCallerAllowedForPersonaHero()
        {
            // Only block when the caller is definitely not from vanilla/our mods.
            string callerAsm = GetFirstExternalCallerAssemblyName();
            if (string.IsNullOrEmpty(callerAsm))
                return true;
            return AllowedGameComponentAssemblyNames.Contains(callerAsm);
        }

        internal static bool BlockThirdPartyTakeDamageOnPersonaHeroes = false;

        private static readonly FieldInfo PawnHealthTrackerPawnField =
            typeof(Pawn_HealthTracker).GetField("pawn", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static Pawn GetPawnFromHealthTracker(Pawn_HealthTracker tracker)
        {
            if (tracker == null || PawnHealthTrackerPawnField == null)
                return null;
            return PawnHealthTrackerPawnField.GetValue(tracker) as Pawn;
        }

        private static string GetFirstExternalCallerAssemblyName()
        {
            try
            {
                var st = new StackTrace();
                var frames = st.GetFrames();
                if (frames == null || frames.Length == 0)
                    return null;

                for (int i = 0; i < frames.Length; i++)
                {
                    var m = frames[i]?.GetMethod();
                    var declaringType = m?.DeclaringType;
                    var asmName = declaringType?.Assembly?.GetName()?.Name;
                    if (string.IsNullOrEmpty(asmName))
                        continue;

                    // Skip core runtime frames; find the first non-core caller.
                    if (asmName.Equals("Assembly-CSharp", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (asmName.StartsWith("System", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (asmName.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (asmName.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (asmName.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) >= 0)
                        continue;

                    return asmName;
                }
            }
            catch
            {
                // ignore
            }
            return null;
        }

        private static readonly HashSet<string> AllowedGameComponentAssemblyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Vanilla RimWorld code.
            "Assembly-CSharp",

            // Allowed mod assemblies.
            "PMP_Persona",
            "RsPandaLibrary",
            "ProtossMech",
        };

        private static int _lastHeroPresenceCheckTick = -999999;
        private static bool _cachedHasPersonaHeroes;
        private const int HeroPresenceRecheckIntervalTicks = 250;

        private static void TryDisableThirdPartyGameComponents(Harmony harmony)
        {
            try
            {
                MethodInfo prefix = typeof(HarmonyPatches).GetMethod(
                    nameof(Prefix_BlockThirdPartyGameComponentTickOrUpdate),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (prefix == null)
                    return;

                var patched = new HashSet<MethodInfo>();
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int a = 0; a < assemblies.Length; a++)
                {
                    Assembly asm = assemblies[a];
                    if (asm == null)
                        continue;

                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch
                    {
                        continue;
                    }

                    for (int t = 0; t < types.Length; t++)
                    {
                        Type type = types[t];
                        if (type == null || type.IsAbstract)
                            continue;
                        if (type == typeof(GameComponent))
                            continue;
                        if (!typeof(GameComponent).IsAssignableFrom(type))
                            continue;

                        string asmName = type.Assembly?.GetName()?.Name;
                        if (string.IsNullOrEmpty(asmName) || AllowedGameComponentAssemblyNames.Contains(asmName))
                            continue; // Allow vanilla and configured mod assemblies.

                        // Patch only overridden methods to avoid touching inherited no-op base.
                        MethodInfo tick = type.GetMethod(nameof(GameComponent.GameComponentTick),
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (tick != null && tick.DeclaringType == type && patched.Add(tick))
                            harmony.Patch(tick, prefix: new HarmonyMethod(prefix));

                        MethodInfo update = type.GetMethod(nameof(GameComponent.GameComponentUpdate),
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (update != null && update.DeclaringType == type && patched.Add(update))
                            harmony.Patch(update, prefix: new HarmonyMethod(prefix));
                    }
                }
            }
            catch
            {
                // Defensive: do nothing if reflection scan fails.
            }
        }

        private static bool Prefix_BlockThirdPartyGameComponentTickOrUpdate(GameComponent __instance)
        {
            // Apply the block while Persona heroes exist.
            return !HasAnyPersonaHeroes();
        }

        private static bool HasAnyPersonaHeroes()
        {
            if (Current.Game == null || Find.TickManager == null)
                return false;

            int tick = Find.TickManager.TicksGame;
            if (tick - _lastHeroPresenceCheckTick < HeroPresenceRecheckIntervalTicks && _lastHeroPresenceCheckTick >= 0)
                return _cachedHasPersonaHeroes;
            _lastHeroPresenceCheckTick = tick;

            // Check spawned and container-held pawns on maps.
            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++)
                {
                    Map map = Find.Maps[i];
                    if (map == null)
                        continue;

                    // Spawned pawns on map.
                    for (int p = 0; p < map.mapPawns.AllPawnsSpawned.Count; p++)
                    {
                        Pawn pawn = map.mapPawns.AllPawnsSpawned[p];
                        if (IsPersonaHeroPawn(pawn))
                            return _cachedHasPersonaHeroes = true;
                    }

                    // Directly-held pawns inside IThingHolder containers (e.g. shuttles).
                    foreach (Thing t in map.listerThings.AllThings)
                    {
                        if (!(t is IThingHolder holder))
                            continue;
                        ThingOwner directlyHeld = holder.GetDirectlyHeldThings();
                        if (directlyHeld == null)
                            continue;
                        foreach (Thing held in directlyHeld)
                        {
                            if (held is Pawn heldPawn && IsPersonaHeroPawn(heldPawn))
                                return _cachedHasPersonaHeroes = true;
                        }
                    }
                }
            }

            // Also consider caravans/traveling transporters (off-map but alive).
            try
            {
                _cachedHasPersonaHeroes = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_OfPlayerFaction
                    .Any(p => IsPersonaHeroPawn(p));
            }
            catch
            {
                // If PawnsFinder throws, fall back to map-only results.
            }
            return _cachedHasPersonaHeroes;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_Thing_TakeDamage
    {
        private static bool isSplashing = false;

        public static void Postfix(DamageInfo dinfo, Thing __instance)
        {
            if (isSplashing)
            {
                return;
            }

            if (!(dinfo.Instigator is Pawn attacker))
            {
                return;
            }

            var comp = attacker.GetComp<CompSplashOnMelee>();
            if (comp == null)
            {
                return;
            }

            // Vanilla TakeDamage is on the victim Thing; melee vs buildings uses Building not Pawn as __instance.
            if (!CompSplashOnMelee.IsSplashMeleeNotifyVictim(__instance))
            {
                return;
            }

            try
            {
                isSplashing = true;
                comp.Notify_DamageDealt(dinfo, __instance);
            }
            finally
            {
                isSplashing = false;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "AddHediff", new Type[] { typeof(HediffDef), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_Pawn_HealthTracker_AddHediffDef_BlockThirdPartyPersonaHero
    {
        public static bool Prefix(Pawn_HealthTracker __instance, HediffDef def)
        {
            Pawn pawn = HarmonyPatches.GetPawnFromHealthTracker(__instance);
            if (!HarmonyPatches.IsPersonaHeroPawn(pawn))
                return true;
            if (def?.defName == null)
                return true;
            // Allow injuries and missing body parts.
            if (def.hediffClass != null && (typeof(Hediff_Injury).IsAssignableFrom(def.hediffClass) || typeof(Hediff_MissingPart).IsAssignableFrom(def.hediffClass)))
                return true;
            return ProtossMechMain.Settings != null && ProtossMechMain.Settings.IsHediffAllowed(def.defName);
        }
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "AddHediff", new Type[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
    public static class Patch_Pawn_HealthTracker_AddHediff_BlockThirdPartyPersonaHero
    {
        public static bool Prefix(Pawn_HealthTracker __instance, Hediff hediff)
        {
            Pawn pawn = HarmonyPatches.GetPawnFromHealthTracker(__instance);
            if (!HarmonyPatches.IsPersonaHeroPawn(pawn))
                return true;
            // Allow injuries and missing body parts.
            if (hediff is Hediff_Injury || hediff is Hediff_MissingPart)
                return true;
            string defName = hediff?.def?.defName;
            if (defName == null)
                return true;
            return ProtossMechMain.Settings != null && ProtossMechMain.Settings.IsHediffAllowed(defName);
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_Thing_TakeDamage_BlockThirdPartyPersonaHero
    {
        public static bool Prefix(Thing __instance, DamageInfo dinfo, ref DamageWorker.DamageResult __result)
        {
            if (!HarmonyPatches.BlockThirdPartyTakeDamageOnPersonaHeroes)
                return true;

            if (!(__instance is Pawn pawn) || !HarmonyPatches.IsPersonaHeroPawn(pawn))
                return true;

            if (HarmonyPatches.IsCallerAllowedForPersonaHero())
                return true;

            __result = new DamageWorker.DamageResult();
            return false;
        }
    }

    [HarmonyPatch(typeof(Hediff), "set_Severity")]
    public static class Patch_Hediff_Severity_Set_BlockThirdPartyPersonaHero
    {
        public static bool Prefix(Hediff __instance, float value)
        {
            if (__instance?.pawn == null || !HarmonyPatches.IsPersonaHeroPawn(__instance.pawn))
                return true;
            // Allow injuries and missing body parts.
            if (__instance is Hediff_Injury || __instance is Hediff_MissingPart)
                return true;
            return HarmonyPatches.IsCallerAllowedForPersonaHero();
        }
    }

    [HarmonyPatch(typeof(GameComponent), nameof(GameComponent.FinalizeInit))]
    public static class Patch_GameComponent_FinalizeInit_PawnCountCache
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            SpawnedPawnCountCache.ForceRefreshOnNextCheck();
        }
    }

    [HarmonyPatch(typeof(MapDrawer), "DrawMapMesh")]
    public static class Patch_MapDrawer_OrbitalSupportMoteOverlay
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Map map = Find.CurrentMap;
            if (map == null)
                return;
            ThingDef airPatrolOrbiterDef = DefDatabase<ThingDef>.GetNamedSilentFail("PMP_GreatWyrmOrbiter");
            SingularitySupport.SingularityShipSupportWorker.DrawPreview();
            FlattenSupport.FlattenShipSupportWorker.DrawPreview();
            if (airPatrolOrbiterDef != null)
            {
                List<Thing> airList = map.listerThings.ThingsOfDef(airPatrolOrbiterDef);
                for (int i = 0; i < airList.Count; i++)
                {
                    if (airList[i] is Building_GreatWyrmOrbiter ap && ap.Spawned && !ap.Destroyed)
                        Building_GreatWyrmOrbiter.DrawGreatWyrmFormation(ap);
                }
            }
            // Orbital laser: draw thick line every frame while choosing end (tied to map draw).
            if (OrbitalLaserTargeter.IsActive && OrbitalLaserTargeter.Map == map)
                OrbitalLaserTargeter.Draw();
            if (EnergyFieldTargeting.Active && EnergyFieldTargeting.Map == map)
            {
                IntVec3 mouse = UI.MouseCell();
                if (mouse.InBounds(map))
                    EnergyFieldWorldDrawer.DrawAt(map, mouse);
            }

            if (SupernovaTargeting.Active && SupernovaTargeting.Map == map)
            {
                IntVec3 mouse = UI.MouseCell();
                if (mouse.InBounds(map))
                    GenDraw.DrawRadiusRing(mouse, SupernovaTargeting.RadiusCells);
            }

            if (RecomposerTargeting.Active && RecomposerTargeting.Map == map)
            {
                IntVec3 mouse = UI.MouseCell();
                if (mouse.InBounds(map))
                    GenDraw.DrawRadiusRing(mouse, RecomposerConstants.TargetRadius);
            }

            EnergyFieldWorldDrawer.DrawForMapIfPlayerField(map);
        }
    }

    [HarmonyPatch(typeof(Targeter), nameof(Targeter.StopTargeting))]
    public static class Patch_Targeter_StopTargeting_OrbitalLaserClear
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            OrbitalLaserTargeter.OnTargetingEnded();
            EnergyFieldTargeting.Clear();
            SupernovaTargeting.Clear();
            RecomposerTargeting.Clear();
            FlattenSupport.FlattenShipSupportWorker.TargetMap = null;
        }
    }

}
