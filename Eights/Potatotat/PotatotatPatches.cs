using System;
using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_PotatotatSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Potatotat)
            || WeaponService.IsFinalGameScreen || __instance.player == null)
        {
            return;
        }

        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null)
        {
            PotatotatState.RequestLoadout(client.PlayerId);
        }
    }
}
[HarmonyPatch(typeof(HandGrenade), "HandleExplosion")]
internal static class HandGrenade_PotatotatDeath_Patch
{
    private static void Prefix()
    {
        PotatotatState.BeginGrenadeExplosion();
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        PotatotatState.EndGrenadeExplosion();
        return __exception;
    }
}

[HarmonyPatch(typeof(PlayerHealth), nameof(PlayerHealth.Explode))]
internal static class PlayerHealth_PotatotatGrenadeDeath_Patch
{
    private static void Prefix(PlayerHealth __instance)
    {
        PotatotatState.MarkGrenadeDeath(__instance);
    }
}
