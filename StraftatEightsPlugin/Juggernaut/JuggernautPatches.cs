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
    private const int MagazineSize = 150;
    private static readonly ConditionalWeakTable<Minigun, object> ConfiguredMiniguns = new();

    private static void Prefix(Minigun __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut) || __instance == null
            || !__instance.name.StartsWith(JuggernautState.WeaponName, System.StringComparison.Ordinal))
        {
            return;
        }

        if (ConfiguredMiniguns.TryGetValue(__instance, out _))
        {
            return;
        }

        __instance.ammoCharge = MagazineSize;
        __instance.chargedBullets = MagazineSize;
        __instance.currentAmmo = MagazineSize;
        ConfiguredMiniguns.Add(__instance, new object());
    }
}
