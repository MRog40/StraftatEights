using HarmonyLib;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace StraftatEightsPlugin;

// Harmony patches for the Juggernaut game mode: kill/crown tracking, forced auto-respawn, and
// Juggernaut-only movement speed. See JuggernautState for the actual game-mode logic and
// JuggernautOutline for the visual outline effect.

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class GameManager_JuggernautTick_Patch
{
    private static void Postfix(GameManager __instance)
    {
        if (__instance.IsServer)
        {
            if (GameModeManager.IsActive(GameMode.Juggernaut))
            {
                JuggernautState.ServerTick(Time.deltaTime);
            }
        }
    }
}
[HarmonyPatch(typeof(Minigun), "Update")]
internal static class Minigun_JuggernautAmmo_Patch
{
    private const int MagazineSize = 100;
    private sealed class MinigunState
    {
        internal bool ReloadStarted;
    }

    private static readonly ConditionalWeakTable<Minigun, MinigunState> MinigunStates = new();

    private static void Prefix(Minigun __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut) || __instance == null
            || !JuggernautState.IsCurrentJuggernautWeapon(__instance))
        {
            return;
        }

        __instance.ammoCharge = MagazineSize;
        WeaponAmmoTuning.PreventAutoDespawn(__instance);
        if (!MinigunStates.TryGetValue(__instance, out MinigunState? state))
        {
            __instance.chargedBullets = MagazineSize;
            __instance.currentAmmo = MagazineSize;
            MinigunStates.Add(__instance, new MinigunState());
            return;
        }

        if (__instance.isReloading)
        {
            state.ReloadStarted = true;
            return;
        }

        if (state.ReloadStarted)
        {
            state.ReloadStarted = false;
            if (__instance.chargedBullets > 0 && __instance.currentAmmo <= 0)
            {
                __instance.currentAmmo = MagazineSize;
            }
        }
    }
}
