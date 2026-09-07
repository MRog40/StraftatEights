using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class FFAState
{
    internal static bool Enabled;
    internal static int KillsToWin = 10;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Kills = new();

    private static float _nextSettingsPushTime;
    private static float _nextLiveStatePushTime;
    private static int _settingsRevision;
    private static int _lastSettingsRoundId = -1;
    private static int _lastSettingsRevision = -1;
    private static int _liveStateRevision;
    private static int _lastLiveStateRoundId = -1;
    private static int _lastLiveStateRevision = -1;

    internal static void ApplySettings(bool enabled, int killsToWin)
    {
        killsToWin = Mathf.Clamp(killsToWin, 3, 30);
        bool changed = Enabled != enabled || KillsToWin != killsToWin;
        Enabled = enabled;
        KillsToWin = killsToWin;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig()
    {
        ApplySettings(Plugin.FFAEnabled.Value, Plugin.FFAKillsToWin.Value);
    }

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplySettingsFromHostConfig();
        MyceliumNetwork.RPC(Plugin.FFAModId, nameof(Plugin.SyncFFASettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, ++_settingsRevision,
            Plugin.FFAEnabled.Value, Plugin.FFAKillsToWin.Value);
    }

    internal static void PeriodicPushSettingsIfHost()
    {
        if (!HostSettingsSync.IsDue(ref _nextSettingsPushTime))
        {
            return;
        }
        PushSettingsIfHost();
    }

       internal static void PeriodicPushIfHost()
       {
           PeriodicPushSettingsIfHost();
           if (HostSettingsSync.IsDue(ref _nextLiveStatePushTime))
           {
               BroadcastLiveState();
           }
       }

    internal static void OnLobbyEntered()
    {
        _lastSettingsRoundId = -1;
        _lastSettingsRevision = -1;
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
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, _settingsRevision,
            Plugin.FFAEnabled.Value, Plugin.FFAKillsToWin.Value);
        MyceliumNetwork.RPCTarget(Plugin.FFAModId, nameof(Plugin.SyncFFALiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, SerializeKills(), WinnerId, GameModeManager.RoundId, _liveStateRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastSettingsRoundId, ref _lastSettingsRevision);
    }

    internal static void ResetMatchState()
    {
        _liveStateRevision++;
        _lastLiveStateRoundId = -1;
        _lastLiveStateRevision = -1;
        WinnerId = -1;
        Kills.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string killsData, int winnerId, int roundId, int revision)
    {
        if (winnerId < -1)
        {
            return;
        }
        if (!SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastLiveStateRoundId, ref _lastLiveStateRevision))
        {
            return;
        }
        WinnerId = winnerId;
        Kills.Clear();
        foreach (string entry in (killsData ?? string.Empty).Split(';'))
        {
            int separator = entry.IndexOf(':');
            if (separator > 0 && int.TryParse(entry.Substring(0, separator), out int id)
                && int.TryParse(entry.Substring(separator + 1), out int kills)
                && id >= 0 && kills >= 0 && kills <= KillsToWin)
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
            _liveStateRevision++;
            MyceliumNetwork.RPC(Plugin.FFAModId, nameof(Plugin.SyncFFALiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeKills(), WinnerId, GameModeManager.RoundId, _liveStateRevision);
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