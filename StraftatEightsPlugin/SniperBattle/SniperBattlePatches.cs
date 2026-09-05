using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_SniperBattleSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.SniperBattle) || WeaponService.IsFinalGameScreen || __instance.player == null)
        {
            return;
        }

        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null)
        {
            SniperBattleState.GiveStartingWeapon(client.PlayerId);
        }
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
internal static class PlayerPickup_SniperBattleWeapon_Patch
{
    private static bool Prefix(GameObject obj)
    {
        if (!GameModeManager.IsActive(GameMode.SniperBattle) || obj == null)
        {
            return true;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        return weapon == null || SniperBattleState.IsSniperWeapon(weapon);
    }
}

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_SniperBattleUnlimitedAmmo_Patch
{
    private static void Prefix(Weapon __instance)
    {
        if (!GameModeManager.IsActive(GameMode.SniperBattle) || !SniperBattleState.IsSniperWeapon(__instance)
            || !__instance.needsAmmo || __instance.gameObject.layer != 8)
        {
            return;
        }

        if (__instance.currentAmmo <= 0)
        {
            __instance.currentAmmo = 1;
            __instance.cantTakeSafeBool = false;
            __instance.noAmmoClicks = 0;
        }
    }
}