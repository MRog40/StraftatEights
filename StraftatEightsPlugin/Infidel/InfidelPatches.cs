using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PauseManager), "InvokeRoundStarted")]
internal static class PauseManager_InfidelRoundStart_Patch
{
    private static void Postfix()
    {
        InfidelState.OnRoundStarted();
    }
}

[HarmonyPatch(typeof(ItemSpawner), "Spawn")]
internal static class ItemSpawner_Infidel_Patch
{
    private static bool Prefix()
    {
        return !GameModeManager.IsActive(GameMode.Infidel);
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_InfidelSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Infidel)
            || WeaponService.IsFinalGameScreen || __instance.player == null)
        {
            return;
        }

        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null)
        {
            InfidelState.RequestHealthReset(client.PlayerId);
            InfidelState.RequestLoadout(client.PlayerId);
        }
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
internal static class PlayerPickup_InfidelWeapon_Patch
{
    private static bool Prefix(PlayerPickup __instance, GameObject obj)
    {
        if (!GameModeManager.IsActive(GameMode.Infidel) || obj == null)
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
        return playerId < 0 || InfidelState.IsAllowedWeapon(weapon, playerId);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Update")]
[HarmonyPriority(Priority.Last)]
internal static class FirstPersonController_InfidelMovement_Patch
{
    private static void Postfix(FirstPersonController __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Infidel))
        {
            return;
        }

        __instance.movementFactor = InfidelState.MovementMultiplier;
        __instance.CanWallJump = false;
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Jump")]
internal static class FirstPersonController_InfidelJump_Patch
{
    private static void Prefix(FirstPersonController __instance)
    {
        if (GameModeManager.IsActive(GameMode.Infidel))
        {
            __instance.CanWallJump = false;
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), "OnControllerColliderHit")]
internal static class FirstPersonController_InfidelWallCollision_Patch
{
    private static void Postfix(FirstPersonController __instance)
    {
        if (GameModeManager.IsActive(GameMode.Infidel))
        {
            __instance.CanWallJump = false;
        }
    }
}