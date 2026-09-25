using System.Collections.Generic;
using System.Globalization;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

// Effective Global Modifiers settings every peer enforces locally; only the lobby host's config is
// authoritative. See GlobalModifiersConfig for the bound ConfigEntry fields and GlobalModifiersPatches
// for where these values actually get enforced via Harmony.
internal static class GlobalModifiersState
{
    internal const string SettingsLobbyDataKey = "Eights_GlobalModifiers_Settings";
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
    internal static float ShootingSpeedMultiplier = 1f;
    internal static bool PlayerRadarEnabled = true;

    internal static float EffectiveMomentumPercent => GameModeManager.ShouldIgnoreGlobalMovementSettings
        ? 100f
        : MomentumPercent;
    internal static float EffectiveAirSpeedRatioPercent => GameModeManager.ShouldIgnoreGlobalMovementSettings
        ? MovementTuning.StockAirSpeedRatioPercent
        : AirSpeedRatioPercent;

    // Bumped on every Apply so per-frame patches can cheaply detect "nothing changed" and skip
    // re-applying reflection-based tuning
    internal static int TuningVersion;

    // When disabled, every value is forced back to its true stock/neutral equivalent (not just this
    // mod's own defaults - e.g. ADS slowdown defaults to an intentional 80%, but "disabled" means 100%)
    // so the individual sliders are ignored entirely and movement is pure stock Straftat.
    private static readonly ModeSyncState Sync = new();
    private static float _nextClientSettingsPollTime;

    internal static void Apply(bool enabled, bool wallJump, bool sliding, bool slideBoost,
        bool wallJumpBoost, int moveSpeedPercent, int adsSpeedPercent, int gravityPercent,
        int momentumPercent, int airSpeedRatioPercent, int shootingSpeedPercent,
        bool playerRadarEnabled, int maximumBloodEffects)
    {
        Enabled = enabled;
        PlayerRadarEnabled = playerRadarEnabled;
        BloodCleanupState.ApplyMaximumBloodEffects(maximumBloodEffects);
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
            ShootingSpeedMultiplier = 1f;
            TuningVersion++;
            return;
        }

        moveSpeedPercent = Mathf.Clamp(moveSpeedPercent, 50, 200);
        adsSpeedPercent = Mathf.Clamp(adsSpeedPercent, 50, 100);
        gravityPercent = Mathf.Clamp(gravityPercent, 10, 100);
        momentumPercent = Mathf.Clamp(momentumPercent, 10, 400);
        airSpeedRatioPercent = Mathf.Clamp(airSpeedRatioPercent, 50, 200);
        shootingSpeedPercent = Mathf.Clamp(shootingSpeedPercent, 10, 100);
        WallJumpEnabled = wallJump;
        SlidingEnabled = sliding;
        SlideBoostEnabled = slideBoost;
        WallJumpBoostEnabled = wallJumpBoost;
        SpeedMultiplier = moveSpeedPercent / 100f;
        AdsSpeedMultiplier = adsSpeedPercent / 100f;
        GravityMultiplier = gravityPercent / 100f;
        MomentumPercent = momentumPercent;
        AirSpeedRatioPercent = airSpeedRatioPercent;
        ShootingSpeedMultiplier = shootingSpeedPercent / 100f;
        TuningVersion++;
    }

    private static void ApplyFromHostConfig()
    {
        Apply(Plugin.MovementTweaksEnabled.Value, Plugin.WallJumpEnabled.Value,
            Plugin.SlidingEnabled.Value, Plugin.SlideBoostEnabled.Value,
            Plugin.WallJumpBoostEnabled.Value, Plugin.MoveSpeedPercent.Value,
            Plugin.AdsSpeedPercent.Value, Plugin.GravityPercent.Value,
            Plugin.MomentumPercent.Value, Plugin.AirSpeedRatioPercent.Value,
            Plugin.ShootingSpeedPercent.Value, Plugin.PlayerRadarEnabled.Value,
            Plugin.MaximumBloodEffects.Value);
    }

    internal static void PushIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplyFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision,
            Plugin.MovementTweaksEnabled.Value ? "1" : "0",
            Plugin.WallJumpEnabled.Value ? "1" : "0",
            Plugin.SlidingEnabled.Value ? "1" : "0",
            Plugin.SlideBoostEnabled.Value ? "1" : "0",
            Plugin.WallJumpBoostEnabled.Value ? "1" : "0",
            Plugin.MoveSpeedPercent.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.AdsSpeedPercent.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.GravityPercent.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.MomentumPercent.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.AirSpeedRatioPercent.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.ShootingSpeedPercent.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.PlayerRadarEnabled.Value ? "1" : "0",
            Plugin.MaximumBloodEffects.Value.ToString(CultureInfo.InvariantCulture));
        MyceliumNetwork.RPC(Plugin.GlobalModifiersModId, nameof(Plugin.SyncMovementSettings), ReliableType.Reliable,
            RpcArgs(revision));
    }

    // Mycelium's P2P session in this game intermittently fails to deliver a message with no error on
    // the sending side (see repo memory - "Session request failed" / "ProblemDetectedLocally" in the
    // log), so a single one-shot broadcast on config change or player join isn't reliable enough.
    // Resending periodically regardless of whether anything changed self-heals within a few seconds,
    // the same way the (working) Juggertat mod's every-second state rebroadcast does.
    internal static void PeriodicPushIfHost()
    {
        if (!Sync.IsSettingsPushDue())
        {
            return;
        }
        PushIfHost();
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        _nextClientSettingsPollTime = 0f;
        if (MyceliumNetwork.IsHost)
        {
            PushIfHost();
        }
        else
        {
            ApplyLobbySettingsSnapshot();
        }
    }

    internal static void PollSettingsIfClient()
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || Time.unscaledTime < _nextClientSettingsPollTime)
        {
            return;
        }

        _nextClientSettingsPollTime = Time.unscaledTime
            + HostSettingsSync.SettingsHeartbeatIntervalSeconds;
        ApplyLobbySettingsSnapshot();
    }

    internal static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !ModeLobbyDataSync.ContainsKey(keys, SettingsLobbyDataKey))
        {
            return;
        }

        ApplyLobbySettingsSnapshot();
    }

    internal static void ResetForLobbyLeft()
    {
        Sync.ResetForLobby();
        Apply(false, true, true, true, true, 100, 100, 100, 100,
            Mathf.RoundToInt(MovementTuning.StockAirSpeedRatioPercent), 100, true,
            Plugin.MaximumBloodEffects.Value);
    }

    // Late joiners won't have received earlier broadcasts, so catch them up directly
    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPCTarget(Plugin.GlobalModifiersModId, nameof(Plugin.SyncMovementSettings), player,
            ReliableType.Reliable, RpcArgs(Sync.SettingsRevision));
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision,
        string source = "rpc")
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision, source);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 13, out CSteamID hostId,
                out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !LobbySnapshotCodec.TryParseBool(fields[1], out bool wallJump)
            || !LobbySnapshotCodec.TryParseBool(fields[2], out bool sliding)
            || !LobbySnapshotCodec.TryParseBool(fields[3], out bool slideBoost)
            || !LobbySnapshotCodec.TryParseBool(fields[4], out bool wallJumpBoost)
            || !int.TryParse(fields[5], out int moveSpeedPercent)
            || !int.TryParse(fields[6], out int adsSpeedPercent)
            || !int.TryParse(fields[7], out int gravityPercent)
            || !int.TryParse(fields[8], out int momentumPercent)
            || !int.TryParse(fields[9], out int airSpeedRatioPercent)
            || !int.TryParse(fields[10], out int shootingSpeedPercent)
            || !LobbySnapshotCodec.TryParseBool(fields[11], out bool playerRadarEnabled)
            || !int.TryParse(fields[12], out int maximumBloodEffects)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("global-modifiers", "settings")))
        {
            return;
        }

        Apply(enabled, wallJump, sliding, slideBoost, wallJumpBoost, moveSpeedPercent,
            adsSpeedPercent, gravityPercent, momentumPercent, airSpeedRatioPercent,
            shootingSpeedPercent, playerRadarEnabled, maximumBloodEffects);
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
            Plugin.MomentumPercent.Value, Plugin.AirSpeedRatioPercent.Value,
            Plugin.ShootingSpeedPercent.Value, Plugin.PlayerRadarEnabled.Value,
            Plugin.MaximumBloodEffects.Value
        };
    }
}
