using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

// Harmony patches that enforce GlobalModifiersState on the actual game objects

[HarmonyPatch(typeof(GameManager), "Update")]
internal static class GameManager_GlobalModifiersTick_Patch
{
    // Keeps resending settings while hosting, in case an earlier one-shot broadcast was silently
    // dropped by a flaky Mycelium P2P session - see GlobalModifiersState.PeriodicPushIfHost
    private static void Postfix(GameManager __instance)
    {
        if (__instance.IsServer)
        {
            GlobalModifiersState.PeriodicPushIfHost();
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Slide")]
internal static class FirstPersonController_Slide_Patch
{
    // Blocks the crouch-slide input handler entirely while sliding is disabled
    private static bool Prefix()
    {
        return GlobalModifiersState.SlidingEnabled;
    }
}

[HarmonyPatch(typeof(FirstPersonController), "OnControllerColliderHit")]
internal static class FirstPersonController_WallJump_Patch
{
    // The base game sets CanWallJump on every wall collision, so we clear it right after
    private static void Postfix(FirstPersonController __instance)
    {
        if (!GlobalModifiersState.WallJumpEnabled)
        {
            __instance.CanWallJump = false;
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), "HandleAddingForce")]
internal static class FirstPersonController_SlideBoost_Patch
{
    // The final horizontal move each frame is moveDirection + forceAdded combined (see
    // ApplyFinalMovements), and moveDirection alone never exceeds crouchSpeed while sliding - so
    // rescaling just the slide impulse undershoots or overshoots depending on moveDirection's
    // contribution that frame. Clamping the actual combined vector here guarantees sprint speed is
    // the true peak speed when the boost is disabled, decaying down from there like a normal slide.
    private static void Postfix(FirstPersonController __instance)
    {
        if (GlobalModifiersState.SlideBoostEnabled || !__instance.isSliding)
        {
            return;
        }

        Vector3 moveHorizontal = new(__instance.moveDirection.x, 0f, __instance.moveDirection.z);
        Vector3 combined = moveHorizontal + __instance.forceAdded;
        float cap = MovementTuning.GetSprintSpeed(__instance);
        float magnitude = combined.magnitude;
        if (magnitude > cap && magnitude > 0.0001f)
        {
            __instance.forceAdded = combined * (cap / magnitude) - moveHorizontal;
        }
    }
}

[HarmonyPatch(typeof(FirstPersonController), "BForce")]
internal static class FirstPersonController_WallJumpBoostTrack_Patch
{
    // BForce is shared by many effects (ceiling bounce, knockback, taser...) - only the wall jump's
    // call happens while CanWallJump is still true (Jump() clears it right after), so that's how we
    // tell this specific invocation apart from the others
    private static void Prefix(FirstPersonController __instance)
    {
        MovementTuning.MarkWallJumpBForce(__instance, __instance.CanWallJump);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "HandleBForce")]
internal static class FirstPersonController_WallJumpBoost_Patch
{
    // Same idea as the slide boost clamp: cap the combined moveDirection + bforcefinal vector to
    // sprint speed instead of letting the wall-jump kick stack additively on top and exceed it
    private static void Postfix(FirstPersonController __instance)
    {
        if (GlobalModifiersState.WallJumpBoostEnabled)
        {
            return;
        }
        MovementTuning.ClampWallJumpBoost(__instance, MovementTuning.GetSprintSpeed(__instance));
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class FirstPersonController_Speed_Patch
{
    private static float _nextOwnerLogTime;

    // movementFactor is a plain per-client multiplier read every frame for move speed
    private static void Prefix(FirstPersonController __instance)
    {
        float adsFactor = __instance.isAiming ? GlobalModifiersState.AdsSpeedMultiplier : 1f;
        __instance.movementFactor = GlobalModifiersState.SpeedMultiplier * adsFactor;
        __instance.gravityMultiplier = GlobalModifiersState.GravityMultiplier;

        // Throttled proof-of-life log for the local player only: confirms what this specific client
        // is actually enforcing every frame, regardless of what was sent/received earlier
        if (__instance.IsOwner && UnityEngine.Time.unscaledTime >= _nextOwnerLogTime)
        {
            _nextOwnerLogTime = UnityEngine.Time.unscaledTime + 5f;
            Plugin.Logger.LogInfo($"[GlobalModifiers] Enforcing on local player: movementFactor={__instance.movementFactor:0.00} (SpeedMultiplier={GlobalModifiersState.SpeedMultiplier:0.00}, Enabled={GlobalModifiersState.Enabled})");
        }

        // Cheap version check skips the reflection work on every frame where nothing changed,
        // while still applying live edits instantly (no need to wait for a respawn)
        MovementTuning.ApplyTuningIfChanged(__instance, GlobalModifiersState.MomentumPercent, GlobalModifiersState.AirSpeedRatioPercent, GlobalModifiersState.TuningVersion);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Awake___UserLogic")]
internal static class FirstPersonController_Tuning_Patch
{
    // Initial apply at spawn; Update's version check picks up any later live edits
    private static void Postfix(FirstPersonController __instance)
    {
        MovementTuning.ApplyTuningIfChanged(__instance, GlobalModifiersState.MomentumPercent, GlobalModifiersState.AirSpeedRatioPercent, GlobalModifiersState.TuningVersion);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "HandleMovementInput")]
internal static class FirstPersonController_Momentum_Patch
{
    // Carries momentum through camera-driven direction changes (e.g. a mouse 180), not just WASD releases
    private static void Postfix(FirstPersonController __instance)
    {
        MovementTuning.BlendHorizontalVelocity(__instance, GlobalModifiersState.MomentumPercent);
    }
}

[HarmonyPatch(typeof(PlayerHealth), "Update")]
internal static class PlayerHealth_MaxHealth_Patch
{
    // Awake-only application had the same "needs a respawn" issue as movement tuning did (and
    // possibly wasn't reliably IsServer yet that early); applying every frame with a version check
    // fixes both. The baseline is captured once so repeated live edits scale from the original
    // value, not whatever was last (incorrectly) applied.
    private static void Postfix(PlayerHealth __instance)
    {
        PlayerHealthTuning.ApplyIfChanged(__instance, GlobalModifiersState.HealthMultiplier, GlobalModifiersState.TuningVersion);
    }
}
