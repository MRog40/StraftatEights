using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class GameManager_KillTheRatTick_Patch
{
    private static void Postfix(GameManager __instance)
    {
        if (__instance.IsServer)
        {
            KillTheRatState.ServerTick(Time.unscaledDeltaTime);
        }
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_KillTheRatSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.KillTheRat) || WeaponService.IsFinalGameScreen
            || __instance.player == null)
        {
            return;
        }

        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null)
        {
            KillTheRatState.RequestLoadout(client.PlayerId);
        }
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
internal static class PlayerPickup_KillTheRatWeapon_Patch
{
    private static bool Prefix(PlayerPickup __instance, GameObject obj)
    {
        if (!GameModeManager.IsActive(GameMode.KillTheRat) || obj == null)
        {
            return true;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        if (weapon == null)
        {
            return true;
        }

        PlayerHealth? health = __instance.GetComponent<PlayerHealth>();
        if (health == null)
        {
            return true;
        }

        return KillTheRatState.IsRat(health)
            ? KillTheRatState.IsRatWeapon(weapon)
            : KillTheRatState.IsHumanWeapon(weapon);
    }
}

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_KillTheRatUnlimitedGlock_Patch
{
    private static void Postfix(Weapon __instance)
    {
        if (!GameModeManager.IsActive(GameMode.KillTheRat)
            || !KillTheRatState.IsHumanWeapon(__instance))
        {
            return;
        }

        WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
        WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Update")]
[HarmonyPriority(Priority.Last)]
internal static class FirstPersonController_KillTheRatSpeed_Patch
{
    private static void Postfix(FirstPersonController __instance)
    {
        if (GameModeManager.IsActive(GameMode.KillTheRat)
            && KillTheRatState.IsRat(__instance))
        {
            __instance.movementFactor *= KillTheRatState.RatMovementMultiplier;
        }
    }
}