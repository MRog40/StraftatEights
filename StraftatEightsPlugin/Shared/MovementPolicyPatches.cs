using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class MovementPolicy
{
    private static float GetBaselineMovementFactor(FirstPersonController controller)
    {
        if (GameModeManager.ShouldIgnoreGlobalMovementSettings)
        {
            return 1f;
        }

        float adsFactor = controller.isAiming ? GlobalModifiersState.AdsSpeedMultiplier : 1f;
        return GlobalModifiersState.SpeedMultiplier * adsFactor;
    }

    private static bool IsJuggernautMinigunFiring(FirstPersonController controller)
    {
        PlayerPickup? pickup = controller.playerPickupScript;
        GameObject? heldObject = pickup?.objInHand;
        Weapon? weapon = heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
        if (weapon == null
            || !weapon.name.StartsWith(JuggernautState.WeaponName, System.StringComparison.Ordinal))
        {
            return false;
        }

        var fireAction = weapon.invertFire ? weapon.fire2 : weapon.fire1;
        return fireAction != null && fireAction.ReadValue<float>() > 0.1f;
    }

    internal static bool CanSlide(FirstPersonController controller)
    {
        return !JuggernautState.IsCurrentJuggernaut(controller)
            && (GameModeManager.ShouldIgnoreGlobalMovementSettings || GlobalModifiersState.SlidingEnabled);
    }

    internal static bool CanRunJump(FirstPersonController controller)
    {
        if (JuggernautState.IsCurrentJuggernaut(controller))
        {
            return false;
        }

        if (GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            controller.CanWallJump = false;
        }
        return true;
    }

    internal static void Apply(FirstPersonController controller)
    {
        switch (GameModeManager.ActiveMode)
        {
            case GameMode.Juggernaut:
                if (JuggernautState.IsCurrentJuggernaut(controller))
                {
                    controller.movementFactor = IsJuggernautMinigunFiring(controller)
                        ? JuggernautState.MovementMultiplier
                        : 1f;
                }
                break;
            case GameMode.KillTheRat:
                ApplyRatMovement(controller);
                break;
            case GameMode.MichaelMeyers:
                controller.movementFactor = MichaelMeyersState.IsMichael(controller)
                    ? MichaelMeyersState.MovementMultiplier
                    : 1f;
                break;
            case GameMode.Infidel:
                controller.movementFactor = InfidelState.MovementMultiplier;
                break;
        }
    }

    internal static void ApplyRatMovement(FirstPersonController controller)
    {
        if (!KillTheRatState.IsRat(controller))
        {
            return;
        }

        float baselineMovementFactor = GetBaselineMovementFactor(controller);
        controller.movementFactor = controller.isSprinting
            ? baselineMovementFactor * KillTheRatState.RatMovementMultiplier
            : baselineMovementFactor;

        if (controller.IsOwner)
        {
            DebugLog.Every("kill-the-rat-movement", 5f,
                $"KillTheRat local rat speed applied rat={KillTheRatState.CurrentRatPlayerId} "
                + $"sprinting={controller.isSprinting} movementFactor={controller.movementFactor:0.00}");
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Slide")]
internal static class FirstPersonController_SlidePolicy_Patch
{
    private static bool Prefix(FirstPersonController __instance)
    {
        return MovementPolicy.CanSlide(__instance);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Jump")]
[HarmonyPriority(Priority.Last)]
internal static class FirstPersonController_JumpPolicy_Patch
{
    private static bool Prefix(FirstPersonController __instance)
    {
        return MovementPolicy.CanRunJump(__instance);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "OnControllerColliderHit")]
internal static class FirstPersonController_WallJumpPolicy_Patch
{
    private static void Postfix(FirstPersonController __instance)
    {
        if ((!GameModeManager.ShouldIgnoreGlobalMovementSettings && !GlobalModifiersState.WallJumpEnabled)
            || GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            __instance.CanWallJump = false;
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Update")]
[HarmonyPriority(Priority.Last)]
internal static class FirstPersonController_MovementPolicy_Patch
{
    private static void Prefix(FirstPersonController __instance)
    {
        if (GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            __instance.CanWallJump = false;
        }

        if (JuggernautState.IsCurrentJuggernaut(__instance))
        {
            __instance.isSprinting = false;
        }

        if (GameModeManager.IsActive(GameMode.KillTheRat))
        {
            MovementPolicy.ApplyRatMovement(__instance);
        }
    }

    private static void Postfix(FirstPersonController __instance)
    {
        MovementPolicy.Apply(__instance);
    }
}

