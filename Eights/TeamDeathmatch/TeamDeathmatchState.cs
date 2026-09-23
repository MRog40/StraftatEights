using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

internal static class TeamDeathmatchState
{
    internal const string SettingsLobbyDataKey = "Eights_TDM_Settings";
    internal const string LiveLobbyDataKey = "Eights_TDM_Live";

    internal static bool Enabled;
    internal static readonly Dictionary<int, int> Scores = new();
    internal static int TeamCount => TeamAssignment.TeamCount;
    internal static IReadOnlyDictionary<int, int> Assignments => TeamAssignment.Current;

    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static bool _roundInitialized;
    private static bool _roundCompletionRequested;

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig()
    {
        ApplySettings(Plugin.TeamDeathmatchEnabled.Value);
    }

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.TeamDeathmatchEnabled.Value ? "1" : "0",
            "1");
        MyceliumNetwork.RPC(Plugin.TeamDeathmatchModId,
            nameof(Plugin.SyncTeamDeathmatchSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.TeamDeathmatchEnabled.Value, true);
    }

    internal static void PeriodicPushSettingsIfHost()
    {
        if (Sync.IsSettingsPushDue())
        {
            PushSettingsIfHost();
        }
    }

    internal static void PeriodicPushIfHost()
    {
        PeriodicPushSettingsIfHost();
        if (Sync.IsLivePushDue())
        {
            BroadcastLiveState();
        }
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        if (MyceliumNetwork.IsHost)
        {
            ApplySettingsFromHostConfig();
            ResetMatchState();
            PushSettingsIfHost();
            BroadcastLiveState();
        }
        else
        {
            ApplyLobbySettingsSnapshot();
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void OnLobbyLeft()
    {
        Sync.ResetForLobby();
        ResetMatchState();
    }

    internal static void PollLiveStateIfClient()
    {
        ApplyLobbyLiveSnapshot();
    }

    internal static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby)
        {
            return;
        }

        if (ModeLobbyDataSync.ContainsKey(keys, SettingsLobbyDataKey))
        {
            ApplyLobbySettingsSnapshot();
        }
        if (ModeLobbyDataSync.ContainsKey(keys, LiveLobbyDataKey))
        {
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        if (GameModeManager.Phase == GameModePhase.ActiveRound)
        {
            int playerId = ResolvePlayerId(player);
            if (TeamAssignment.AssignLatePlayer(playerId))
            {
                BroadcastLiveState();
            }
        }

        MyceliumNetwork.RPCTarget(Plugin.TeamDeathmatchModId,
            nameof(Plugin.SyncTeamDeathmatchSettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.TeamDeathmatchEnabled.Value, true);
        SendLiveStateTo(player);
    }

    internal static void OnPlayerLeft(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        int playerId = ResolvePlayerId(player);
        if (playerId >= 0 && TeamAssignment.RemovePlayer(playerId))
        {
            BroadcastLiveState();
        }
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        TeamAssignment.Reset();
        Scores.Clear();
        _roundInitialized = false;
        _roundCompletionRequested = false;
    }

    internal static void PrepareTeamsForRound()
    {
        if (MyceliumNetwork.IsHost && TeamAssignment.Current.Count == 0)
        {
            TeamAssignment.AssignForRound();
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !MyceliumNetwork.IsHost)
        {
            return;
        }

        Scores.Clear();
        _roundInitialized = false;
        _roundCompletionRequested = false;
        if (!TeamAssignment.AssignForRound())
        {
            return;
        }

        for (int teamId = 0; teamId < TeamAssignment.TeamCount; teamId++)
        {
            Scores[teamId] = 0;
        }

        _roundInitialized = true;
        BroadcastLiveState();
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !MyceliumNetwork.IsHost || !_roundInitialized
            || _roundCompletionRequested || killerId < 0 || killerId == deadPlayerId
            || !TeamAssignment.TryGetTeamId(killerId, out int teamId))
        {
            return;
        }

        Scores.TryGetValue(teamId, out int currentScore);
        int nextScore = TeamDeathmatchRules.AddKillPoints(currentScore,
            GameModeManager.EffectivePointsToWin);
        Scores[teamId] = nextScore;
        int awardedPoints = nextScore - currentScore;
        if (awardedPoints > 0)
        {
            GameModeHud.ShowScorePopupForPlayer(killerId, awardedPoints);
        }

        if (TeamDeathmatchRules.IsMatchWon(nextScore, GameModeManager.EffectivePointsToWin))
        {
            _roundCompletionRequested = true;
            BroadcastLiveState();
            GameModeManager.CompleteCustomRound(teamId);
            return;
        }

        BroadcastLiveState();
    }

    internal static int GetScore(int teamId)
    {
        return Scores.TryGetValue(teamId, out int score) ? score : 0;
    }

    internal static void ApplyLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int roundId, int revision,
        string source = "rpc")
    {
        if (teamCount < 2 || teamCount > 3
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        TeamAssignment.ApplySnapshot(assignmentsData, teamCount);
        Scores.Clear();
        foreach (KeyValuePair<int, int> score in ScoreCodec.Parse(
            scoresData, GameModeManager.EffectivePointsToWin))
        {
            if (score.Key >= 0 && score.Key < teamCount)
            {
                Scores[score.Key] = score.Value;
            }
        }
        _roundInitialized = true;
    }

    private static void BroadcastLiveState()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        int revision = Sync.NextLiveRevision();
        string assignmentsData = TeamRules.SerializeAssignments(TeamAssignment.Current);
        string scoresData = ScoreCodec.Serialize(Scores);
        ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, assignmentsData, TeamCount.ToString(), scoresData);
        MyceliumNetwork.RPC(Plugin.TeamDeathmatchModId,
            nameof(Plugin.SyncTeamDeathmatchLiveState), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, assignmentsData, TeamCount, scoresData,
            GameModeManager.RoundId, revision);
    }

    private static void SendLiveStateTo(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.TeamDeathmatchModId,
            nameof(Plugin.SyncTeamDeathmatchLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, TeamRules.SerializeAssignments(TeamAssignment.Current),
            TeamCount, ScoreCodec.Serialize(Scores), GameModeManager.RoundId,
            Sync.LiveRevision);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 2, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !LobbySnapshotCodec.TryParseBool(fields[1], out _)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("tdm", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 3, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[1], out int teamCount))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], teamCount, fields[2], roundId, revision,
            ModeLobbyDataSync.Source("tdm", "live"));
    }

    private static int ResolvePlayerId(CSteamID steam)
    {
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client && client.PlayerSteamID == steam.m_SteamID)
            {
                return client.PlayerId;
            }
        }

        return -1;
    }
}