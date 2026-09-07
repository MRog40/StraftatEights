using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HealthSettingsState
{
    internal static bool Enabled;
    internal static float MaxHealthMultiplier = 1f;
    internal static bool RegenEnabled;
    internal static float RegenDelaySeconds = 5f;
    internal static float RegenRate = 10f;
    internal static int TuningVersion;

    private static float _nextPeriodicPushTime;
    private static float _nextServerScanTime;
    private static int _settingsRevision;
    private static int _lastSettingsRoundId = -1;
    private static int _lastSettingsRevision = -1;

    internal static void Apply(bool enabled, int maxHealthPercent, bool regenEnabled, int regenDelaySeconds, int regenRate)
    {
        Enabled = enabled;
        if (!enabled)
        {
            MaxHealthMultiplier = 1f;
            RegenEnabled = false;
            RegenDelaySeconds = 5f;
            RegenRate = 25f;
            TuningVersion++;
            Plugin.Logger.LogInfo("[HealthSettings] Apply: disabled - all values reset to stock");
            return;
        }
        MaxHealthMultiplier = Mathf.Clamp(maxHealthPercent, 10, 400) / 100f;
        RegenEnabled = regenEnabled;
        RegenDelaySeconds = Mathf.Clamp(regenDelaySeconds, 2f, 15f);
        RegenRate = NormalizeRegenRate(regenRate);
        TuningVersion++;
        Plugin.Logger.LogInfo($"[HealthSettings] Apply: maxHealthMultiplier={MaxHealthMultiplier:0.##} enabled={RegenEnabled} delay={RegenDelaySeconds:0.##} rate={RegenRate:0.##} version={TuningVersion}");
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
        MyceliumNetwork.RPC(Plugin.HealthSettingsModId, nameof(Plugin.SyncHealthSettings), ReliableType.Reliable,
            RpcArgs(++_settingsRevision));
    }

    internal static void PeriodicPushIfHost()
    {
        if (!HostSettingsSync.IsDue(ref _nextPeriodicPushTime))
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
        PlayerHealth[] players = UnityEngine.Object.FindObjectsOfType<PlayerHealth>(true);
        foreach (PlayerHealth player in players)
        {
            HealthSettingsTuning.ApplyIfChanged(player, MaxHealthMultiplier, TuningVersion);
            HealthSettingsTuning.RegenerateIfNeeded(player, HealthSettingsTuning.GetMemory(player));
        }
    }

    internal static void OnLobbyEntered()
    {
        _lastSettingsRoundId = -1;
        _lastSettingsRevision = -1;
        if (MyceliumNetwork.IsHost)
        {
            ApplyFromHostConfig();
        }
    }

    internal static void ResetForLobbyLeft()
    {
        Apply(false, 100, false, 5, 25);
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPCTarget(Plugin.HealthSettingsModId, nameof(Plugin.SyncHealthSettings), player,
            ReliableType.Reliable, RpcArgs(_settingsRevision));
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastSettingsRoundId, ref _lastSettingsRevision);
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