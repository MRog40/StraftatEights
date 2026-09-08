using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PauseManager), "InvokeRoundStarted")]
internal static class PauseManager_HotPotatoRoundStart_Patch
{
    private static void Postfix()
    {
        HotPotatoState.OnRoundStarted();
    }
}

[HarmonyPatch(typeof(ItemSpawner), "Spawn")]
internal static class ItemSpawner_HotPotato_Patch
{
    private static bool Prefix()
    {
        return !GameModeManager.IsActive(GameMode.HotPotato);
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_HotPotatoSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.HotPotato)
            || WeaponService.IsFinalGameScreen || __instance.player == null)
        {
            return;
        }

        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null)
        {
            HotPotatoState.RequestLoadout(client.PlayerId);
        }
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
internal static class PlayerPickup_HotPotatoWeapon_Patch
{
    private static bool Prefix(PlayerPickup __instance, GameObject obj)
    {
        if (!GameModeManager.IsActive(GameMode.HotPotato) || obj == null)
        {
            return true;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        if (weapon == null)
        {
            return true;
        }

        PlayerHealth? health = __instance.GetComponent<PlayerHealth>();
        int playerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
        return playerId < 0 || HotPotatoState.IsAllowedWeapon(weapon, playerId);
    }
}