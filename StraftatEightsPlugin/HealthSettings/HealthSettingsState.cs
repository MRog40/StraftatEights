using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HealthSettingsState
{
    internal static bool Enabled = true;
    internal static float MaxHealthMultiplier = 1f;
    internal static bool RegenEnabled = true;
    internal static float RegenDelaySeconds = 5f;
    internal static float RegenRate = 10f;
    internal static int TuningVersion;

    private static float _nextPeriodicPushTime;

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
        MaxHealthMultiplier = maxHealthPercent / 100f;
        RegenEnabled = regenEnabled;
        RegenDelaySeconds = Mathf.Clamp(regenDelaySeconds, 2f, 15f);
        RegenRate = Mathf.Max(25f, regenRate);
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
        MyceliumNetwork.RPC(Plugin.HealthSettingsModId, nameof(Plugin.SyncHealthSettings), ReliableType.Reliable, RpcArgs());
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
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        PlayerHealth[] players = UnityEngine.Object.FindObjectsOfType<PlayerHealth>(true);
        foreach (PlayerHealth player in players)
        {
            HealthSettingsTuning.ApplyIfChanged(player, MaxHealthMultiplier, TuningVersion);
            HealthSettingsTuning.RegenerateIfNeeded(player, HealthSettingsTuning.GetMemory(player));
        }
    }

    internal static void OnLobbyEntered()
    {
        if (MyceliumNetwork.IsHost)
        {
            ApplyFromHostConfig();
        }
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPCTarget(Plugin.HealthSettingsModId, nameof(Plugin.SyncHealthSettings), player, ReliableType.Reliable, RpcArgs());
    }

    private static object[] RpcArgs()
    {
        return new object[] { Plugin.HealthTweaksEnabled.Value, Plugin.MaxHealthPercent.Value, Plugin.HealthRegenEnabled.Value, Plugin.HealthRegenDelaySeconds.Value, Plugin.HealthRegenRate.Value };
    }
}