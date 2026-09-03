using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

// Effective Global Modifiers settings every peer enforces locally; only the lobby host's config is
// authoritative. See GlobalModifiersConfig for the bound ConfigEntry fields and GlobalModifiersPatches
// for where these values actually get enforced via Harmony.
internal static class GlobalModifiersState
{
    internal static bool Enabled = true;
    internal static bool WallJumpEnabled = true;
    internal static bool SlidingEnabled = true;
    internal static bool SlideBoostEnabled = true;
    internal static bool WallJumpBoostEnabled = true;
    internal static float SpeedMultiplier = 1f;
    internal static float AdsSpeedMultiplier = 0.8f;
    internal static float HealthMultiplier = 1f;
    internal static float GravityMultiplier = 1f;
    internal static float MomentumPercent = 100f;
    internal static float AirSpeedRatioPercent = MovementTuning.StockAirSpeedRatioPercent;

    // Bumped on every Apply so per-frame patches can cheaply detect "nothing changed" and skip
    // re-applying reflection-based tuning
    internal static int TuningVersion;

    // When disabled, every value is forced back to its true stock/neutral equivalent (not just this
    // mod's own defaults - e.g. ADS slowdown defaults to an intentional 80%, but "disabled" means 100%)
    // so the individual sliders are ignored entirely and movement is pure stock Straftat.
    internal static void Apply(bool enabled, bool wallJump, bool sliding, bool slideBoost, bool wallJumpBoost, int moveSpeedPercent, int adsSpeedPercent, int maxHealthPercent, int gravityPercent, int momentumPercent, int airSpeedRatioPercent)
    {
        Enabled = enabled;
        if (!enabled)
        {
            WallJumpEnabled = true;
            SlidingEnabled = true;
            SlideBoostEnabled = true;
            WallJumpBoostEnabled = true;
            SpeedMultiplier = 1f;
            AdsSpeedMultiplier = 1f;
            HealthMultiplier = 1f;
            GravityMultiplier = 1f;
            MomentumPercent = 100f;
            AirSpeedRatioPercent = MovementTuning.StockAirSpeedRatioPercent;
            TuningVersion++;
            return;
        }

        WallJumpEnabled = wallJump;
        SlidingEnabled = sliding;
        SlideBoostEnabled = slideBoost;
        WallJumpBoostEnabled = wallJumpBoost;
        SpeedMultiplier = moveSpeedPercent / 100f;
        AdsSpeedMultiplier = adsSpeedPercent / 100f;
        HealthMultiplier = maxHealthPercent / 100f;
        GravityMultiplier = gravityPercent / 100f;
        MomentumPercent = momentumPercent;
        AirSpeedRatioPercent = airSpeedRatioPercent;
        TuningVersion++;
    }

    private static void ApplyFromHostConfig()
    {
        Apply(Plugin.MovementTweaksEnabled.Value, Plugin.WallJumpEnabled.Value, Plugin.SlidingEnabled.Value, Plugin.SlideBoostEnabled.Value, Plugin.WallJumpBoostEnabled.Value, Plugin.MoveSpeedPercent.Value, Plugin.AdsSpeedPercent.Value, Plugin.MaxHealthPercent.Value, Plugin.GravityPercent.Value, Plugin.MomentumPercent.Value, Plugin.AirSpeedRatioPercent.Value);
    }

    internal static void PushIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplyFromHostConfig();
        Plugin.Logger.LogInfo($"[GlobalModifiers] Host broadcasting movement settings to {MyceliumNetwork.PlayerCount} player(s)");
        MyceliumNetwork.RPC(Plugin.GlobalModifiersModId, nameof(Plugin.SyncMovementSettings), ReliableType.Reliable, RpcArgs());
    }

    internal static void OnLobbyEntered()
    {
        Plugin.Logger.LogInfo($"[GlobalModifiers] Lobby session started, IsHost={MyceliumNetwork.IsHost}");
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
        Plugin.Logger.LogInfo($"[GlobalModifiers] Sending catch-up movement settings to newly joined player {player}");
        MyceliumNetwork.RPCTarget(Plugin.GlobalModifiersModId, nameof(Plugin.SyncMovementSettings), player, ReliableType.Reliable, RpcArgs());
    }

    // MyceliumNetworking's serializer only supports primitives
    private static object[] RpcArgs()
    {
        return new object[]
        {
            Plugin.MovementTweaksEnabled.Value,
            Plugin.WallJumpEnabled.Value, Plugin.SlidingEnabled.Value, Plugin.SlideBoostEnabled.Value, Plugin.WallJumpBoostEnabled.Value,
            Plugin.MoveSpeedPercent.Value, Plugin.AdsSpeedPercent.Value, Plugin.MaxHealthPercent.Value, Plugin.GravityPercent.Value,
            MomentumPercent, Plugin.AirSpeedRatioPercent.Value
        };
    }
}
