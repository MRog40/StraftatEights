using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PauseManager), "InvokeRoundStarted")]
internal static class PauseManager_MichaelMeyersRoundStart_Patch
{
    private static void Postfix()
    {
        MichaelMeyersState.OnRoundStarted();
    }
}

[HarmonyPatch(typeof(GameManager), "ResetGame")]
internal static class GameManager_MichaelMeyersReset_Patch
{
    private static void Postfix()
    {
        MichaelMeyersState.ResetMatchState();
    }
}

[HarmonyPatch(typeof(ItemSpawner), "Spawn")]
internal static class ItemSpawner_MichaelMeyers_Patch
{
    private static bool Prefix()
    {
        return !GameModeManager.IsActive(GameMode.MichaelMeyers);
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
internal static class PlayerPickup_MichaelMeyersWeapon_Patch
{
    private static bool Prefix(PlayerPickup __instance, GameObject obj)
    {
        if (!GameModeManager.IsActive(GameMode.MichaelMeyers) || obj == null)
        {
            return true;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        if (weapon == null)
        {
            return true;
        }

        PlayerHealth? health = __instance.GetComponent<PlayerHealth>();
        return health != null && MichaelMeyersState.CanHoldCouperet(health) && MichaelMeyersState.IsCouperet(weapon);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class FirstPersonController_MichaelMeyersMovement_Patch
{
    private static void Prefix(FirstPersonController __instance)
    {
        if (!GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            return;
        }

        __instance.CanWallJump = false;
        __instance.isSliding = false;
        __instance.isCrouching = false;
        __instance.forceAdded = Vector3.zero;
        __instance.force = Vector3.zero;
        __instance.forceFactor = 0f;
    }

    private static void Postfix(FirstPersonController __instance)
    {
        if (GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            __instance.movementFactor = MichaelMeyersState.IsMichael(__instance)
                ? MichaelMeyersState.MovementMultiplier
                : 1f;
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Slide")]
internal static class FirstPersonController_MichaelMeyersSlide_Patch
{
    private static bool Prefix()
    {
        return !GameModeManager.IsActive(GameMode.MichaelMeyers);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "HandleSlide")]
internal static class FirstPersonController_MichaelMeyersHandleSlide_Patch
{
    private static bool Prefix()
    {
        return !GameModeManager.IsActive(GameMode.MichaelMeyers);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Jump")]
internal static class FirstPersonController_MichaelMeyersWallJump_Patch
{
    private static void Prefix(FirstPersonController __instance)
    {
        if (GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            __instance.CanWallJump = false;
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), "OnControllerColliderHit")]
internal static class FirstPersonController_MichaelMeyersWallCollision_Patch
{
    private static void Postfix(FirstPersonController __instance)
    {
        if (GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            __instance.CanWallJump = false;
        }
    }
}

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_MichaelMeyersMovement_Patch
{
    private static void Postfix(Weapon __instance)
    {
        if (!GameModeManager.IsActive(GameMode.MichaelMeyers) || __instance.playerController == null)
        {
            return;
        }

        __instance.playerController.movementFactor = MichaelMeyersState.IsMichael(__instance.playerController)
            ? MichaelMeyersState.MovementMultiplier
            : 1f;
    }
}
