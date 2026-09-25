using System.Collections.Generic;
using System.Globalization;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class HealthSettingsState
{
    internal const string SettingsLobbyDataKey = "Eights_HealthSettings_Settings";
    internal static bool Enabled;
    internal static float MaxHealthMultiplier = 1f;
    internal static bool RegenEnabled;
    internal static float RegenDelaySeconds = 5f;
    internal static float RegenRate = 10f;
    internal static int TuningVersion;

    private static float _nextServerScanTime;
    private static float _nextClientSettingsPollTime;
    private static readonly ModeSyncState Sync = new();

    internal static void Apply(bool enabled, int maxHealthPercent, bool regenEnabled, int regenDelaySeconds, int regenRate)
    {
        float nextMaxHealthMultiplier = enabled
            ? Mathf.Clamp(maxHealthPercent, 10, 400) / 100f
            : 1f;
        bool nextRegenEnabled = enabled && regenEnabled;
        float nextRegenDelaySeconds = enabled
            ? Mathf.Clamp(regenDelaySeconds, 2f, 15f)
            : 5f;
        float nextRegenRate = enabled ? NormalizeRegenRate(regenRate) : 25f;
        if (Enabled == enabled
            && Mathf.Approximately(MaxHealthMultiplier, nextMaxHealthMultiplier)
            && RegenEnabled == nextRegenEnabled
            && Mathf.Approximately(RegenDelaySeconds, nextRegenDelaySeconds)
            && Mathf.Approximately(RegenRate, nextRegenRate))
        {
            return;
        }

        Enabled = enabled;
        MaxHealthMultiplier = nextMaxHealthMultiplier;
        RegenEnabled = nextRegenEnabled;
        RegenDelaySeconds = nextRegenDelaySeconds;
        RegenRate = nextRegenRate;
        TuningVersion++;
    }

    private static void ApplyFromHostConfig()
    {
        Apply(Plugin.HealthTweaksEnabled.Value, Plugin.MaxHealthPercent.Value, Plugin.HealthRegenEnabled.Value, Plugin.HealthRegenDelaySeconds.Value, Plugin.HealthRegenRate.Value);
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
            Plugin.HealthTweaksEnabled.Value ? "1" : "0",
            Plugin.MaxHealthPercent.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.HealthRegenEnabled.Value ? "1" : "0",
            Plugin.HealthRegenDelaySeconds.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.HealthRegenRate.Value.ToString(CultureInfo.InvariantCulture));
        MyceliumNetwork.RPC(Plugin.HealthSettingsModId, nameof(Plugin.SyncHealthSettings), ReliableType.Reliable,
            RpcArgs(revision));
    }

    internal static void PeriodicPushIfHost()
    {
        if (!Sync.IsSettingsPushDue())
        {
            return;
        }
        PushIfHost();
    }

    internal static void ServerTick()
    {
        if (!MyceliumNetwork.IsHost || !SessionState.IsActive || !Enabled
            || Time.unscaledTime < _nextServerScanTime)
        {
            return;
        }
        _nextServerScanTime = Time.unscaledTime + 0.1f;
        foreach (PlayerHealth player in PlayerLookup.KnownPlayerHealths)
        {
            if (player == null || !player)
            {
                continue;
            }

            HealthSettingsTuning.ApplyIfChanged(player, MaxHealthMultiplier, TuningVersion);
            HealthSettingsTuning.RegenerateIfNeeded(player, HealthSettingsTuning.GetMemory(player));
        }
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
        Apply(false, 100, false, 5, 25);
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPCTarget(Plugin.HealthSettingsModId, nameof(Plugin.SyncHealthSettings), player,
            ReliableType.Reliable, RpcArgs(Sync.SettingsRevision));
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision,
        string source = "rpc")
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision, source);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 5, out CSteamID hostId,
                out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !int.TryParse(fields[1], out int maxHealthPercent)
            || !LobbySnapshotCodec.TryParseBool(fields[2], out bool regenEnabled)
            || !int.TryParse(fields[3], out int regenDelaySeconds)
            || !int.TryParse(fields[4], out int regenRate)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("health-settings", "settings")))
        {
            return;
        }

        Apply(enabled, maxHealthPercent, regenEnabled, regenDelaySeconds, regenRate);
    }

    private static object[] RpcArgs(int revision)
    {
        return new object[]
        {
            MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId,
            revision,
            Plugin.HealthTweaksEnabled.Value,
            Plugin.MaxHealthPercent.Value,
            Plugin.HealthRegenEnabled.Value,
            Plugin.HealthRegenDelaySeconds.Value,
            Plugin.HealthRegenRate.Value
        };
    }

    private static float NormalizeRegenRate(int rate)
    {
        return rate switch
        {
            25 or 50 or 75 or 100 or 150 or 200 => rate,
            _ => 25
        };
    }
}