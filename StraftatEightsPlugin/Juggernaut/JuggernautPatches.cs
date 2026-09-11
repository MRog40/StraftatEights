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
    private static readonly ConditionalWeakTable<Minigun, object> ConfiguredMiniguns = new();

    private static void Prefix(Minigun __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut) || __instance == null
            || !JuggernautState.IsCurrentJuggernautWeapon(__instance))
        {
            return;
        }

        __instance.reloadWeapon = false;
        __instance.ammoCharge = MagazineSize;
        WeaponAmmoTuning.PreventAutoDespawn(__instance);
        if (!ConfiguredMiniguns.TryGetValue(__instance, out _))
        {
            __instance.currentAmmo = MagazineSize;
            __instance.chargedBullets = 0f;
            ConfiguredMiniguns.Add(__instance, new object());
        }

        WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance, MagazineSize);
        WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
    }

    private static void Postfix(Minigun __instance)
    {
        if (GameModeManager.IsActive(GameMode.Juggernaut)
            && __instance != null
            && JuggernautState.IsCurrentJuggernautWeapon(__instance))
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
    }
}
