using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_GunGameSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.GunGame) || __instance.player == null) return;
        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null) GunGameState.GiveStartingWeapon(client.PlayerId);
    }
}