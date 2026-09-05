using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_ReserveAmmo_Patch
{
    private static void Prefix(Weapon __instance)
    {
        if (GameModeManager.ShouldIgnoreGlobalWeaponSettingsFor(__instance))
        {
            return;
        }
        bool enabled = WeaponSettingsState.Enabled;
        WeaponAmmoTuning.ApplyToWeapon(__instance, enabled, WeaponSettingsState.SpareMagazines);
    }

    private static void Postfix(Weapon __instance)
    {
        if (GameModeManager.ShouldIgnoreGlobalWeaponSettingsFor(__instance))
        {
            return;
        }
        bool customReloading = __instance != null && WeaponAmmoTuning.IsReloading(__instance);
        if (!WeaponSettingsState.Enabled || __instance == null || !__instance.IsOwner || !__instance.needsAmmo || __instance.gameObject.layer != 8 || (!customReloading && __instance.reloadWeapon))
        {
            return;
        }

        int spareRounds = WeaponAmmoTuning.GetSpareRounds(__instance);
        int currentAmmo = customReloading ? 0 : (__instance.currentAmmo > 0 ? __instance.currentAmmo : 0);
        PauseManager.Instance.ChangeAmmoText(spareRounds.ToString(), currentAmmo + " / ", __instance.inRightHand);
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
internal static class PlayerPickup_WeaponAmmoPickup_Patch
{
    private static void Prefix(GameObject obj)
    {
        if (GameModeManager.ShouldIgnoreGlobalWeaponSettings || !WeaponSettingsState.Enabled || obj == null)
        {
            return;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        if (weapon == null || obj.transform.GetComponentInParent<ItemSpawner>() == null)
        {
            return;
        }

        WeaponAmmoTuning.InitializeFromSpawnerPickup(weapon, WeaponSettingsState.SpareMagazines);
    }
}

[HarmonyPatch(typeof(PauseManager), "InvokeRoundStarted")]
internal static class PauseManager_WeaponAmmoHudReset_Patch
{
    private static void Postfix()
    {
        if (GameModeManager.ShouldIgnoreGlobalWeaponSettings)
        {
            return;
        }
        WeaponAmmoTuning.ScheduleLocalAmmoHudRefresh();
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