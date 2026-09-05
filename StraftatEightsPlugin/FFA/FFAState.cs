using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class FFAState
{
    internal static bool Enabled;
    internal static float RespawnDelaySeconds = 3f;
    internal static int KillsToWin = 10;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Kills = new();

    private static float _nextSettingsPushTime;

    internal static void ApplySettings(bool enabled, float respawnDelaySeconds, int killsToWin)
    {
        bool wasEnabled = Enabled;
        Enabled = enabled;
        RespawnDelaySeconds = respawnDelaySeconds;
        KillsToWin = killsToWin;
        if (!wasEnabled && enabled)
        {
            ResetMatchState();
        }
        else if (wasEnabled && !enabled)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig()
    {
        ApplySettings(Plugin.FFAEnabled.Value, Plugin.FFARespawnDelaySeconds.Value, Plugin.FFAKillsToWin.Value);
    }

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplySettingsFromHostConfig();
        MyceliumNetwork.RPC(Plugin.FFAModId, nameof(Plugin.SyncFFASettings), ReliableType.Reliable,
            Plugin.FFAEnabled.Value, Plugin.FFARespawnDelaySeconds.Value, Plugin.FFAKillsToWin.Value);
    }

    internal static void PeriodicPushSettingsIfHost()
    {
        if (!HostSettingsSync.IsDue(ref _nextSettingsPushTime))
        {
            return;
        }
        PushSettingsIfHost();
    }

    internal static void OnLobbyEntered()
    {
        if (MyceliumNetwork.IsHost)
        {
            ApplySettingsFromHostConfig();
            ResetMatchState();
        }
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPCTarget(Plugin.FFAModId, nameof(Plugin.SyncFFASettings), player, ReliableType.Reliable,
            Plugin.FFAEnabled.Value, Plugin.FFARespawnDelaySeconds.Value, Plugin.FFAKillsToWin.Value);
        MyceliumNetwork.RPCTarget(Plugin.FFAModId, nameof(Plugin.SyncFFALiveState), player, ReliableType.Reliable,
            SerializeKills(), WinnerId);
    }

    internal static void ResetMatchState()
    {
        WinnerId = -1;
        Kills.Clear();
    }

    internal static void ApplyLiveState(string killsData, int winnerId)
    {
        WinnerId = winnerId;
        Kills.Clear();
        foreach (string entry in (killsData ?? string.Empty).Split(';'))
        {
            int separator = entry.IndexOf(':');
            if (separator > 0 && int.TryParse(entry.Substring(0, separator), out int id)
                && int.TryParse(entry.Substring(separator + 1), out int kills))
            {
                Kills[id] = kills;
            }
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || WinnerId >= 0 || killerId < 0 || killerId == deadPlayerId)
        {
            return;
        }

        Kills.TryGetValue(killerId, out int currentKills);
        int totalKills = currentKills + 1;
        Kills[killerId] = totalKills;
        if (totalKills >= KillsToWin)
        {
            WinnerId = killerId;
            Announce(PlayerLookup.GetPlayerNameTag(killerId) + " reached " + KillsToWin + " kills and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(killerId));
        }
        BroadcastLiveState();
    }

    internal static string SerializeKills()
    {
        StringBuilder result = new();
        foreach (KeyValuePair<int, int> entry in Kills)
        {
            if (result.Length > 0) result.Append(';');
            result.Append(entry.Key).Append(':').Append(entry.Value);
        }
        return result.ToString();
    }

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.FFAModId, nameof(Plugin.SyncFFALiveState), ReliableType.Reliable, SerializeKills(), WinnerId);
        }
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.FFAModId, nameof(Plugin.FFAAnnounce), ReliableType.Reliable, text);
        }
    }
}