using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MyceliumNetworking;
using Steamworks;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace StraftatEightsPlugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("RugbugRedfern.MyceliumNetworking")]
[BepInProcess("STRAFTAT.exe")]
public class Plugin : BaseUnityPlugin
{
    internal const uint ModId = 1892737465u;

    internal static new ManualLogSource Logger = null!;

    internal static ConfigEntry<bool> WallJumpEnabled = null!;
    internal static ConfigEntry<bool> WallJumpBoostEnabled = null!;
    internal static ConfigEntry<bool> SlidingEnabled = null!;
    internal static ConfigEntry<bool> SlideBoostEnabled = null!;
    internal static ConfigEntry<int> MoveSpeedPercent = null!;
    internal static ConfigEntry<int> AdsSpeedPercent = null!;
    internal static ConfigEntry<int> MaxHealthPercent = null!;
    internal static ConfigEntry<int> GravityPercent = null!;
    internal static ConfigEntry<int> MomentumPercent = null!;

    private void Awake()
    {
        Logger = base.Logger;

        WallJumpEnabled = Config.Bind("Global Modifiers", "Enable Wall Jump", true,
            "Host-controlled: allows wall jumping for everyone in the lobby.");
        WallJumpBoostEnabled = Config.Bind("Global Modifiers", "Enable Wall Jump Speed Boost", true,
            "Host-controlled: whether wall jumping gives the usual Straftat horizontal speed kick (chaining wall jumps for extra speed).");
        SlidingEnabled = Config.Bind("Global Modifiers", "Enable Sliding", true,
            "Host-controlled: allows crouch-sliding for everyone in the lobby.");
        SlideBoostEnabled = Config.Bind("Global Modifiers", "Enable Slide Speed Boost", true,
            "Host-controlled: whether sliding gives the usual Straftat speed boost. Disable to slide like most other games.");
        MoveSpeedPercent = Config.Bind("Global Modifiers", "Move Speed %", 100,
            new ConfigDescription("Host-controlled: overall movement speed as a percent of normal.", new AcceptableValueRange<int>(50, 200)));
        AdsSpeedPercent = Config.Bind("Global Modifiers", "ADS Speed %", 80,
            new ConfigDescription("Host-controlled: movement speed while aiming down sights, as a percent of normal.", new AcceptableValueRange<int>(50, 100)));
        MaxHealthPercent = Config.Bind("Global Modifiers", "Max Health %", 100,
            new ConfigDescription("Host-controlled: max health as a percent of normal.", new AcceptableValueRange<int>(10, 400)));
        GravityPercent = Config.Bind("Global Modifiers", "Gravity %", 100,
            new ConfigDescription("Host-controlled: gravity as a percent of normal. Lower = floatier jumps and slower falling.", new AcceptableValueRange<int>(10, 100)));
        MomentumPercent = Config.Bind("Global Modifiers", "Momentum %", 100,
            new ConfigDescription(
                "Host-controlled: how much weight/inertia is behind movement. 100% = stock Straftat snappiness. " +
                "Higher = heavier, slower to speed up/stop/turn. " +
                "Lower = snappier, near-instant direction changes.",
                new AcceptableValueRange<int>(10, 400)));

        WallJumpEnabled.SettingChanged += (_, _) => HostSettings.PushIfHost();
        SlidingEnabled.SettingChanged += (_, _) => HostSettings.PushIfHost();
        SlideBoostEnabled.SettingChanged += (_, _) => HostSettings.PushIfHost();
        WallJumpBoostEnabled.SettingChanged += (_, _) => HostSettings.PushIfHost();
        MoveSpeedPercent.SettingChanged += (_, _) => HostSettings.PushIfHost();
        AdsSpeedPercent.SettingChanged += (_, _) => HostSettings.PushIfHost();
        MaxHealthPercent.SettingChanged += (_, _) => HostSettings.PushIfHost();
        GravityPercent.SettingChanged += (_, _) => HostSettings.PushIfHost();
        MomentumPercent.SettingChanged += (_, _) => HostSettings.PushIfHost();

        MyceliumNetwork.RegisterNetworkObject(this, ModId);
        MyceliumNetwork.LobbyEntered += HostSettings.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += HostSettings.OnPlayerEntered;

        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    // Invoked on every peer when the host (re)broadcasts its settings
    [CustomRPC]
    public void SyncMovementSettings(bool wallJump, bool sliding, bool slideBoost, bool wallJumpBoost, int moveSpeedPercent, int adsSpeedPercent, int maxHealthPercent, int gravityPercent, int momentumPercent)
    {
        HostSettings.Apply(wallJump, sliding, slideBoost, wallJumpBoost, moveSpeedPercent, adsSpeedPercent, maxHealthPercent, gravityPercent, momentumPercent);
    }
}

// Effective settings every peer enforces locally; only the lobby host's config is authoritative
internal static class HostSettings
{
    internal static bool WallJumpEnabled = true;
    internal static bool SlidingEnabled = true;
    internal static bool SlideBoostEnabled = true;
    internal static bool WallJumpBoostEnabled = true;
    internal static float SpeedMultiplier = 1f;
    internal static float AdsSpeedMultiplier = 0.8f;
    internal static float HealthMultiplier = 1f;
    internal static float GravityMultiplier = 1f;
    internal static float MomentumPercent = 100f;

    // Bumped on every Apply so Update can cheaply detect "nothing changed" and skip re-applying tuning
    internal static int TuningVersion;

    internal static void Apply(bool wallJump, bool sliding, bool slideBoost, bool wallJumpBoost, int moveSpeedPercent, int adsSpeedPercent, int maxHealthPercent, int gravityPercent, int momentumPercent)
    {
        WallJumpEnabled = wallJump;
        SlidingEnabled = sliding;
        SlideBoostEnabled = slideBoost;
        WallJumpBoostEnabled = wallJumpBoost;
        SpeedMultiplier = moveSpeedPercent / 100f;
        AdsSpeedMultiplier = adsSpeedPercent / 100f;
        HealthMultiplier = maxHealthPercent / 100f;
        GravityMultiplier = gravityPercent / 100f;
        MomentumPercent = momentumPercent;
        TuningVersion++;
    }

    private static void ApplyFromHostConfig()
    {
        Apply(Plugin.WallJumpEnabled.Value, Plugin.SlidingEnabled.Value, Plugin.SlideBoostEnabled.Value, Plugin.WallJumpBoostEnabled.Value, Plugin.MoveSpeedPercent.Value, Plugin.AdsSpeedPercent.Value, Plugin.MaxHealthPercent.Value, Plugin.GravityPercent.Value, Plugin.MomentumPercent.Value);
    }

    internal static void PushIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplyFromHostConfig();
        MyceliumNetwork.RPC(Plugin.ModId, nameof(Plugin.SyncMovementSettings), ReliableType.Reliable, RpcArgs());
    }

    internal static void OnLobbyEntered()
    {
        if (MyceliumNetwork.IsHost)
        {
            ApplyFromHostConfig();
        }
    }

    // Late joiners won't have received earlier broadcasts, so catch them up directly
    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPCTarget(Plugin.ModId, nameof(Plugin.SyncMovementSettings), player, ReliableType.Reliable, RpcArgs());
    }

    // MyceliumNetworking's serializer only supports primitives
    private static object[] RpcArgs()
    {
        return new object[]
        {
            Plugin.WallJumpEnabled.Value, Plugin.SlidingEnabled.Value, Plugin.SlideBoostEnabled.Value, Plugin.WallJumpBoostEnabled.Value,
            Plugin.MoveSpeedPercent.Value, Plugin.AdsSpeedPercent.Value, Plugin.MaxHealthPercent.Value, Plugin.GravityPercent.Value,
            MomentumPercent
        };
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Slide")]
internal static class FirstPersonController_Slide_Patch
{
    // Blocks the crouch-slide input handler entirely while sliding is disabled
    private static bool Prefix()
    {
        return HostSettings.SlidingEnabled;
    }
}

[HarmonyPatch(typeof(FirstPersonController), "OnControllerColliderHit")]
internal static class FirstPersonController_WallJump_Patch
{
    // The base game sets CanWallJump on every wall collision, so we clear it right after
    private static void Postfix(FirstPersonController __instance)
    {
        if (!HostSettings.WallJumpEnabled)
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
        if (HostSettings.SlideBoostEnabled || !__instance.isSliding)
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
        if (HostSettings.WallJumpBoostEnabled)
        {
            return;
        }
        MovementTuning.ClampWallJumpBoost(__instance, MovementTuning.GetSprintSpeed(__instance));
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class FirstPersonController_Speed_Patch
{
    // movementFactor is a plain per-client multiplier read every frame for move speed
    private static void Prefix(FirstPersonController __instance)
    {
        float adsFactor = __instance.isAiming ? HostSettings.AdsSpeedMultiplier : 1f;
        __instance.movementFactor = HostSettings.SpeedMultiplier * adsFactor;
        __instance.gravityMultiplier = HostSettings.GravityMultiplier;

        // Cheap version check skips the reflection work on every frame where nothing changed,
        // while still applying live edits instantly (no need to wait for a respawn)
        MovementTuning.ApplyMomentumIfChanged(__instance, HostSettings.MomentumPercent, HostSettings.TuningVersion);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Awake___UserLogic")]
internal static class FirstPersonController_Tuning_Patch
{
    // Initial apply at spawn; Update's version check picks up any later live edits
    private static void Postfix(FirstPersonController __instance)
    {
        MovementTuning.ApplyMomentumIfChanged(__instance, HostSettings.MomentumPercent, HostSettings.TuningVersion);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "HandleMovementInput")]
internal static class FirstPersonController_Momentum_Patch
{
    // Carries momentum through camera-driven direction changes (e.g. a mouse 180), not just WASD releases
    private static void Postfix(FirstPersonController __instance)
    {
        MovementTuning.BlendHorizontalVelocity(__instance, HostSettings.MomentumPercent);
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
        PlayerHealthTuning.ApplyIfChanged(__instance, HostSettings.HealthMultiplier, HostSettings.TuningVersion);
    }
}

internal static class PlayerHealthTuning
{
    private sealed class Memory
    {
        public float BaselineFullHealth = -1f;
        public int LastAppliedVersion = -1;
    }

    private static readonly ConditionalWeakTable<PlayerHealth, Memory> MemoryByInstance = new();

    internal static void ApplyIfChanged(PlayerHealth controller, float healthMultiplier, int version)
    {
        Memory memory = MemoryByInstance.GetOrCreateValue(controller);
        if (memory.BaselineFullHealth < 0f)
        {
            memory.BaselineFullHealth = controller.fullHealth;
        }
        if (memory.LastAppliedVersion == version)
        {
            return;
        }
        memory.LastAppliedVersion = version;

        float scaledFullHealth = memory.BaselineFullHealth * healthMultiplier;
        float previousFullHealth = controller.fullHealth;
        controller.fullHealth = scaledFullHealth;

        if (controller.IsServer)
        {
            float bonus = scaledFullHealth - previousFullHealth;
            if (!Mathf.Approximately(bonus, 0f))
            {
                controller.RpcLogic___RemoveHealth_431000436(-bonus);
            }
        }
    }
}



