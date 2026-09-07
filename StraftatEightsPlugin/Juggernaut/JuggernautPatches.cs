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
            JuggernautState.PeriodicPushSettingsIfHost();
        }
        JuggernautOutline.EnforceOutline();
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Update")]
[HarmonyPriority(Priority.Last)]
internal static class FirstPersonController_JuggernautSpeed_Patch
{
    // Runs after GlobalModifiers' own Update prefix sets movementFactor for this frame, then layers
    // the Juggernaut speed boost on top for whoever currently holds the crown - takes effect starting
    // next frame, same as every other per-frame tuning value in this project.
    private static void Postfix(FirstPersonController __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut) || JuggernautState.CurrentJuggernautPlayerId < 0)
        {
            return;
        }

        PlayerHealth health = __instance.GetComponent<PlayerHealth>();
        if (health == null || health.playerValues == null || health.playerValues.playerClient == null)
        {
            return;
        }
        if (health.playerValues.playerClient.PlayerId != JuggernautState.CurrentJuggernautPlayerId)
        {
            return;
        }

        __instance.movementFactor = JuggernautState.MovementMultiplier;
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Jump")]
internal static class FirstPersonController_JuggernautJump_Patch
{
    private static bool Prefix(FirstPersonController __instance)
    {
        return !JuggernautState.IsCurrentJuggernaut(__instance);
    }
}

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_JuggernautMinigunAmmo_Patch
{
    private static void Prefix(Weapon __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut) || !JuggernautState.IsCurrentJuggernautWeapon(__instance)
            || !__instance.needsAmmo)
        {
            return;
        }

        __instance.currentAmmo = 1;
        __instance.chargedBullets = Mathf.Max(1, __instance.ammoCharge);
    }

    private static void Postfix(Weapon __instance)
    {
        if (__instance.inRightHand && JuggernautState.IsCurrentJuggernaut(__instance.playerController))
        {
            __instance.playerController.movementFactor = JuggernautState.MovementMultiplier;
        }
    }
}

[HarmonyPatch(typeof(Minigun), "Update")]
internal static class Minigun_JuggernautAmmoDisplay_Patch
{
    private static void Postfix(Minigun __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut)
            || !JuggernautState.IsCurrentJuggernautWeapon(__instance))
        {
            return;
        }

        __instance.currentAmmo = 0;
        if (__instance.IsOwner && PauseManager.Instance != null)
        {
            PauseManager.Instance.ChangeAmmoText("0", __instance.chargedBullets + " / ", __instance.inRightHand);
        }
    }
}

[HarmonyPatch(typeof(Minigun), "Reload")]
internal static class Minigun_JuggernautReload_Patch
{
    private static bool Prefix(Minigun __instance)
    {
        return !GameModeManager.IsActive(GameMode.Juggernaut)
            || !JuggernautState.IsCurrentJuggernautWeapon(__instance);
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
internal static class PlayerPickup_JuggernautWeapon_Patch
{
    private static bool Prefix(PlayerPickup __instance, GameObject obj)
    {
        if (!GameModeManager.IsActive(GameMode.Juggernaut) || obj == null)
        {
            return true;
        }

        PlayerHealth? health = __instance.GetComponent<PlayerHealth>();
        Weapon? weapon = obj.GetComponent<Weapon>();
        return health == null || !JuggernautState.IsCurrentJuggernaut(health)
            || weapon == null || weapon.name.StartsWith(JuggernautState.WeaponName, System.StringComparison.Ordinal);
    }
}
