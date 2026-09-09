using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace StraftatEightsPlugin;

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