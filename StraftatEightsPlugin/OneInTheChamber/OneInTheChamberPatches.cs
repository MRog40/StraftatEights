using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PauseManager), "InvokeRoundStarted")]
internal static class PauseManager_OneInTheChamberRoundStart_Patch
{
    private static void Postfix()
    {
        OneInTheChamberState.OnRoundStarted();
    }
}

[HarmonyPatch(typeof(ItemSpawner), "Spawn")]
internal static class ItemSpawner_OneInTheChamber_Patch
{
    private static bool Prefix()
    {
        return !GameModeManager.IsActive(GameMode.OneInTheChamber);
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_OneInTheChamberSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.OneInTheChamber)
            || WeaponService.IsFinalGameScreen || __instance.player == null)
        {
            return;
        }

        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null)
        {
            OneInTheChamberState.RequestLoadout(client.PlayerId);
        }
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
internal static class PlayerPickup_OneInTheChamberWeapon_Patch
{
    private static bool Prefix(GameObject obj, bool rightHand)
    {
        if (!GameModeManager.IsActive(GameMode.OneInTheChamber) || obj == null)
        {
            return true;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        if (weapon == null)
        {
            return true;
        }

        return (OneInTheChamberState.IsPistol(weapon) && rightHand)
            || (OneInTheChamberState.IsCouperet(weapon) && !rightHand);
    }
}

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_OneInTheChamberAmmo_Patch
{
    private static void Postfix(Weapon __instance)
    {
        if (!GameModeManager.IsActive(GameMode.OneInTheChamber)
            || !OneInTheChamberState.IsPistol(__instance))
        {
            return;
        }

        PlayerHealth? health = __instance.playerController == null
            ? null
            : __instance.playerController.GetComponent<PlayerHealth>();
        int playerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
        if (playerId >= 0)
        {
            OneInTheChamberState.EnforcePistolAmmo(__instance, playerId);
        }
    }
}