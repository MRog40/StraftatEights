using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace StraftatEightsPlugin;

// Reflection-based helpers for tuning FirstPersonController's private movement fields, used by
// GlobalModifiersPatches. Kept separate since it deals with raw field access rather than config/sync.
internal static class MovementTuning
{
    // Stock Straftat acceleration value, shared by all 7 acceleration fields by default
    private const float BaseAcceleration = 15f;

    // Stock Straftat sprint-in-air speed is already faster than ground sprint (14 vs 12) - this is
    // the real ratio, used as the Air Move Speed % slider's default so 100% on the slider wouldn't
    // lie about what stock Straftat actually does
    internal const float StockAirSpeedRatioPercent = 14f / 12f * 100f;

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static FieldInfo _globalAcceleration = null!, _globalDeceleration = null!, _walkAcceleration = null!, _sprintAcceleration = null!, _crouchAcceleration = null!, _airAcceleration = null!, _sprintAirAcceleration = null!;
    private static FieldInfo _walkSpeed = null!, _sprintSpeed = null!, _airSpeed = null!, _sprintAirSpeed = null!;
    private static FieldInfo _bforcefinal = null!, _bfactor = null!;
    private static bool _cached;

    private static void EnsureCached()
    {
        if (_cached)
        {
            return;
        }
        System.Type t = typeof(FirstPersonController);
        _globalAcceleration = t.GetField("globalAcceleration", Flags);
        _globalDeceleration = t.GetField("globalDeceleration", Flags);
        _walkAcceleration = t.GetField("walkAcceleration", Flags);
        _sprintAcceleration = t.GetField("sprintAcceleration", Flags);
        _crouchAcceleration = t.GetField("crouchAcceleration", Flags);
        _airAcceleration = t.GetField("airAcceleration", Flags);
        _sprintAirAcceleration = t.GetField("sprintAirAcceleration", Flags);
        _walkSpeed = t.GetField("walkSpeed", Flags);
        _sprintSpeed = t.GetField("sprintSpeed", Flags);
        _airSpeed = t.GetField("airSpeed", Flags);
        _sprintAirSpeed = t.GetField("sprintAirSpeed", Flags);
        _bforcefinal = t.GetField("bforcefinal", Flags);
        _bfactor = t.GetField("bfactor", Flags);
        _cached = true;
    }

    // Momentum % is inversely proportional to acceleration: higher % = more weight/inertia = slower
    // to speed up, slow down, or redirect. 100% reproduces stock Straftat's snappiness exactly.
    // Air speed ratio % is airSpeed/sprintAirSpeed as a percent of walkSpeed/sprintSpeed (ground
    // speed) - the real stock ratio is ~117%, i.e. already faster in the air than sprinting on the
    // ground, so the default reflects that instead of a misleading 100%.
    internal static void ApplyTuning(FirstPersonController controller, float momentumPercent, float airSpeedRatioPercent)
    {
        EnsureCached();
        float accel = momentumPercent <= 0f ? BaseAcceleration : BaseAcceleration * 100f / momentumPercent;
        _globalAcceleration.SetValue(controller, accel);
        _globalDeceleration.SetValue(controller, accel);
        _walkAcceleration.SetValue(controller, accel);
        _sprintAcceleration.SetValue(controller, accel);
        _crouchAcceleration.SetValue(controller, accel);
        _airAcceleration.SetValue(controller, accel);
        _sprintAirAcceleration.SetValue(controller, accel);

        float airRatio = airSpeedRatioPercent / 100f;
        float walkSpeed = (float)_walkSpeed.GetValue(controller);
        float sprintSpeed = (float)_sprintSpeed.GetValue(controller);
        _airSpeed.SetValue(controller, walkSpeed * airRatio);
        _sprintAirSpeed.SetValue(controller, sprintSpeed * airRatio);
    }

    // Tracks the tuning version last written to each controller so Update can skip the reflection
    // work entirely on the (vast majority of) frames where nothing has changed
    private static readonly ConditionalWeakTable<FirstPersonController, StrongBox<int>> LastAppliedVersion = new();

    internal static void ApplyTuningIfChanged(FirstPersonController controller, float momentumPercent, float airSpeedRatioPercent, int version)
    {
        StrongBox<int> box = LastAppliedVersion.GetOrCreateValue(controller);
        if (box.Value == version)
        {
            return;
        }
        ApplyTuning(controller, momentumPercent, airSpeedRatioPercent);
        box.Value = version;
    }

    internal static float GetWalkSpeed(FirstPersonController controller)
    {
        EnsureCached();
        return (float)_walkSpeed.GetValue(controller);
    }

    internal static float GetSprintSpeed(FirstPersonController controller)
    {
        EnsureCached();
        return (float)_sprintSpeed.GetValue(controller);
    }

    internal static float GetEffectiveSprintSpeed(FirstPersonController controller)
    {
        return GetSprintSpeed(controller) * GlobalModifiersState.SpeedMultiplier;
    }

    // WASD-axis smoothing alone doesn't cover camera-driven direction changes (e.g. spinning 180 while
    // still holding forward) since moveDirection is recomputed fresh from facing every frame with no
    // persisted world-space velocity. This blends the resulting horizontal direction/speed across
    // frames instead, so a mouse-turn 180 also carries momentum instead of snapping instantly.
    private sealed class VelocityMemory
    {
        public Vector3 Value;
        public bool Initialized;
    }

    private static readonly ConditionalWeakTable<FirstPersonController, VelocityMemory> VelocityByController = new();

    internal static void BlendHorizontalVelocity(FirstPersonController controller, float momentumPercent)
    {
        VelocityMemory memory = VelocityByController.GetOrCreateValue(controller);
        Vector3 target = new(controller.moveDirection.x, 0f, controller.moveDirection.z);
        if (!memory.Initialized)
        {
            memory.Value = target;
            memory.Initialized = true;
            return;
        }

        float rate = momentumPercent <= 0f ? BaseAcceleration : BaseAcceleration * 100f / momentumPercent;
        float t = Mathf.Clamp01(rate * Time.deltaTime);
        memory.Value = Vector3.Lerp(memory.Value, target, t);
        controller.moveDirection = new Vector3(memory.Value.x, controller.moveDirection.y, memory.Value.z);
    }

    // Tracks whether the in-progress BForce decay (bfactor/bforcefinal) came from a wall jump kick,
    // so HandleBForce's clamp only ever touches that specific effect and leaves knockback/bounces alone
    private static readonly ConditionalWeakTable<FirstPersonController, StrongBox<bool>> WallJumpBForceActive = new();
    private static readonly ConditionalWeakTable<FirstPersonController, StrongBox<bool>> SlideBForceActive = new();

    internal static void MarkWallJumpBForce(FirstPersonController controller, bool isWallJump)
    {
        WallJumpBForceActive.GetOrCreateValue(controller).Value = isWallJump;
    }

    internal static void MarkSlideBForce(FirstPersonController controller, bool isSliding)
    {
        SlideBForceActive.GetOrCreateValue(controller).Value = isSliding;
    }

    internal static void SuppressSlideBoost(FirstPersonController controller)
    {
        if (!SlideBForceActive.TryGetValue(controller, out StrongBox<bool> active) || !active.Value)
        {
            return;
        }
        EnsureCached();

        _bfactor.SetValue(controller, 0f);
        _bforcefinal.SetValue(controller, Vector3.zero);
        active.Value = false;
    }

    internal static void ClampWallJumpBoost(FirstPersonController controller, float capSpeed)
    {
        if (!WallJumpBForceActive.TryGetValue(controller, out StrongBox<bool> active) || !active.Value)
        {
            return;
        }
        EnsureCached();

        float bfactor = (float)_bfactor.GetValue(controller);
        if (bfactor <= 0f)
        {
            active.Value = false;
            return;
        }

        Vector3 bforcefinal = (Vector3)_bforcefinal.GetValue(controller);
        Vector3 moveHorizontal = new(controller.moveDirection.x, 0f, controller.moveDirection.z);
        Vector3 combined = moveHorizontal + bforcefinal;
        float magnitude = combined.magnitude;
        if (magnitude > capSpeed && magnitude > 0.0001f)
        {
            _bforcefinal.SetValue(controller, combined * (capSpeed / magnitude) - moveHorizontal);
        }
    }
}
