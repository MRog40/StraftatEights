using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class GameManager_HVTTick_Patch
{
    private static void Postfix(GameManager __instance)
    {
        if (__instance.IsServer)
        {
            HVTState.ServerTick(Time.unscaledDeltaTime);
        }
    }
}
