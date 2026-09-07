using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

// Effective Global Modifiers settings every peer enforces locally; only the lobby host's config is
// authoritative. See GlobalModifiersConfig for the bound ConfigEntry fields and GlobalModifiersPatches
// for where these values actually get enforced via Harmony.
internal static class GlobalModifiersState
{
    internal static bool Enabled;
    internal static bool WallJumpEnabled = true;
    internal static bool SlidingEnabled = true;
    internal static bool SlideBoostEnabled = true;
    internal static bool WallJumpBoostEnabled = true;
    internal static float SpeedMultiplier = 1f;
    internal static float AdsSpeedMultiplier = 0.8f;
    internal static float GravityMultiplier = 1f;
    internal static float MomentumPercent = 100f;
    internal static float AirSpeedRatioPercent = MovementTuning.StockAirSpeedRatioPercent;

    // Bumped on every Apply so per-frame patches can cheaply detect "nothing changed" and skip
    // re-applying reflection-based tuning
    internal static int TuningVersion;

    // When disabled, every value is forced back to its true stock/neutral equivalent (not just this
    // mod's own defaults - e.g. ADS slowdown defaults to an intentional 80%, but "disabled" means 100%)
    // so the individual sliders are ignored entirely and movement is pure stock Straftat.
    private static int _settingsRevision;
    private static int _lastSettingsRoundId = -1;
    private static int _lastSettingsRevision = -1;

    internal static void Apply(bool enabled, bool wallJump, bool sliding, bool slideBoost, bool wallJumpBoost, int moveSpeedPercent, int adsSpeedPercent, int gravityPercent, int momentumPercent, int airSpeedRatioPercent)
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
            GravityMultiplier = 1f;
            MomentumPercent = 100f;
            AirSpeedRatioPercent = MovementTuning.StockAirSpeedRatioPercent;
            TuningVersion++;
            Plugin.Logger.LogInfo("[GlobalModifiers] Apply: disabled - all values reset to stock");
            return;
        }

        moveSpeedPercent = Mathf.Clamp(moveSpeedPercent, 50, 200);
        adsSpeedPercent = Mathf.Clamp(adsSpeedPercent, 50, 100);
        gravityPercent = Mathf.Clamp(gravityPercent, 10, 100);
        momentumPercent = Mathf.Clamp(momentumPercent, 10, 400);
        airSpeedRatioPercent = Mathf.Clamp(airSpeedRatioPercent, 50, 200);
        WallJumpEnabled = wallJump;
        SlidingEnabled = sliding;
        SlideBoostEnabled = slideBoost;
        WallJumpBoostEnabled = wallJumpBoost;
        SpeedMultiplier = moveSpeedPercent / 100f;
        AdsSpeedMultiplier = adsSpeedPercent / 100f;
        GravityMultiplier = gravityPercent / 100f;
        MomentumPercent = momentumPercent;
        AirSpeedRatioPercent = airSpeedRatioPercent;
        TuningVersion++;
        Plugin.Logger.LogInfo($"[MovementSettings] Apply: SpeedMultiplier={SpeedMultiplier:0.00} AdsSpeedMultiplier={AdsSpeedMultiplier:0.00} GravityMultiplier={GravityMultiplier:0.00} MomentumPercent={MomentumPercent} AirSpeedRatioPercent={AirSpeedRatioPercent} TuningVersion={TuningVersion}");
    }

    private static void ApplyFromHostConfig()
    {
        Apply(Plugin.MovementTweaksEnabled.Value, Plugin.WallJumpEnabled.Value, Plugin.SlidingEnabled.Value, Plugin.SlideBoostEnabled.Value, Plugin.WallJumpBoostEnabled.Value, Plugin.MoveSpeedPercent.Value, Plugin.AdsSpeedPercent.Value, Plugin.GravityPercent.Value, Plugin.MomentumPercent.Value, Plugin.AirSpeedRatioPercent.Value);
    }

    internal static void PushIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            Plugin.Logger.LogInfo($"[GlobalModifiers] PushIfHost skipped: InLobby={MyceliumNetwork.InLobby} IsHost={MyceliumNetwork.IsHost}");
            return;
        }
        ApplyFromHostConfig();
        Plugin.Logger.LogInfo($"[MovementSettings] Host broadcasting movement settings to {MyceliumNetwork.PlayerCount} player(s)");
        MyceliumNetwork.RPC(Plugin.GlobalModifiersModId, nameof(Plugin.SyncMovementSettings), ReliableType.Reliable,
            RpcArgs(++_settingsRevision));
    }

    // Mycelium's P2P session in this game intermittently fails to deliver a message with no error on
    // the sending side (see repo memory - "Session request failed" / "ProblemDetectedLocally" in the
    // log), so a single one-shot broadcast on config change or player join isn't reliable enough.
    // Resending periodically regardless of whether anything changed self-heals within a few seconds,
    // the same way the (working) Juggernaut mod's every-second state rebroadcast does.
    private static float _nextPeriodicPushTime;

    internal static void PeriodicPushIfHost()
    {
        if (!HostSettingsSync.IsDue(ref _nextPeriodicPushTime))
        {
            return;
        }
        PushIfHost();
    }

    internal static void OnLobbyEntered()
    {
        Plugin.Logger.LogInfo($"[GlobalModifiers] Lobby session started, IsHost={MyceliumNetwork.IsHost}");
        _lastSettingsRoundId = -1;
        _lastSettingsRevision = -1;
        if (MyceliumNetwork.IsHost)
        {
            ApplyFromHostConfig();
        }
    }

    internal static void ResetForLobbyLeft()
    {
        Apply(false, true, true, true, true, 100, 100, 100, 100, Mathf.RoundToInt(MovementTuning.StockAirSpeedRatioPercent));
    }

    // Late joiners won't have received earlier broadcasts, so catch them up directly
    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        Plugin.Logger.LogInfo($"[MovementSettings] Sending catch-up movement settings to newly joined player {player}");
        MyceliumNetwork.RPCTarget(Plugin.GlobalModifiersModId, nameof(Plugin.SyncMovementSettings), player,
            ReliableType.Reliable, RpcArgs(_settingsRevision));
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastSettingsRoundId, ref _lastSettingsRevision);
    }

    // MyceliumNetworking's serializer only supports primitives
    private static object[] RpcArgs(int revision)
    {
        return new object[]
        {
            MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId,
            revision,
            Plugin.MovementTweaksEnabled.Value,
            Plugin.WallJumpEnabled.Value, Plugin.SlidingEnabled.Value, Plugin.SlideBoostEnabled.Value, Plugin.WallJumpBoostEnabled.Value,
            Plugin.MoveSpeedPercent.Value, Plugin.AdsSpeedPercent.Value, Plugin.GravityPercent.Value,
            Plugin.MomentumPercent.Value, Plugin.AirSpeedRatioPercent.Value
        };
    }
}
