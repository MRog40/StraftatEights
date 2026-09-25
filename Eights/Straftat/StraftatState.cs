using System;
using System.Collections;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class StraftatState
{
    internal const string LiveLobbyDataKey = "Eights_Default_Live";
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static int AliveCount => AlivePlayers.Count;
    internal static int TakeId { get; private set; }
    internal static int WinnerId { get; private set; } = -1;
    internal static float TimeRemaining => Mathf.Max(0f, _timeRemaining);
    internal static readonly Dictionary<int, int> Scores = new();

    private static readonly HashSet<int> AlivePlayers = new();
    private static readonly HashSet<int> RoundPlayers = new();
    private static readonly ModeSyncState Sync = new();
    private static float _timeRemaining;
    private static bool _takeEnding;
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
        if (MyceliumNetwork.IsHost)
        {
            ResetMatchState();
            BroadcastLiveState();
        }
        else
        {
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void PollLiveStateIfClient()
    {
        ApplyLobbyLiveSnapshot();
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
        TakeId = 0;
        WinnerId = -1;
        _timeRemaining = 0f;
        _takeEnding = false;
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

        MyceliumNetwork.RPCTarget(Plugin.StraftatModId,
            nameof(Plugin.SyncStraftatLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, SerializeScores(), SerializeAlive(), TakeId,
            WinnerId, TimeRemaining, GameModeManager.RoundId, Sync.LiveRevision);

        if (TakeId > 0 && WinnerId < 0 && !_takeEnding)
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

    internal static void OnPlayerLeft(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        int playerId = PlayerLookup.FindPlayerId(player);
        bool wasAlive = playerId >= 0 && AlivePlayers.Contains(playerId);
        bool changed = playerId >= 0 && (wasAlive || RoundPlayers.Remove(playerId)
            || Scores.Remove(playerId));
        if (wasAlive && GameModeManager.IsActive(GameMode.Straftat)
            && WinnerId < 0 && !_takeEnding)
        {
            OnServerKill(playerId);
        }

        if (playerId >= 0)
        {
            AlivePlayers.Remove(playerId);
            RoundPlayers.Remove(playerId);
            Scores.Remove(playerId);
        }
        if (changed)
        {
            BroadcastLiveState();
        }
    }

    internal static void OnRoundStarted()
    {
        if (!GameModeManager.IsModeEnabledForCurrentRound(GameMode.Straftat,
                Plugin.StraftatEnabled.Value)
            || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetMatchState();
        StartTake();
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!GameModeManager.IsModeEnabledForCurrentRound(GameMode.Straftat,
            Plugin.StraftatEnabled.Value) || !MyceliumNetwork.IsHost
            || !GameModeManager.IsActive(GameMode.Straftat)
            || !GameModeManager.IsRoundGameplayActive
            || WinnerId >= 0 || TakeId <= 0)
        {
            return;
        }

        _timeRemaining = Mathf.Max(0f, _timeRemaining - Mathf.Max(0f, deltaTime));
        if (_timeRemaining <= 0f)
        {
            CompleteTimeout();
        }
    }

    internal static void ClientTick(float deltaTime)
    {
        if (MyceliumNetwork.IsHost
            || !GameModeManager.IsModeEnabledForCurrentRound(GameMode.Straftat,
                Plugin.StraftatEnabled.Value)
            || !GameModeManager.IsActive(GameMode.Straftat)
            || !GameModeManager.IsRoundGameplayActive
            || WinnerId >= 0 || TakeId <= 0)
        {
            return;
        }

        _timeRemaining = Mathf.Max(0f, _timeRemaining - Mathf.Max(0f, deltaTime));
    }

    internal static bool HandleVoidFall(FirstPersonController controller)
    {
        if (!GameModeManager.IsModeEnabledForCurrentRound(GameMode.Straftat,
                Plugin.StraftatEnabled.Value)
            || TakeId <= 0 || controller == null || !controller
            || controller.transform.position.y >= -300f)
        {
            return false;
        }

        PlayerHealth? health = controller.GetComponent<PlayerHealth>();
        if (health == null || !health || health.sync___get_value_health() <= 0f)
        {
            return false;
        }

        health.fellVoid = true;
        float lethalDamage = health.sync___get_value_health() + 1f;
        if (health.IsServer)
        {
            if (!FishNetCompatibility.TryRemoveHealth(health, lethalDamage))
            {
                return false;
            }
        }
        else
        {
            health.RemoveHealth(lethalDamage);
        }

        controller.transform.position = new Vector3(controller.transform.position.x, -299f,
            controller.transform.position.z);
        return true;
    }

    internal static void ApplyLiveState(CSteamID hostId, string scoresData, string aliveData,
        int takeId, int winnerId, float timeRemaining, int roundId, int revision,
        string source = "rpc")
    {
        int previousRoundId = Sync.LastLiveRoundId;
        if (takeId < 0 || winnerId < -1 || timeRemaining < 0f
            || float.IsNaN(timeRemaining) || float.IsInfinity(timeRemaining)
            || timeRemaining > ModeTimeoutRules.DefaultRoundSeconds
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        TakeId = roundId != previousRoundId ? takeId : Math.Max(TakeId, takeId);
        WinnerId = winnerId;
        Scores.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(scoresData, PointsToWin))
        {
            Scores[entry.Key] = entry.Value;
        }

        _timeRemaining = timeRemaining;
        AlivePlayers.Clear();
        foreach (int playerId in ParseIds(aliveData))
        {
            AlivePlayers.Add(playerId);
        }
    }

    internal static void OnServerKill(int deadPlayerId)
    {
        if (!GameModeManager.IsModeEnabledForCurrentRound(GameMode.Straftat,
                Plugin.StraftatEnabled.Value)
            || WinnerId >= 0 || _takeEnding)
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

        string winnerLabel = winners.Count == 1
            ? PlayerLookup.GetPlayerNameTag(winners[0])
            : TeamDisplayNames.Get(winningTeamId);
        GameModeHud.BroadcastTakeResult("<b>" + winnerLabel
            + " won the take</b>\n<i>Last team standing</i>");
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

        BeginNextTake();
    }

    private static void StartTake()
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

        TakeId++;
        _takeEnding = false;
        _timeRemaining = ModeTimeoutRules.DefaultRoundSeconds;
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

    private static void BeginNextTake()
    {
        if (_takeEnding || Plugin.Instance == null)
        {
            return;
        }

        _takeEnding = true;
        foreach (int playerId in RoundPlayers)
        {
            if (ClientInstance.playerInstances.ContainsKey(playerId))
            {
                GameModeRespawn.Schedule(playerId, GameModeManager.EffectiveRespawnDelaySeconds,
                    protectOnRespawn: false);
            }
        }

        Plugin.Instance.StartCoroutine(StartNextTakeAfterRespawn(
            GameModeManager.EffectiveRespawnDelaySeconds + 0.75f,
            SessionState.Generation, GameModeManager.RoundId));
    }

    private static IEnumerator StartNextTakeAfterRespawn(float delay, int sessionGeneration,
        int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Straftat))
        {
            yield break;
        }

        StartTake();
    }

    private static void CompleteTimeout()
    {
        if (WinnerId >= 0 || _takeEnding)
        {
            return;
        }

        GameModeHud.BroadcastTakeResult("<b>The take ended</b>\n<i>Time expired</i>");
        BroadcastLiveState();
        BeginNextTake();
    }

    private static void ScheduleStartRetry()
    {
        if (_startRetryPending || Plugin.Instance == null)
        {
            return;
        }

        _startRetryPending = true;
        Plugin.Instance.StartCoroutine(RetryStartTake(SessionState.Generation,
            GameModeManager.RoundId, TakeId));
    }

    private static IEnumerator RetryStartTake(int sessionGeneration, int roundId,
        int previousTakeId)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            yield return new WaitForSeconds(0.25f);
            if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
                || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Straftat))
            {
                break;
            }

            StartTake();
            if (TakeId > previousTakeId)
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

            int playerTeamId = TeamAssignment.ResolveTeamId(playerId);
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
            if (TeamAssignment.ResolveTeamId(playerId) == teamId)
            {
                winners.Add(playerId);
            }
        }
        return winners;
    }

    private static void AwardScore(int playerId, int amount)
    {
        Scores.TryGetValue(playerId, out int currentScore);
        int nextScore = ScoreRules.AddPoints(currentScore, amount, PointsToWin);
        int awardedPoints = nextScore - currentScore;
        Scores[playerId] = nextScore;
        if (awardedPoints > 0)
        {
            GameModeHud.ShowScorePopupForPlayer(playerId, awardedPoints);
        }
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
                TakeId.ToString(), WinnerId.ToString(), TimeRemaining.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            MyceliumNetwork.RPC(Plugin.StraftatModId,
                nameof(Plugin.SyncStraftatLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeScores(), SerializeAlive(), TakeId,
                WinnerId, TimeRemaining, GameModeManager.RoundId, revision);
        }
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 5, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[2], out int takeId)
            || !int.TryParse(fields[3], out int winnerId)
            || !float.TryParse(fields[4], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float timeRemaining))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], fields[1], takeId, winnerId, timeRemaining,
            roundId, revision,
            ModeLobbyDataSync.Source("straftat", "live"));
    }

    private static void Announce(string text)
    {
        GameModeHud.BroadcastAnnouncement(text);
    }
}