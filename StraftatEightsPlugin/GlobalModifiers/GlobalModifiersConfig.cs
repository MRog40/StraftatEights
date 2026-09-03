using BepInEx.Configuration;
using MyceliumNetworking;

namespace StraftatEightsPlugin;

// Config bindings + RPC entry point for the "Global Modifiers" feature (host-controlled movement/
// health toggles and sliders). See GlobalModifiersState for the synced runtime values and
// GlobalModifiersPatches for the Harmony patches that actually enforce them.
public partial class Plugin
{
    internal const uint GlobalModifiersModId = 1892737465u;

    internal static ConfigEntry<bool> MovementTweaksEnabled = null!;
    internal static ConfigEntry<bool> WallJumpEnabled = null!;
    internal static ConfigEntry<bool> WallJumpBoostEnabled = null!;
    internal static ConfigEntry<bool> SlidingEnabled = null!;
    internal static ConfigEntry<bool> SlideBoostEnabled = null!;
    internal static ConfigEntry<int> MoveSpeedPercent = null!;
    internal static ConfigEntry<int> AdsSpeedPercent = null!;
    internal static ConfigEntry<int> MaxHealthPercent = null!;
    internal static ConfigEntry<int> GravityPercent = null!;
    internal static ConfigEntry<int> MomentumPercent = null!;
    internal static ConfigEntry<int> AirSpeedRatioPercent = null!;

    private void InitializeGlobalModifiers()
    {
        MovementTweaksEnabled = Config.Bind("Global Modifiers", "Movement Tweaks Enabled", true,
            "Host-controlled: master switch. Turn off for pure stock Straftat movement.");
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
        AdsSpeedPercent = Config.Bind("Global Modifiers", "ADS Speed %", 100,
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
        AirSpeedRatioPercent = Config.Bind("Global Modifiers", "Air Move Speed %", (int)System.MathF.Round(MovementTuning.StockAirSpeedRatioPercent),
            new ConfigDescription(
                "Host-controlled: air move speed (not air control) as a percent of ground move speed. Stock Straftat is " +
                "already faster sprinting in the air than on the ground (the default reflects that real ratio). Lower this to slow players down in the air.",
                new AcceptableValueRange<int>(50, 200)));

        WallJumpEnabled.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        MovementTweaksEnabled.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        SlidingEnabled.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        SlideBoostEnabled.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        WallJumpBoostEnabled.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        MoveSpeedPercent.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        AdsSpeedPercent.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        MaxHealthPercent.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        GravityPercent.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        MomentumPercent.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();
        AirSpeedRatioPercent.SettingChanged += (_, _) => GlobalModifiersState.PushIfHost();

        MyceliumNetwork.RegisterNetworkObject(this, GlobalModifiersModId);
        // LobbyCreated fires for the host (Steam LobbyCreated_t); LobbyEntered only fires for
        // joining clients (Steam LobbyEnter_t) - the host needs both to ever re-apply/reset on its
        // own session start, since LobbyEntered alone never fires when hosting.
        MyceliumNetwork.LobbyCreated += GlobalModifiersState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += GlobalModifiersState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += GlobalModifiersState.OnPlayerEntered;
    }

    // Invoked on every peer when the host (re)broadcasts its settings
    [CustomRPC]
    public void SyncMovementSettings(bool enabled, bool wallJump, bool sliding, bool slideBoost, bool wallJumpBoost, int moveSpeedPercent, int adsSpeedPercent, int maxHealthPercent, int gravityPercent, int momentumPercent, int airSpeedRatioPercent)
    {
        Logger.LogInfo($"[GlobalModifiers] Received sync: enabled={enabled} moveSpeed%={moveSpeedPercent} maxHealth%={maxHealthPercent}");
        GlobalModifiersState.Apply(enabled, wallJump, sliding, slideBoost, wallJumpBoost, moveSpeedPercent, adsSpeedPercent, maxHealthPercent, gravityPercent, momentumPercent, airSpeedRatioPercent);
    }
}
