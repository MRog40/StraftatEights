using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class FirstPersonController_KillTheRatVoid_Patch
{
    private static bool Prefix(FirstPersonController __instance)
    {
        return !KillTheRatState.HandleHumanVoidFall(__instance);
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
