using HarmonyLib;
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
internal static class Minigun_JuggernautAmmoDisplay_Patch
{
    private static void Postfix(Minigun __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut) || __instance == null
            || !__instance.name.StartsWith(JuggernautState.WeaponName, System.StringComparison.Ordinal))
        {
            return;
        }

        __instance.currentAmmo = 1;
        if (__instance.IsOwner && PauseManager.Instance != null)
        {
            PauseManager.Instance.ChangeAmmoText("1", __instance.chargedBullets + " / ", __instance.inRightHand);
        }
    }
}
[HarmonyPatch(typeof(Minigun), "Reload")]
internal static class Minigun_JuggernautReload_Patch
{
    private static bool Prefix(Minigun __instance)
    {
        return !GameModeManager.IsActive(GameMode.Juggernaut)
            || __instance == null
            || !__instance.name.StartsWith(JuggernautState.WeaponName, System.StringComparison.Ordinal);
    }
}
