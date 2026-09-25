using HarmonyLib;
using UnityEngine;

namespace Eights;

internal static class MovementPolicy
{
    private static readonly System.Reflection.FieldInfo? CrouchPressField =
        AccessTools.Field(typeof(FirstPersonController), "crouchPress");
    private static readonly System.Reflection.FieldInfo? SlideSprintingField =
        AccessTools.Field(typeof(FirstPersonController), "isSlideSprinting");

    internal static void ForceTankState(FirstPersonController controller)
    {
        if (!HuntersState.IsTankBattle)
        {
            return;
        }

        controller.isCrouching = true;
        controller.isSliding = false;
        controller.isSprinting = false;
        controller.CanWallJump = false;
        CrouchPressField?.SetValue(controller, true);
        SlideSprintingField?.SetValue(controller, false);
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
        if (GameModeManager.IsVanillaScene)
        {
            return true;
        }

        if (HuntersState.IsTankBattle)
        {
            return false;
        }

        return !JuggernautState.IsCurrentJuggernaut(controller)
            && (GameModeManager.ShouldIgnoreGlobalMovementSettings || GlobalModifiersState.SlidingEnabled);
    }

    internal static bool CanRunJump(FirstPersonController controller)
    {
        if (GameModeManager.IsVanillaScene)
        {
            return true;
        }

        if (HuntersState.IsTankBattle)
        {
            controller.CanWallJump = false;
            return false;
        }

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
        if (GameModeManager.IsVanillaScene)
        {
            return;
        }

        switch (GameModeManager.ActiveMode)
        {
            case GameMode.TankBattle:
                ForceTankState(controller);
                controller.movementFactor = 1f;
                break;
        }
    }

    internal static void ApplyPerPlayerSpeed(FirstPersonController controller)
    {
        if (GameModeManager.IsVanillaScene)
        {
            return;
        }

        float multiplier = 1f;
        switch (GameModeManager.ActiveMode)
        {
            case GameMode.MichaelMeyers:
                if (MichaelMeyersState.IsMichael(controller))
                {
                    multiplier = MichaelMeyersState.MovementMultiplier;
                }
                break;
            case GameMode.Infected:
                PlayerHealth? health = controller.GetComponent<PlayerHealth>();
                if (health != null && health && InfectedState.IsInfected(health))
                {
                    multiplier = InfectedState.InfectedSpeedMultiplier;
                }
                break;
            case GameMode.HotPotInfected:
                PlayerHealth? hotPotInfectedHealth = controller.GetComponent<PlayerHealth>();
                if (hotPotInfectedHealth != null && hotPotInfectedHealth
                    && HotPotInfectedState.IsInfected(hotPotInfectedHealth))
                {
                    multiplier = HotPotInfectedState.InfectedSpeedMultiplier;
                }
                break;
            case GameMode.Juggernaut:
                if (JuggernautState.IsCurrentJuggernaut(controller)
                    && IsJuggernautMinigunFiring(controller))
                {
                    multiplier = JuggernautState.MovementMultiplier;
                }
                break;
            case GameMode.KillTheRat:
                if (KillTheRatState.IsRat(controller))
                {
                    multiplier = KillTheRatState.RatMovementMultiplier;
                }
                break;
            case GameMode.Infidel:
                multiplier = InfidelState.MovementMultiplier;
                break;
        }

        if (Mathf.Approximately(multiplier, 1f))
        {
            return;
        }

        Vector3 movement = controller.moveDirection;
        movement.x *= multiplier;
        movement.z *= multiplier;
        controller.moveDirection = movement;
    }
}

[HarmonyPatch(typeof(FirstPersonController), "ApplyFinalMovements")]
[HarmonyPriority(Priority.Last)]
internal static class FirstPersonController_PerPlayerSpeed_Patch
{
    private static void Prefix(FirstPersonController __instance)
    {
        MovementPolicy.ApplyPerPlayerSpeed(__instance);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "HandleCrouch")]
internal static class FirstPersonController_TankCrouch_Patch
{
    private static void Prefix(FirstPersonController __instance)
    {
        MovementPolicy.ForceTankState(__instance);
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
        if (HuntersState.IsTankBattle
            || (!GameModeManager.ShouldIgnoreGlobalMovementSettings && !GlobalModifiersState.WallJumpEnabled)
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
        MovementPolicy.ForceTankState(__instance);

        if (GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            __instance.CanWallJump = false;
        }

        if (JuggernautState.IsCurrentJuggernaut(__instance))
        {
            __instance.isSprinting = false;
        }

        MovementPolicy.Apply(__instance);
    }

}

