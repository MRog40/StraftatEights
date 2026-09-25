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
        if (!HuntModesState.IsTanktat)
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

    private static bool IsJuggertatMinigunFiring(FirstPersonController controller)
    {
        PlayerPickup? pickup = controller.playerPickupScript;
        GameObject? heldObject = pickup?.objInHand;
        Weapon? weapon = heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
        if (weapon == null
            || !weapon.name.StartsWith(JuggertatState.WeaponName, System.StringComparison.Ordinal))
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

        if (HuntModesState.IsTanktat)
        {
            return false;
        }

        return !JuggertatState.IsCurrentJuggertat(controller)
            && (GameModeManager.ShouldIgnoreGlobalMovementSettings || GlobalModifiersState.SlidingEnabled);
    }

    internal static bool CanRunJump(FirstPersonController controller)
    {
        if (GameModeManager.IsVanillaScene)
        {
            return true;
        }

        if (HuntModesState.IsTanktat)
        {
            controller.CanWallJump = false;
            return false;
        }

        if (JuggertatState.IsCurrentJuggertat(controller))
        {
            return false;
        }

        if (GameModeManager.IsActive(GameMode.Michaeltat))
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
            case GameMode.Tanktat:
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
            case GameMode.Michaeltat:
                if (MichaeltatState.IsMichael(controller))
                {
                    multiplier = MichaeltatState.MovementMultiplier;
                }
                break;
            case GameMode.Infectedtat:
                PlayerHealth? health = controller.GetComponent<PlayerHealth>();
                if (health != null && health && InfectedtatState.IsInfectedtat(health))
                {
                    multiplier = InfectedtatState.InfectedtatSpeedMultiplier;
                }
                break;
            case GameMode.PotatoInftat:
                PlayerHealth? hotPotInfectedtatHealth = controller.GetComponent<PlayerHealth>();
                if (hotPotInfectedtatHealth != null && hotPotInfectedtatHealth
                    && PotatoInftatState.IsInfectedtat(hotPotInfectedtatHealth))
                {
                    multiplier = PotatoInftatState.InfectedtatSpeedMultiplier;
                }
                break;
            case GameMode.Juggertat:
                if (JuggertatState.IsCurrentJuggertat(controller)
                    && IsJuggertatMinigunFiring(controller))
                {
                    multiplier = JuggertatState.MovementMultiplier;
                }
                break;
            case GameMode.Ratatat:
                if (RatatatState.IsRat(controller))
                {
                    multiplier = RatatatState.RatMovementMultiplier;
                }
                break;
            case GameMode.Infideltat:
                multiplier = InfideltatState.MovementMultiplier;
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
        if (HuntModesState.IsTanktat
            || (!GameModeManager.ShouldIgnoreGlobalMovementSettings && !GlobalModifiersState.WallJumpEnabled)
            || GameModeManager.IsActive(GameMode.Michaeltat))
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

        if (GameModeManager.IsActive(GameMode.Michaeltat))
        {
            __instance.CanWallJump = false;
        }

        if (JuggertatState.IsCurrentJuggertat(__instance))
        {
            __instance.isSprinting = false;
        }

        MovementPolicy.Apply(__instance);
    }

}

