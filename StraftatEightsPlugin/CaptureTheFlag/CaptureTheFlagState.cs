using System;
using System.Collections.Generic;
using System.Globalization;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class CaptureTheFlagState
{
    internal const string SettingsLobbyDataKey = "StraftatEights_CTF_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_CTF_Live";
    internal const float MatchDurationMultiplier = CaptureTheFlagRules.MatchDurationMultiplier;
    internal const int CapturePoints = CaptureTheFlagRules.CapturePoints;
    internal const float FlagTouchRadius = 1.5f;
    internal const float ServerTickIntervalSeconds = 0.1f;

    internal static bool Enabled;
    internal static readonly Dictionary<int, int> Scores = new();
    internal static float MatchTimeRemaining { get; private set; }
    internal static bool IsSuddenDeath { get; private set; }
    internal static int TeamCount => TeamAssignment.TeamCount;
    internal static IReadOnlyDictionary<int, int> Assignments => TeamAssignment.Current;

    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static readonly CaptureTheFlagFlagStatus[] FlagStatuses =
        new CaptureTheFlagFlagStatus[2];
    private static readonly int[] FlagCarriers = { -1, -1 };
    private static readonly Vector3[] FlagPositions = new Vector3[2];
    private static readonly int[] FlagTeamIds = { -1, -1 };
    private static float _serverTickAccumulator;
    private static bool _roundCompletionRequested;

    internal static CaptureTheFlagFlagStatus GetFlagStatus(int flagIndex)
    {
        return flagIndex >= 0 && flagIndex < FlagStatuses.Length
            ? FlagStatuses[flagIndex]
            : CaptureTheFlagFlagStatus.Home;
    }

    internal static int GetFlagCarrier(int flagIndex)
    {
        return flagIndex >= 0 && flagIndex < FlagCarriers.Length
            ? FlagCarriers[flagIndex]
            : -1;
    }

    internal static int GetFlagTeam(int flagIndex)
    {
        return flagIndex >= 0 && flagIndex < FlagTeamIds.Length
            ? FlagTeamIds[flagIndex]
            : -1;
    }

    internal static bool TryGetFlagPosition(int flagIndex, out Vector3 position)
    {
        position = default;
        if (flagIndex < 0 || flagIndex >= FlagStatuses.Length || FlagTeamIds[flagIndex] < 0)
        {
            return false;
        }

        if (FlagStatuses[flagIndex] == CaptureTheFlagFlagStatus.Carried)
        {
            PlayerHealth? carrier = PlayerLookup.FindActivePlayerHealthById(FlagCarriers[flagIndex]);
            if (carrier != null && carrier && carrier.gameObject.activeInHierarchy)
            {
                position = carrier.transform.position;
                return true;
            }
        }

        position = FlagPositions[flagIndex];
        return true;
    }

    internal static bool TryGetEnemyFlagPosition(int playerId, out Vector3 position)
    {
        position = default;
        if (!TeamAssignment.TryGetTeamId(playerId, out int teamId))
        {
            return false;
        }

        for (int flagIndex = 0; flagIndex < FlagTeamIds.Length; flagIndex++)
        {
            if (FlagTeamIds[flagIndex] != teamId
                && TryGetFlagPosition(flagIndex, out position))
            {
                return true;
            }
        }

        return false;
    }

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
        ApplySettings(Plugin.CaptureTheFlagEnabled.Value);
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
            GameModeManager.RoundId, revision, Plugin.CaptureTheFlagEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.CaptureTheFlagModId,
            nameof(Plugin.SyncCaptureTheFlagSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.CaptureTheFlagEnabled.Value);
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
            TeamAssignment.AssignLatePlayer(ResolvePlayerId(player));
            BroadcastLiveState();
        }

        MyceliumNetwork.RPCTarget(Plugin.CaptureTheFlagModId,
            nameof(Plugin.SyncCaptureTheFlagSettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.CaptureTheFlagEnabled.Value);
        SendLiveStateTo(player);
    }

    internal static void OnPlayerLeft(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        int playerId = ResolvePlayerId(player);
        if (playerId < 0)
        {
            return;
        }

        bool changed = false;
        for (int flagIndex = 0; flagIndex < FlagCarriers.Length; flagIndex++)
        {
            if (FlagStatuses[flagIndex] == CaptureTheFlagFlagStatus.Carried
                && FlagCarriers[flagIndex] == playerId)
            {
                ReturnFlagHome(flagIndex);
                changed = true;
            }
        }

        if (changed)
        {
            BroadcastLiveState();
        }
    }

    internal static void PollLiveStateIfClient()
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !GameModeManager.IsActive(GameMode.CaptureTheFlag))
        {
            return;
        }

        ApplyLobbyLiveSnapshot();
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
        MatchTimeRemaining = 0f;
        IsSuddenDeath = false;
        _serverTickAccumulator = 0f;
        _roundCompletionRequested = false;
        for (int flagIndex = 0; flagIndex < FlagStatuses.Length; flagIndex++)
        {
            FlagStatuses[flagIndex] = CaptureTheFlagFlagStatus.Home;
            FlagCarriers[flagIndex] = -1;
            FlagPositions[flagIndex] = default;
            FlagTeamIds[flagIndex] = -1;
        }
    }

    internal static void PrepareTeamsForRound()
    {
        if (MyceliumNetwork.IsHost)
        {
            TeamAssignment.AssignCaptureTheFlagRound();
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled)
        {
            return;
        }

        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetRoundState();
        PrepareTeamsForRound();
        BroadcastLiveState();
    }

    internal static void EnsureTeamsAssigned()
    {
        if (!MyceliumNetwork.IsHost || TeamAssignment.TeamCount < 2)
        {
            return;
        }

        if (TeamAssignment.Current.Count == 0)
        {
            PrepareTeamsForRound();
            return;
        }

        foreach (int playerId in PlayerLookup.GetConnectedPlayerIds())
        {
            TeamAssignment.AssignLatePlayer(playerId);
        }
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !MyceliumNetwork.IsHost
            || !GameModeManager.IsActive(GameMode.CaptureTheFlag)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || _roundCompletionRequested)
        {
            return;
        }

        _serverTickAccumulator += Mathf.Max(0f, deltaTime);
        if (_serverTickAccumulator < ServerTickIntervalSeconds)
        {
            return;
        }

        float elapsed = _serverTickAccumulator;
        _serverTickAccumulator = 0f;
        EnsureTeamsAssigned();
        bool stateChanged = EnsureFlagOwnership();
        stateChanged |= ProcessFlagInteractions();
        stateChanged |= UpdateCarriedFlagPositions();
        if (!_roundCompletionRequested && !IsSuddenDeath)
        {
            MatchTimeRemaining = Mathf.Max(0f, MatchTimeRemaining - elapsed);
            if (MatchTimeRemaining <= 0f)
            {
                if (TryGetUniqueLeader(out int winningTeamId))
                {
                    CompleteRound(winningTeamId);
                }
                else
                {
                    IsSuddenDeath = true;
                    stateChanged = true;
                }
            }
            else
            {
                stateChanged = true;
            }
        }

        if (stateChanged)
        {
            BroadcastLiveStateWhenDue();
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        PlayerHealth? deadHealth = PlayerLookup.FindPlayerHealthById(deadPlayerId);
        Vector3 dropPosition = deadHealth != null && deadHealth
            ? deadHealth.transform.position
            : Vector3.zero;
        bool changed = false;
        for (int flagIndex = 0; flagIndex < FlagCarriers.Length; flagIndex++)
        {
            if (FlagStatuses[flagIndex] == CaptureTheFlagFlagStatus.Carried
                && FlagCarriers[flagIndex] == deadPlayerId)
            {
                if (CaptureTheFlagRules.TryDrop(FlagStatuses[flagIndex],
                    out CaptureTheFlagFlagStatus droppedStatus))
                {
                    FlagStatuses[flagIndex] = droppedStatus;
                    FlagCarriers[flagIndex] = -1;
                    FlagPositions[flagIndex] = dropPosition;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            BroadcastLiveState();
        }
    }

    internal static void ApplyLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, string flagsData, float matchTimeRemaining,
        bool suddenDeath, int roundId, int revision, string source = "rpc")
    {
        if (teamCount != 2 || matchTimeRemaining < 0f
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        TeamAssignment.ApplySnapshot(assignmentsData, teamCount);
        Scores.Clear();
        foreach (KeyValuePair<int, int> score in ScoreCodec.Parse(scoresData,
            GameModeManager.EffectivePointsToWin))
        {
            Scores[score.Key] = score.Value;
        }

        InitializeFlagOwnership();
        MatchTimeRemaining = matchTimeRemaining;
        IsSuddenDeath = suddenDeath;
        ParseFlags(flagsData);
    }

    internal static int GetScore(int teamId)
    {
        return Scores.TryGetValue(teamId, out int score) ? score : 0;
    }

    private static void ResetRoundState()
    {
        Scores.Clear();
        Scores[0] = 0;
        Scores[1] = 0;
        MatchTimeRemaining = CaptureTheFlagRules.GetMatchDuration(
            GameModeManager.EffectivePointsToWin);
        IsSuddenDeath = false;
        _serverTickAccumulator = 0f;
        _roundCompletionRequested = false;
        InitializeFlagOwnership();
    }

    private static void InitializeFlagOwnership()
    {
        if (!GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            || definition.CaptureTheFlagObjectives.Count != 2)
        {
            return;
        }

        for (int flagIndex = 0; flagIndex < 2; flagIndex++)
        {
            FlagTeamIds[flagIndex] = CaptureTheFlagRules.FindNearestTeam(
                ToTeamPoint(definition.CaptureTheFlagObjectives[flagIndex]),
                ToTeamPoints(definition.TeamOrigins));
            FlagStatuses[flagIndex] = CaptureTheFlagFlagStatus.Home;
            FlagCarriers[flagIndex] = -1;
            FlagPositions[flagIndex] = definition.CaptureTheFlagObjectives[flagIndex];
        }
    }

    private static bool ProcessFlagInteractions()
    {
        bool changed = false;
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIds())
        {
            if (!TeamAssignment.TryGetTeamId(playerId, out int teamId))
            {
                continue;
            }

            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(playerId);
            if (health == null || !health.gameObject.activeInHierarchy || health.health <= 0f)
            {
                continue;
            }

            Vector3 playerPosition = health.transform.position;
            int carriedFlag = FindCarriedFlag(playerId);
            if (carriedFlag >= 0)
            {
                int ownFlag = FindFlagForTeam(teamId);
                if (ownFlag >= 0 && FlagStatuses[ownFlag] == CaptureTheFlagFlagStatus.Home
                    && IsNear(playerPosition, GetHomePosition(ownFlag)))
                {
                    if (!CaptureTheFlagRules.TryAwardCapture(Scores, teamId,
                        GameModeManager.EffectivePointsToWin, true, true,
                        out int winningTeamId))
                    {
                        continue;
                    }

                    ReturnFlagHome(carriedFlag);
                    ShowCapturePopupForTeam(teamId);
                    changed = true;
                    if (winningTeamId >= 0)
                    {
                        CompleteRound(teamId);
                        return true;
                    }
                }

                continue;
            }

            int ownFlagIndex = FindFlagForTeam(teamId);
            if (ownFlagIndex >= 0
                && FlagStatuses[ownFlagIndex] == CaptureTheFlagFlagStatus.Dropped
                && IsNear(playerPosition, FlagPositions[ownFlagIndex]))
            {
                if (CaptureTheFlagRules.TryReturn(FlagStatuses[ownFlagIndex], true,
                    out CaptureTheFlagFlagStatus returnedStatus))
                {
                    FlagStatuses[ownFlagIndex] = returnedStatus;
                    FlagCarriers[ownFlagIndex] = -1;
                    FlagPositions[ownFlagIndex] = GetHomePosition(ownFlagIndex);
                    changed = true;
                }
            }

            int enemyFlagIndex = FindFlagForOtherTeam(teamId);
            if (enemyFlagIndex >= 0 && FlagStatuses[enemyFlagIndex]
                != CaptureTheFlagFlagStatus.Carried
                && IsNear(playerPosition, GetFlagWorldPosition(enemyFlagIndex)))
            {
                if (CaptureTheFlagRules.TryPickup(FlagStatuses[enemyFlagIndex], true,
                    out CaptureTheFlagFlagStatus carriedStatus))
                {
                    FlagStatuses[enemyFlagIndex] = carriedStatus;
                    FlagCarriers[enemyFlagIndex] = playerId;
                    FlagPositions[enemyFlagIndex] = playerPosition;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static bool EnsureFlagOwnership()
    {
        bool changed = false;
        for (int flagIndex = 0; flagIndex < FlagCarriers.Length; flagIndex++)
        {
            if (FlagStatuses[flagIndex] != CaptureTheFlagFlagStatus.Carried)
            {
                continue;
            }

            int carrierId = FlagCarriers[flagIndex];
            if (!TeamAssignment.TryGetTeamId(carrierId, out _)
                || PlayerLookup.FindActivePlayerHealthById(carrierId) == null)
            {
                ReturnFlagHome(flagIndex);
                changed = true;
            }
        }

        return changed;
    }

    private static bool UpdateCarriedFlagPositions()
    {
        bool changed = false;
        for (int flagIndex = 0; flagIndex < FlagCarriers.Length; flagIndex++)
        {
            if (FlagStatuses[flagIndex] != CaptureTheFlagFlagStatus.Carried)
            {
                continue;
            }

            PlayerHealth? carrier = PlayerLookup.FindActivePlayerHealthById(FlagCarriers[flagIndex]);
            if (carrier == null || !carrier)
            {
                continue;
            }

            Vector3 position = carrier.transform.position;
            if ((FlagPositions[flagIndex] - position).sqrMagnitude > 0.0001f)
            {
                FlagPositions[flagIndex] = position;
                changed = true;
            }
        }

        return changed;
    }

    private static void ShowCapturePopupForTeam(int teamId)
    {
        foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current)
        {
            if (assignment.Value == teamId)
            {
                GameModeHud.ShowScorePopupForPlayer(assignment.Key, CapturePoints);
            }
        }
    }

    private static int FindCarriedFlag(int playerId)
    {
        for (int flagIndex = 0; flagIndex < FlagCarriers.Length; flagIndex++)
        {
            if (FlagStatuses[flagIndex] == CaptureTheFlagFlagStatus.Carried
                && FlagCarriers[flagIndex] == playerId)
            {
                return flagIndex;
            }
        }

        return -1;
    }

    private static int FindFlagForTeam(int teamId)
    {
        for (int flagIndex = 0; flagIndex < FlagTeamIds.Length; flagIndex++)
        {
            if (FlagTeamIds[flagIndex] == teamId)
            {
                return flagIndex;
            }
        }

        return -1;
    }

    private static int FindFlagForOtherTeam(int teamId)
    {
        for (int flagIndex = 0; flagIndex < FlagTeamIds.Length; flagIndex++)
        {
            if (FlagTeamIds[flagIndex] != teamId)
            {
                return flagIndex;
            }
        }

        return -1;
    }

    private static Vector3 GetHomePosition(int flagIndex)
    {
        return GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            && definition.CaptureTheFlagObjectives.Count == 2
            ? definition.CaptureTheFlagObjectives[flagIndex]
            : FlagPositions[flagIndex];
    }

    private static Vector3 GetFlagWorldPosition(int flagIndex)
    {
        return FlagStatuses[flagIndex] == CaptureTheFlagFlagStatus.Dropped
            ? FlagPositions[flagIndex]
            : GetHomePosition(flagIndex);
    }

    private static void ReturnFlagHome(int flagIndex)
    {
        FlagStatuses[flagIndex] = CaptureTheFlagFlagStatus.Home;
        FlagCarriers[flagIndex] = -1;
        FlagPositions[flagIndex] = GetHomePosition(flagIndex);
    }

    private static bool IsNear(Vector3 left, Vector3 right)
    {
        Vector3 delta = left - right;
        return delta.x * delta.x + delta.y * delta.y + delta.z * delta.z
            <= FlagTouchRadius * FlagTouchRadius;
    }

    private static bool TryGetUniqueLeader(out int winningTeamId)
    {
        return CaptureTheFlagRules.TryResolveTimeoutWinner(Scores, out winningTeamId);
    }

    private static void CompleteRound(int winningTeamId)
    {
        if (_roundCompletionRequested || winningTeamId < 0)
        {
            return;
        }

        _roundCompletionRequested = true;
        GameModeManager.CompleteCustomRound(winningTeamId);
        BroadcastLiveState();
    }

    private static void BroadcastLiveStateWhenDue()
    {
        if (Sync.IsLivePushDue())
        {
            BroadcastLiveState();
        }
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
        string flagsData = SerializeFlags();
        string payload = string.Join("|", MyceliumNetwork.LobbyHost.m_SteamID,
            GameModeManager.RoundId, revision, assignmentsData, TeamAssignment.TeamCount,
            scoresData, flagsData, MatchTimeRemaining.ToString(CultureInfo.InvariantCulture),
            IsSuddenDeath ? "1" : "0");
        ModeLobbyDataSync.PublishRaw(LiveLobbyDataKey, payload);
        MyceliumNetwork.RPC(Plugin.CaptureTheFlagModId,
            nameof(Plugin.SyncCaptureTheFlagLiveState), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, assignmentsData, TeamAssignment.TeamCount,
            scoresData, flagsData, MatchTimeRemaining, IsSuddenDeath,
            GameModeManager.RoundId, revision);
    }

    private static void SendLiveStateTo(CSteamID player)
    {
        MyceliumNetwork.RPCTarget(Plugin.CaptureTheFlagModId,
            nameof(Plugin.SyncCaptureTheFlagLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, TeamRules.SerializeAssignments(TeamAssignment.Current),
            TeamAssignment.TeamCount, ScoreCodec.Serialize(Scores), SerializeFlags(),
            MatchTimeRemaining, IsSuddenDeath, GameModeManager.RoundId, Sync.LiveRevision);
    }

    private static string SerializeFlags()
    {
        return string.Join(";", SerializeFlag(0), SerializeFlag(1));
    }

    private static string SerializeFlag(int flagIndex)
    {
        Vector3 position = FlagPositions[flagIndex];
        return string.Join(",", (int)FlagStatuses[flagIndex], FlagCarriers[flagIndex],
            FlagTeamIds[flagIndex],
            position.x.ToString(CultureInfo.InvariantCulture),
            position.y.ToString(CultureInfo.InvariantCulture),
            position.z.ToString(CultureInfo.InvariantCulture));
    }

    private static void ParseFlags(string data)
    {
        string[] flags = (data ?? string.Empty).Split(';');
        for (int flagIndex = 0; flagIndex < 2 && flagIndex < flags.Length; flagIndex++)
        {
            string[] fields = flags[flagIndex].Split(',');
            if (fields.Length != 6 || !int.TryParse(fields[0], out int status)
                || !int.TryParse(fields[1], out int carrier)
                || !int.TryParse(fields[2], out int teamId)
                || !float.TryParse(fields[3], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float x)
                || !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float y)
                || !float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float z)
                || status < 0 || status > 2 || teamId < 0 || teamId > 1)
            {
                continue;
            }

            FlagStatuses[flagIndex] = (CaptureTheFlagFlagStatus)status;
            FlagCarriers[flagIndex] = carrier;
            FlagTeamIds[flagIndex] = teamId;
            FlagPositions[flagIndex] = new Vector3(x, y, z);
        }
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("ctf", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 7, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[1], out int teamCount)
            || !float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture,
                out float matchTimeRemaining)
            || !LobbySnapshotCodec.TryParseBool(fields[6], out bool suddenDeath))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], teamCount, fields[2], fields[3],
            matchTimeRemaining, suddenDeath, roundId, revision,
            ModeLobbyDataSync.Source("ctf", "live"));
    }

    private static string? TryGetPlayerName(int playerId)
    {
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client && client.PlayerId == playerId)
            {
                return client.PlayerName;
            }
        }

        return null;
    }

    private static int ResolvePlayerId(CSteamID steamId)
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

    private static List<TeamPoint> ToTeamPoints(IReadOnlyList<Vector3> positions)
    {
        List<TeamPoint> points = new(positions.Count);
        foreach (Vector3 position in positions)
        {
            points.Add(ToTeamPoint(position));
        }

        return points;
    }

    private static TeamPoint ToTeamPoint(Vector3 position)
    {
        return new TeamPoint(position.x, position.y, position.z);
    }
}
