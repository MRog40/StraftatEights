using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class FFAState
{
    internal static bool Enabled;
    internal static int KillsToWin => GameModeManager.EffectivePointsToWin;
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

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.FFAEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplySettingsFromHostConfig();
        MyceliumNetwork.RPC(Plugin.FFAModId, nameof(Plugin.SyncFFASettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, ++_settingsRevision, Plugin.FFAEnabled.Value);
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
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, _settingsRevision, Plugin.FFAEnabled.Value);
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
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(killsData, KillsToWin))
        {
            Kills[entry.Key] = entry.Value;
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || WinnerId >= 0 || killerId < 0 || killerId == deadPlayerId)
        {
            return;
        }

        Kills.TryGetValue(killerId, out int currentKills);
        int totalKills = currentKills + ScoreRules.PointsPerKill;
        Kills[killerId] = totalKills;
        GameModeHud.ShowScorePopupForPlayer(killerId, ScoreRules.PointsPerKill);
        if (totalKills >= KillsToWin)
        {
            WinnerId = killerId;
            Announce(PlayerLookup.GetPlayerNameTag(killerId) + " reached " + KillsToWin + " points and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(killerId));
        }
        BroadcastLiveState();
    }

    internal static string SerializeKills()
    {
        return ScoreCodec.Serialize(Kills);
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