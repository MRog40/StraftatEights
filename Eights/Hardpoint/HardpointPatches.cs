using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class GameManager_HardpointTick_Patch
{
    private static void Postfix(GameManager __instance)
    {
        if (__instance.IsServer && GameModeManager.IsActive(GameMode.Hardpoint))
        {
            HardpointState.ServerTick(Time.deltaTime);
        }
    }
}