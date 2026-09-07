using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_GunGameSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.GunGame) || WeaponService.IsFinalGameScreen || __instance.player == null) return;
        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null) GunGameState.GiveStartingWeapon(client.PlayerId);
    }
}

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_GunGameUnlimitedAmmo_Patch
{
    private static void Postfix(Weapon __instance)
    {
        if (GameModeManager.IsActive(GameMode.GunGame) && GunGameState.Enabled)
        {
            WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
            WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
        }
    }
}