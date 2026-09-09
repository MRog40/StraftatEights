using HarmonyLib;

namespace StraftatEightsPlugin;

internal static class MovementPolicy
{
    internal static bool CanSlide(FirstPersonController controller)
    {
        return !GameModeManager.IsActive(GameMode.MichaelMeyers)
            && !JuggernautState.IsCurrentJuggernaut(controller)
            && GlobalModifiersState.SlidingEnabled;
    }

    internal static bool CanRunJump(FirstPersonController controller)
    {
        if (JuggernautState.IsCurrentJuggernaut(controller))
        {
            return false;
        }

        if (GameModeManager.IsActive(GameMode.MichaelMeyers)
            || GameModeManager.IsActive(GameMode.Infidel))
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
                    controller.movementFactor = JuggernautState.MovementMultiplier;
                }
                break;
            case GameMode.KillTheRat:
                if (KillTheRatState.IsRat(controller))
                {
                    controller.movementFactor = KillTheRatState.RatMovementMultiplier;
                }
                break;
            case GameMode.MichaelMeyers:
                controller.movementFactor = MichaelMeyersState.IsMichael(controller)
                    ? MichaelMeyersState.MovementMultiplier
                    : 1f;
                break;
            case GameMode.Infidel:
                controller.movementFactor = InfidelState.MovementMultiplier;
                controller.CanWallJump = false;
                break;
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

[HarmonyPatch(typeof(FirstPersonController), "HandleSlide")]
internal static class FirstPersonController_HandleSlidePolicy_Patch
{
    private static bool Prefix()
    {
        return !GameModeManager.IsActive(GameMode.MichaelMeyers);
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
        if (!GlobalModifiersState.WallJumpEnabled
            || GameModeManager.IsActive(GameMode.MichaelMeyers)
            || GameModeManager.IsActive(GameMode.Infidel))
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
            __instance.isSliding = false;
            __instance.isCrouching = false;
        }
    }

    private static void Postfix(FirstPersonController __instance)
    {
        MovementPolicy.Apply(__instance);
    }
}

