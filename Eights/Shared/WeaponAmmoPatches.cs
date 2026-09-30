using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace Eights;

[HarmonyPatch]
internal static class Weapon_AmmoInitialization_Patch
{
    private static MethodBase? TargetMethod()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return typeof(Weapon).GetMethod("Awake___UserLogic", flags)
            ?? typeof(Weapon).GetMethod("Awake", flags);
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(Weapon __instance)
    {
        WeaponAmmoTuning.CaptureMagazineSize(__instance);
    }
}

[HarmonyPatch(typeof(Weapon), "DespawnObject")]
internal static class Weapon_DespawnWithSpareRounds_Patch
{
    private static bool Prefix(Weapon __instance)
    {
        if (__instance == null || !__instance)
        {
            return true;
        }

        bool ammoTuningEnabled = GameModeManager.UsesTeamWeaponLoadouts
            || WeaponSettingsState.Enabled;
        if (WeaponAmmoTuning.RestoreDroppedWeaponAmmo(__instance, ammoTuningEnabled,
                WeaponSettingsState.SpareMagazines))
        {
            return false;
        }

        return !WeaponDespawnRules.ShouldPreserveDroppedWeapon(
            __instance.gameObject.layer == 7, __instance.needsAmmo,
            __instance.currentAmmo, WeaponAmmoTuning.HasSpareRounds(__instance));
    }
}

[HarmonyPatch]
internal static class Weapon_DroppedDespawnServer_Patch
{
    private static MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(Weapon),
            "RpcLogic___DespawnObjectServer_",
            method => method.ReturnType == typeof(void)
                && method.GetParameters().Length == 0);
    }

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(Weapon __instance)
    {
        bool restored = WeaponAmmoTuning.RestoreDroppedWeaponAmmo(
            __instance, GameModeManager.UsesTeamWeaponLoadouts || WeaponSettingsState.Enabled,
            WeaponSettingsState.SpareMagazines);
        return !restored;
    }
}

[HarmonyPatch(typeof(PlayerPickup), "HandleInteraction")]
internal static class PlayerPickup_EmptyWeaponWithReserve_Patch
{
    private sealed class State
    {
        internal Weapon Weapon = null!;
        internal int CurrentAmmo;
        internal bool CantTakeSafe;
    }

    private static void Prefix(PlayerPickup __instance, out State? __state)
    {
        __state = null;
        if (__instance == null || !__instance || !__instance.IsOwner
            || __instance.currentInteractable == null || !__instance.currentInteractable)
        {
            return;
        }

        Weapon? weapon = __instance.currentInteractable.GetComponent<Weapon>();
        if (weapon == null || !weapon || weapon.gameObject.layer != 7
            || weapon.currentAmmo > 0 || !WeaponAmmoTuning.HasSpareRounds(weapon))
        {
            return;
        }

        __state = new State
        {
            Weapon = weapon,
            CurrentAmmo = weapon.currentAmmo,
            CantTakeSafe = weapon.cantTakeSafeBool
        };
        weapon.currentAmmo = 1;
        weapon.cantTakeSafeBool = false;
    }

    private static void Postfix(State? __state)
    {
        if (__state == null || __state.Weapon == null || !__state.Weapon)
        {
            return;
        }

        __state.Weapon.currentAmmo = __state.CurrentAmmo;
        __state.Weapon.cantTakeSafeBool = __state.CantTakeSafe;
    }
}

[HarmonyPatch(typeof(PauseManager), "MoveAmmoDisplay")]
internal static class PauseManager_RemotePlayerHudCleanup_Patch
{
    private static bool Prefix()
    {
        return !PlayerSetup_WeaponAmmoHudReset_Patch.SuppressRemoteHudCleanup;
    }
}

[HarmonyPatch(typeof(PauseManager), "ChangeAmmoText")]
internal static class PauseManager_RemotePlayerAmmoTextCleanup_Patch
{
    private static bool Prefix()
    {
        return !PlayerSetup_WeaponAmmoHudReset_Patch.SuppressRemoteHudCleanup;
    }
}

[HarmonyPatch]
internal static class PlayerSetup_WeaponAmmoHudReset_Patch
{
    [ThreadStatic]
    internal static bool SuppressRemoteHudCleanup;

    private static MethodBase? TargetMethod()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return typeof(PlayerSetup).GetMethod("OnDisable___UserLogic", flags)
            ?? typeof(PlayerSetup).GetMethod("OnDisable", flags);
    }

    private static void Prefix(PlayerSetup __instance)
    {
        SuppressRemoteHudCleanup = __instance != null && !__instance.IsOwner;
    }

    private static void Postfix(PlayerSetup __instance)
    {
        bool isOwner = __instance != null && __instance.IsOwner;
        SuppressRemoteHudCleanup = false;
        if (isOwner)
        {
            WeaponAmmoTuning.ScheduleLocalAmmoHudRefresh();
        }
    }
}

[HarmonyPatch]
internal static class PlayerSetup_LocalHudRestore_Patch
{
    private static MethodBase? TargetMethod()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return typeof(PlayerSetup).GetMethod("OnStartClient___UserLogic", flags)
            ?? typeof(PlayerSetup).GetMethod("OnStartClient", flags);
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(PlayerSetup __instance)
    {
        if (__instance != null && __instance.IsOwner)
        {
            if (!GameModeManager.ShouldHideCustomHud)
            {
                __instance.HideHUD(false);
            }
            WeaponAmmoTuning.ScheduleLocalAmmoHudRefresh();
        }
    }
}