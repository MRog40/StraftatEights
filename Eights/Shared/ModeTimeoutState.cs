using System;
using System.Collections.Generic;
using System.Globalization;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class ModeTimeoutState
{
    internal const string LiveLobbyDataKey = "Eights_ModeTimeout_Live";
    internal static float TimeRemaining { get; private set; }
    internal static bool IsSuddenDeath { get; private set; }
    internal static float RoundElapsedSeconds
    {
        get
        {
            float duration = GetDuration(GameModeManager.ActiveMode);
            return Mathf.Clamp(duration - TimeRemaining, 0f, duration);
        }
    }

    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static readonly Dictionary<int, int> SuddenDeathScores = new();
    private static bool _hasSuddenDeathSnapshot;

    internal static bool IsTimedMode(GameMode mode)
    {
        return mode == GameMode.Chambertat
            || mode == GameMode.Ffatat
            || mode == GameMode.Nifetat
            || mode == GameMode.Juggertat
            || mode == GameMode.Guntat
            || mode == GameMode.Snipertat
            || mode == GameMode.Ratatat
            || mode == GameMode.Potatotat
            || mode == GameMode.Hvtat
            || mode == GameMode.Tdmtat
            || mode == GameMode.Infectedtat
            || mode == GameMode.PotatoInftat;
    }

    internal static void OnTakeStarted()
    {
        if (GameModeManager.ActiveMode != GameMode.Chambertat)
        {
            return;
        }

        TimeRemaining = ModeTimeoutRules.DefaultRoundSeconds;
        IsSuddenDeath = false;
        SuddenDeathScores.Clear();
        _hasSuddenDeathSnapshot = false;
        if (MyceliumNetwork.IsHost)
        {
            BroadcastLiveState();
        }
    }

    internal static string GetScoreboardText()
    {
        if (!IsTimedMode(GameModeManager.ActiveMode)
            || GameModeManager.Phase != GameModePhase.ActiveRound)
        {
            return string.Empty;
        }

        return IsSuddenDeath
            ? "Sudden Death"
            : (GameModeManager.ActiveMode == GameMode.Chambertat
                ? "Take: "
                : "Round: ") + Mathf.CeilToInt(TimeRemaining) + "s";
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        ResetMatchState();
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        TimeRemaining = 0f;
        IsSuddenDeath = false;
        SuddenDeathScores.Clear();
        _hasSuddenDeathSnapshot = false;
    }

    internal static void OnRoundStarted()
    {
        TimeRemaining = 0f;
        IsSuddenDeath = false;
        SuddenDeathScores.Clear();
        _hasSuddenDeathSnapshot = false;
        if (!IsTimedMode(GameModeManager.ActiveMode))
        {
            return;
        }

        TimeRemaining = GetDuration(GameModeManager.ActiveMode);
        if (MyceliumNetwork.IsHost)
        {
            BroadcastLiveState();
        }
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !IsTimedMode(GameModeManager.ActiveMode)
            || !GameModeManager.IsRoundGameplayActive)
        {
            return;
        }

        if (IsSuddenDeath)
        {
            ResolveSuddenDeathScore();
            return;
        }

        TimeRemaining = Mathf.Max(0f, TimeRemaining - Mathf.Max(0f, deltaTime));
        if (TimeRemaining <= 0f)
        {
            ResolveTimeout();
        }
    }

    internal static void ClientTick(float deltaTime)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !IsTimedMode(GameModeManager.ActiveMode)
            || !GameModeManager.IsRoundGameplayActive
            || IsSuddenDeath)
        {
            return;
        }

        TimeRemaining = Mathf.Max(0f, TimeRemaining - Mathf.Max(0f, deltaTime));
    }

    internal static void PeriodicPushIfHost()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost
            && IsTimedMode(GameModeManager.ActiveMode) && Sync.IsLivePushDue())
        {
            BroadcastLiveState();
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

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(GameModeManager.ModId, nameof(Plugin.SyncModeTimeout), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, TimeRemaining, IsSuddenDeath,
            GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static void ApplyLiveState(CSteamID hostId, float timeRemaining, bool suddenDeath,
        int roundId, int revision, string source = "rpc")
    {
        float maxRoundSeconds = GetDuration(GameModeManager.ActiveMode);
        if (timeRemaining < 0f || timeRemaining > maxRoundSeconds
            || float.IsNaN(timeRemaining) || float.IsInfinity(timeRemaining)
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        TimeRemaining = timeRemaining;
        IsSuddenDeath = suddenDeath;
        if (IsSuddenDeath && !_hasSuddenDeathSnapshot)
        {
            CaptureSuddenDeathScores();
        }
    }

    private static void ResolveTimeout()
    {
        if (GameModeManager.ActiveMode == GameMode.Infectedtat)
        {
            InfectedtatState.OnRoundTimeout();
            return;
        }

        if (GameModeManager.ActiveMode == GameMode.PotatoInftat)
        {
            PotatoInftatState.OnRoundTimeout();
            return;
        }

        if (GameModeManager.ActiveMode == GameMode.Chambertat)
        {
            ChambertatState.OnTakeTimeout();
            return;
        }

        if (TryGetTimeoutWinner(out int winnerId, out bool teamScores))
        {
            int winningTeamId = GetWinningTeamId(winnerId, teamScores);
            if (winningTeamId >= 0)
            {
                string winnerLabel = teamScores
                    ? TeamDisplayNames.Get(winnerId)
                    : PlayerLookup.GetPlayerNameTag(winnerId);
                GameModeHud.BroadcastTakeResult("<b>Time expired</b>\n<i>"
                    + winnerLabel + " won on score</i>");
                BroadcastLiveState();
                GameModeManager.CompleteCustomRound(winningTeamId);
                return;
            }
        }

        IsSuddenDeath = true;
        CaptureSuddenDeathScores();
        GameModeHud.BroadcastTakeResult("<b>Sudden death</b>\n<i>Next score wins</i>");
        BroadcastLiveState();
    }

    private static void ResolveSuddenDeathScore()
    {
        if (!_hasSuddenDeathSnapshot || !TryGetScores(out IReadOnlyDictionary<int, int> scores,
                out bool teamScores))
        {
            return;
        }

        foreach (KeyValuePair<int, int> entry in scores)
        {
            SuddenDeathScores.TryGetValue(entry.Key, out int previousScore);
            if (entry.Value <= previousScore)
            {
                continue;
            }

            int winningTeamId = GetWinningTeamId(entry.Key, teamScores);
            if (winningTeamId < 0)
            {
                return;
            }

            string winnerLabel = teamScores
                ? TeamDisplayNames.Get(entry.Key)
                : PlayerLookup.GetPlayerNameTag(entry.Key);
            GameModeHud.BroadcastTakeResult("<b>" + winnerLabel
                + " won sudden death</b>");
            BroadcastLiveState();
            GameModeManager.CompleteCustomRound(winningTeamId);
            return;
        }

        CaptureSuddenDeathScores();
    }

    private static bool TryGetTimeoutWinner(out int winnerId, out bool teamScores)
    {
        winnerId = -1;
        teamScores = false;
        if (!TryGetScores(out IReadOnlyDictionary<int, int> scores, out teamScores))
        {
            return false;
        }

        return ModeTimeoutRules.TryGetUniqueLeader(scores, out winnerId);
    }

    private static int GetWinningTeamId(int winnerId, bool teamScores)
    {
        if (teamScores)
        {
            return winnerId;
        }

        return winnerId;
    }

    private static void CaptureSuddenDeathScores()
    {
        SuddenDeathScores.Clear();
        if (TryGetScores(out IReadOnlyDictionary<int, int> scores, out _))
        {
            foreach (KeyValuePair<int, int> entry in scores)
            {
                SuddenDeathScores[entry.Key] = entry.Value;
            }
        }
        _hasSuddenDeathSnapshot = true;
    }

    private static bool TryGetScores(out IReadOnlyDictionary<int, int> scores,
        out bool teamScores)
    {
        teamScores = false;
        switch (GameModeManager.ActiveMode)
        {
            case GameMode.Ffatat:
                scores = FfatatState.Kills;
                return true;
            case GameMode.Nifetat:
                scores = NifetatState.Kills;
                return true;
            case GameMode.Juggertat:
                scores = JuggertatState.Points;
                return true;
            case GameMode.Guntat:
                scores = GuntatState.Progress;
                return true;
            case GameMode.Snipertat:
                scores = SnipertatState.Points;
                return true;
            case GameMode.Ratatat:
                scores = RatatatState.Points;
                return true;
            case GameMode.Chambertat:
                scores = ChambertatState.Scores;
                return true;
            case GameMode.Potatotat:
                scores = PotatotatState.Kills;
                return true;
            case GameMode.Assassintat:
                scores = AssassintatState.Scores;
                return true;
            case GameMode.Hvtat:
                scores = HvtatState.Points;
                return true;
            case GameMode.Tdmtat:
                scores = TdmtatState.Scores;
                teamScores = true;
                return true;
            case GameMode.Infectedtat:
                scores = InfectedtatState.Scores;
                return true;
            case GameMode.PotatoInftat:
                scores = PotatoInftatState.Scores;
                return true;
            default:
                scores = new Dictionary<int, int>();
                return false;
        }
    }

    private static void BroadcastLiveState()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        int revision = Sync.NextLiveRevision();
        ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision,
            TimeRemaining.ToString(CultureInfo.InvariantCulture),
            IsSuddenDeath ? "1" : "0");
        MyceliumNetwork.RPC(GameModeManager.ModId, nameof(Plugin.SyncModeTimeout),
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, TimeRemaining, IsSuddenDeath,
            GameModeManager.RoundId, revision);
    }

    private static float GetDuration(GameMode mode)
    {
        return mode == GameMode.Chambertat
            ? ModeTimeoutRules.DefaultRoundSeconds
            : ModeTimeoutRules.LongRoundSeconds;
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 2, out CSteamID hostId,
                out int roundId, out int revision, out string[] fields)
            || !float.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture,
                out float timeRemaining)
            || !LobbySnapshotCodec.TryParseBool(fields[1], out bool suddenDeath))
        {
            return;
        }

        ApplyLiveState(hostId, timeRemaining, suddenDeath, roundId, revision,
            ModeLobbyDataSync.Source("mode-timeout", "live"));
    }
}