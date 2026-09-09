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
