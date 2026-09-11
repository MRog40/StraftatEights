using System;
using System.Collections;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class DefaultGameModeState
{
    internal const string LiveLobbyDataKey = "StraftatEights_Default_Live";
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static int AliveCount => AlivePlayers.Count;
    internal static int SubRoundId { get; private set; }
    internal static int WinnerId { get; private set; } = -1;
    internal static readonly Dictionary<int, int> Scores = new();

    private static readonly HashSet<int> AlivePlayers = new();
    private static readonly HashSet<int> RoundPlayers = new();
    private static readonly ModeSyncState Sync = new();
    private static bool _subRoundEnding;
    private static bool _startRetryPending;

    internal static void PeriodicPushIfHost()
    {
        if (MyceliumNetwork.IsHost && Sync.IsLivePushDue())
        {
            BroadcastLiveState();
        }
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        if (!MyceliumNetwork.IsHost)
        {
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !ModeLobbyDataSync.ContainsKey(keys, LiveLobbyDataKey))
        {
            return;
        }

        ApplyLobbyLiveSnapshot();
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        SubRoundId = 0;
        WinnerId = -1;
        _subRoundEnding = false;
        _startRetryPending = false;
        AlivePlayers.Clear();
        RoundPlayers.Clear();
        Scores.Clear();
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.DefaultGameModeModId,
            nameof(Plugin.SyncDefaultGameModeLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, SerializeScores(), SerializeAlive(), SubRoundId,
            WinnerId, GameModeManager.RoundId, Sync.LiveRevision);

        if (SubRoundId > 0 && WinnerId < 0 && !_subRoundEnding)
        {
            int playerId = FindPlayerId(player);
            if (playerId >= 0)
            {
                RoundPlayers.Add(playerId);
                AlivePlayers.Add(playerId);
                Scores.TryAdd(playerId, 0);
                BroadcastLiveState();
            }
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Plugin.DefaultGameModeEnabled.Value || !GameModeManager.IsActive(GameMode.Default)
            || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetMatchState();
        StartSubRound();
    }

    internal static void ApplyLiveState(CSteamID hostId, string scoresData, string aliveData,
        int subRoundId, int winnerId, int roundId, int revision, string source = "rpc")
    {
        int previousRoundId = Sync.LastLiveRoundId;
        if (subRoundId < 0 || winnerId < -1
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        SubRoundId = roundId != previousRoundId ? subRoundId : Math.Max(SubRoundId, subRoundId);
        WinnerId = winnerId;
        Scores.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(scoresData, PointsToWin))
        {
            Scores[entry.Key] = entry.Value;
        }

        AlivePlayers.Clear();
        foreach (int playerId in ParseIds(aliveData))
        {
            AlivePlayers.Add(playerId);
        }
    }

    internal static void OnServerKill(int deadPlayerId)
    {
        if (!Plugin.DefaultGameModeEnabled.Value || !GameModeManager.IsActive(GameMode.Default)
            || WinnerId >= 0 || _subRoundEnding)
        {
            return;
        }

        if (!AlivePlayers.Remove(deadPlayerId))
        {
            return;
        }

        int winningTeamId = GetOnlyAliveTeamId();
        if (winningTeamId < 0)
        {
            BroadcastLiveState();
            return;
        }

        List<int> winners = GetAlivePlayersOnTeam(winningTeamId);
        foreach (int playerId in winners)
        {
            AwardScore(playerId, ScoreRules.PointsPerRoundWin);
        }

        BroadcastLiveState();
        if (WinnerId >= 0)
        {
            string winnerName = winners.Count == 1
                ? PlayerLookup.GetPlayerNameTag(winners[0])
                : "The winning team";
            Announce(winnerName + " reached " + PointsToWin + " points and won the round!");
            GameModeManager.CompleteCustomRound(winningTeamId);
            return;
        }

        BeginNextSubRound();
    }

    private static void StartSubRound()
    {
        if (WinnerId >= 0 || !MyceliumNetwork.IsHost)
        {
            return;
        }

        List<int> players = new();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                players.Add(client.PlayerId);
            }
        }

        if (players.Count == 0)
        {
            ScheduleStartRetry();
            return;
        }

        SubRoundId++;
        _subRoundEnding = false;
        AlivePlayers.Clear();
        RoundPlayers.Clear();
        foreach (int playerId in players)
        {
            AlivePlayers.Add(playerId);
            RoundPlayers.Add(playerId);
            Scores.TryAdd(playerId, 0);
        }

        BroadcastLiveState();
    }

    private static void BeginNextSubRound()
    {
        if (_subRoundEnding || Plugin.Instance == null)
        {
            return;
        }

        _subRoundEnding = true;
        foreach (int playerId in RoundPlayers)
        {
            if (ClientInstance.playerInstances.ContainsKey(playerId))
            {
                GameModeRespawn.Schedule(playerId, GameModeManager.EffectiveRespawnDelaySeconds);
            }
        }

        Plugin.Instance.StartCoroutine(StartNextSubRoundAfterRespawn(
            GameModeManager.EffectiveRespawnDelaySeconds + 0.75f,
            SessionState.Generation, GameModeManager.RoundId));
    }

    private static IEnumerator StartNextSubRoundAfterRespawn(float delay, int sessionGeneration,
        int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Default))
        {
            yield break;
        }

        StartSubRound();
    }

    private static void ScheduleStartRetry()
    {
        if (_startRetryPending || Plugin.Instance == null)
        {
            return;
        }

        _startRetryPending = true;
        Plugin.Instance.StartCoroutine(RetryStartSubRound(SessionState.Generation,
            GameModeManager.RoundId, SubRoundId));
    }

    private static IEnumerator RetryStartSubRound(int sessionGeneration, int roundId,
        int previousSubRoundId)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            yield return new WaitForSeconds(0.25f);
            if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
                || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Default))
            {
                break;
            }

            StartSubRound();
            if (SubRoundId > previousSubRoundId)
            {
                break;
            }
        }

        _startRetryPending = false;
    }

    private static int GetOnlyAliveTeamId()
    {
        int teamId = -1;
        foreach (int playerId in AlivePlayers)
        {
            if (ScoreManager.Instance == null)
            {
                return -1;
            }

            int playerTeamId = ScoreManager.Instance.GetTeamId(playerId);
            if (teamId < 0)
            {
                teamId = playerTeamId;
            }
            else if (teamId != playerTeamId)
            {
                return -1;
            }
        }

        return teamId;
    }

    private static int FindPlayerId(CSteamID steamId)
    {
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client && client.PlayerSteamID == steamId.m_SteamID)
            {
                return client.PlayerId;
            }
        }
        return -1;
    }

    private static List<int> GetAlivePlayersOnTeam(int teamId)
    {
        List<int> winners = new();
        foreach (int playerId in AlivePlayers)
        {
            if (ScoreManager.Instance != null && ScoreManager.Instance.GetTeamId(playerId) == teamId)
            {
                winners.Add(playerId);
            }
        }
        return winners;
    }

    private static void AwardScore(int playerId, int amount)
    {
        Scores.TryGetValue(playerId, out int currentScore);
        int nextScore = currentScore + amount;
        Scores[playerId] = nextScore;
        GameModeHud.ShowScorePopupForPlayer(playerId, amount);
        if (WinnerId < 0 && nextScore >= PointsToWin)
        {
            WinnerId = playerId;
        }
    }

    private static string SerializeScores() => ScoreCodec.Serialize(Scores);

    private static string SerializeAlive() => string.Join(",", AlivePlayers);

    private static IEnumerable<int> ParseIds(string data)
    {
        foreach (string value in (data ?? string.Empty).Split(',', ';'))
        {
            if (int.TryParse(value, out int playerId) && playerId >= 0)
            {
                yield return playerId;
            }
        }
    }

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            int revision = Sync.NextLiveRevision();
            ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
                GameModeManager.RoundId, revision, SerializeScores(), SerializeAlive(),
                SubRoundId.ToString(), WinnerId.ToString());
            MyceliumNetwork.RPC(Plugin.DefaultGameModeModId,
                nameof(Plugin.SyncDefaultGameModeLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeScores(), SerializeAlive(), SubRoundId,
                WinnerId, GameModeManager.RoundId, revision);
        }
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 4, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[2], out int subRoundId)
            || !int.TryParse(fields[3], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], fields[1], subRoundId, winnerId, roundId, revision,
            ModeLobbyDataSync.Source("default", "live"));
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.DefaultGameModeModId,
                nameof(Plugin.DefaultGameModeAnnounce), ReliableType.Reliable, text);
        }
    }
}