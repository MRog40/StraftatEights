using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class FirstPersonController_RatatatVoid_Patch
{
    private static bool Prefix(FirstPersonController __instance)
    {
        return !RatatatState.HandleHumanVoidFall(__instance);
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_RatatatSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Ratatat) || WeaponService.IsFinalGameScreen
            || __instance.player == null)
        {
            return;
        }

        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null)
        {
            RatatatState.RequestLoadout(client.PlayerId);
        }
    }
}
