using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_GuntatSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Guntat) || WeaponService.IsFinalGameScreen || __instance.player == null) return;
        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null) GuntatState.GiveStartingWeapon(client.PlayerId);
    }
}
