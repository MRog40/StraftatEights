using System;
using System.Collections.Generic;
using System.Globalization;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class HardpointState
{
    internal const string SettingsLobbyDataKey = "Eights_Hardpoint_Settings";
    internal const string LiveLobbyDataKey = "Eights_Hardpoint_Live";
    internal const float ObjectiveDurationSeconds = 50f;
    internal const float WarningDurationSeconds = HardpointRules.NextObjectiveWarningSeconds;
    internal const float ServerTickIntervalSeconds = 0.1f;
    internal const float ClientLivePollIntervalSeconds = 0.25f;

    internal static bool Enabled;
    internal static readonly Dictionary<int, int> Scores = new();
    internal static int CurrentObjectiveIndex { get; private set; }
    internal static float ObjectiveElapsedSeconds { get; private set; }
    internal static float ContestTimeRemaining { get; private set; }
    internal static int CurrentController { get; private set; } = -1;
    internal static bool IsSuddenDeath { get; private set; }
    internal static bool IsWarningActive => HardpointRules.IsWarningActive(ObjectiveElapsedSeconds,
        ObjectiveDurationSeconds, WarningDurationSeconds);
    internal static int TeamCount => TeamAssignment.TeamCount;
    internal static IReadOnlyDictionary<int, int> Assignments => TeamAssignment.Current;

    internal static bool CanRespawn()
    {
        return !IsSuddenDeath && !_roundCompletionRequested;
    }

    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static float _scoreAccumulator;
    private static float _serverTickAccumulator;
    private static bool _roundInitialized;
    private static bool _roundCompletionRequested;
    private static float _nextClientLivePollTime;
    private static bool _loggedAssignmentFailure;

    internal static void ApplySettings(bool enabled)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Hardpoint, enabled))
        {
            return;
        }

        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig()
    {
        ApplySettings(Plugin.HardpointEnabled.Value);
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
            GameModeManager.RoundId, revision, Plugin.HardpointEnabled.Value ? "1" : "0",
            "1");
        MyceliumNetwork.RPC(Plugin.HardpointModId, nameof(Plugin.SyncHardpointSettings),
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId,
            revision, Plugin.HardpointEnabled.Value, true);
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
        _nextClientLivePollTime = 0f;
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

        if (GameModeManager.Phase == GameModePhase.ActiveRound
            && TeamAssignment.AssignLatePlayer(ResolvePlayerId(player)))
        {
            BroadcastLiveState();
        }

        MyceliumNetwork.RPCTarget(Plugin.HardpointModId, nameof(Plugin.SyncHardpointSettings),
            player, ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId,
            Sync.SettingsRevision, Plugin.HardpointEnabled.Value, true);
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

    internal static void PollLiveStateIfClient()
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !GameModeManager.IsActive(GameMode.Hardpoint)
            || Time.unscaledTime < _nextClientLivePollTime)
        {
            return;
        }

        _nextClientLivePollTime = Time.unscaledTime + ClientLivePollIntervalSeconds;
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
        TeammateMarker.ResetState();
        HardpointMarker.ResetState();
        Scores.Clear();
        CurrentObjectiveIndex = 0;
        ObjectiveElapsedSeconds = 0f;
        ContestTimeRemaining = 0f;
        CurrentController = -1;
        IsSuddenDeath = false;
        _scoreAccumulator = 0f;
        _serverTickAccumulator = 0f;
        _roundInitialized = false;
        _roundCompletionRequested = false;
        _loggedAssignmentFailure = false;
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Hardpoint))
        {
            return;
        }

        if (!MyceliumNetwork.IsHost)
        {
            _roundInitialized = true;
            return;
        }

        EnsureTeamsAssigned();
        if (TeamAssignment.TeamCount < 2 || TeamAssignment.Current.Count == 0)
        {
            Plugin.Logger.LogWarning($"[Hardpoint] Round start deferred: teams={TeamAssignment.TeamCount} "
                + $"assignments={TeamAssignment.Current.Count} "
                + $"players={PlayerLookup.GetConnectedPlayerIdsReadOnly().Count}");
            _roundInitialized = false;
            return;
        }

        ResetRoundState();
        _roundInitialized = true;
        BroadcastLiveState();
    }

    internal static bool EnsureTeamsAssigned()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return false;
        }

        if (TeamAssignment.TeamCount < 2 || TeamAssignment.Current.Count == 0)
        {
            return TeamAssignment.AssignForRound();
        }

        bool changed = false;
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIdsReadOnly())
        {
            changed |= TeamAssignment.AssignLatePlayer(playerId);
        }

        return changed;
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !MyceliumNetwork.IsHost || !GameModeManager.IsActive(GameMode.Hardpoint))
        {
            return;
        }

        float frameElapsed = Mathf.Max(0f, deltaTime);
        if (!GameModeManager.IsRoundGameplayActive || _roundCompletionRequested)
        {
            return;
        }

        _serverTickAccumulator += frameElapsed;

        if (_serverTickAccumulator < ServerTickIntervalSeconds)
        {
            return;
        }

        float elapsed = _serverTickAccumulator;
        _serverTickAccumulator = 0f;

        if (EnsureTeamsAssigned())
        {
            BroadcastLiveState();
        }
        else if (!_roundInitialized && !_loggedAssignmentFailure)
        {
            IReadOnlyList<int> playerIds = PlayerLookup.GetConnectedPlayerIdsReadOnly();
            Plugin.Logger.LogWarning($"[Hardpoint] Team assignment failed: host={MyceliumNetwork.IsHost} "
                + $"players=[{string.Join(",", playerIds)}] "
                + $"active={GameModeManager.IsActive(GameMode.Hardpoint)}");
            _loggedAssignmentFailure = true;
        }
        if (!_roundInitialized)
        {
            if (TeamAssignment.TeamCount < 2 || TeamAssignment.Current.Count == 0)
            {
                return;
            }

            ResetRoundState();
            _roundInitialized = true;
            BroadcastLiveState();
        }

        if (TeamAssignment.Current.Count == 0
            || !GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            || definition.HardpointObjectives.Count == 0)
        {
            return;
        }

        HardpointObjective objective = definition.HardpointObjectives[CurrentObjectiveIndex
            % definition.HardpointObjectives.Count];
        HashSet<int> teamsOnPoint = GetTeamsOnPoint(objective,
            out List<int> playersOnPoint);
        int controller = TeamRules.ResolveController(teamsOnPoint);
        bool controllerChanged = controller != CurrentController;
        if (controllerChanged)
        {
            _scoreAccumulator = 0f;
        }
        bool stateChanged = controllerChanged;
        CurrentController = controller;

        ObjectiveElapsedSeconds += elapsed;
        if (controller >= 0)
        {
            _scoreAccumulator += elapsed;
            while (_scoreAccumulator >= 1f && !_roundCompletionRequested)
            {
                _scoreAccumulator -= 1f;
                if (IsSuddenDeath)
                {
                    CompleteRound(controller);
                    break;
                }

                int scoreBeforeAward = GetScore(controller);
                bool roundWon = HardpointRules.TryAwardPoint(Scores, controller,
                    GameModeManager.EffectivePointsToWin, IsSuddenDeath, out int winningTeamId);
                int awardedPoints = GetScore(controller) - scoreBeforeAward;
                if (awardedPoints > 0)
                {
                    foreach (int playerId in playersOnPoint)
                    {
                        if (TeamAssignment.TryGetTeamId(playerId, out int playerTeamId)
                            && playerTeamId == controller)
                        {
                            GameModeHud.ShowScorePopupForPlayer(playerId, awardedPoints);
                        }
                    }
                }
                stateChanged = true;
                if (roundWon)
                {
                    CompleteRound(winningTeamId);
                }
            }
        }
        else
        {
            _scoreAccumulator = 0f;
            ContestTimeRemaining = Mathf.Max(0f, ContestTimeRemaining - elapsed);
            if (ContestTimeRemaining <= 0f && !IsSuddenDeath)
            {
                if (HardpointRules.TryResolveTimerWinner(Scores, out int winnerTeamId))
                {
                    CompleteRound(winnerTeamId);
                }
                else
                {
                    IsSuddenDeath = true;
                    stateChanged = true;
                    AnnounceSuddenDeath();
                }
            }
        }

        if (!_roundCompletionRequested && ObjectiveElapsedSeconds >= ObjectiveDurationSeconds)
        {
            CurrentObjectiveIndex = HardpointRules.GetNextObjectiveIndex(CurrentObjectiveIndex,
                definition.HardpointObjectives.Count);
            ObjectiveElapsedSeconds -= ObjectiveDurationSeconds;
            _scoreAccumulator = 0f;
            stateChanged = true;
        }

        if (controllerChanged)
        {
            BroadcastLiveState();
        }
        else if (stateChanged || IsWarningActive)
        {
            BroadcastLiveStateWhenDue();
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
    }

    internal static int GetScore(int teamId)
    {
        return Scores.TryGetValue(teamId, out int score) ? score : 0;
    }

    internal static bool TryGetCurrentObjective(out HardpointObjective objective)
    {
        if (GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            && definition.HardpointObjectives.Count > 0)
        {
            objective = definition.HardpointObjectives[CurrentObjectiveIndex
                % definition.HardpointObjectives.Count];
            return true;
        }

        objective = null!;
        return false;
    }

    internal static bool TryGetNextObjective(out HardpointObjective objective)
    {
        if (GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            && definition.HardpointObjectives.Count > 1)
        {
            int nextIndex = HardpointRules.GetNextObjectiveIndex(CurrentObjectiveIndex,
                definition.HardpointObjectives.Count);
            objective = definition.HardpointObjectives[nextIndex];
            return true;
        }

        objective = null!;
        return false;
    }

    internal static void ApplyLiveState(CSteamID hostId, string assignmentsData, int teamCount,
        string scoresData, int objectiveIndex, float objectiveElapsed, float contestTimeRemaining,
        int controller, bool suddenDeath, int roundId, int revision, string source = "rpc")
    {
        if (teamCount < 2 || teamCount > 3 || objectiveIndex < 0
            || objectiveElapsed < 0f || objectiveElapsed > ObjectiveDurationSeconds
            || contestTimeRemaining < 0f || controller < -2 || controller >= teamCount
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        bool wasSuddenDeath = IsSuddenDeath;
        TeamAssignment.ApplySnapshot(assignmentsData, teamCount);
        Scores.Clear();
        foreach (KeyValuePair<int, int> score in ScoreCodec.Parse(
            scoresData, GameModeManager.EffectivePointsToWin))
        {
            Scores[score.Key] = score.Value;
        }

        CurrentObjectiveIndex = objectiveIndex;
        ObjectiveElapsedSeconds = objectiveElapsed;
        ContestTimeRemaining = contestTimeRemaining;
        CurrentController = controller;
        IsSuddenDeath = suddenDeath;
        _roundInitialized = true;
        if (!wasSuddenDeath && IsSuddenDeath)
        {
            AnnounceSuddenDeath();
        }
    }

    private static void ResetRoundState()
    {
        Scores.Clear();
        for (int teamId = 0; teamId < TeamAssignment.TeamCount; teamId++)
        {
            Scores[teamId] = 0;
        }

        CurrentObjectiveIndex = 0;
        ObjectiveElapsedSeconds = 0f;
        ContestTimeRemaining = HardpointRules.GetContestTimeLimit(GameModeManager.EffectivePointsToWin);
        CurrentController = -1;
        IsSuddenDeath = false;
        _scoreAccumulator = 0f;
        _serverTickAccumulator = 0f;
        _roundCompletionRequested = false;
    }

    private static HashSet<int> GetTeamsOnPoint(HardpointObjective objective,
        out List<int> playersOnPoint)
    {
        HashSet<int> teams = new();
        playersOnPoint = new List<int>();
        float radiusSquared = objective.Radius * objective.Radius;
        foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current)
        {
            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(assignment.Key);
            if (health == null || !health.gameObject.activeInHierarchy || health.health <= 0f)
            {
                continue;
            }

            Vector3 delta = health.transform.position - objective.Position;
            if (delta.y >= -1f && delta.y <= 2f
                && delta.x * delta.x + delta.z * delta.z <= radiusSquared)
            {
                teams.Add(assignment.Value);
                playersOnPoint.Add(assignment.Key);
            }
        }

        return teams;
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

    private static void AnnounceSuddenDeath()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        GameModeHud.BroadcastAnnouncement("<color=#FFCF4A><b>SUDDEN DEATH</b></color>\n"
            + "<i>NEXT CAP WINS</i>", 4f);
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
        string objectiveElapsed = ObjectiveElapsedSeconds.ToString(CultureInfo.InvariantCulture);
        string contestTimeRemaining = ContestTimeRemaining.ToString(CultureInfo.InvariantCulture);
        string payload = string.Join("|", MyceliumNetwork.LobbyHost.m_SteamID,
            GameModeManager.RoundId, revision, assignmentsData, TeamAssignment.TeamCount,
            scoresData, CurrentObjectiveIndex, objectiveElapsed, contestTimeRemaining,
            CurrentController, IsSuddenDeath ? "1" : "0");
        ModeLobbyDataSync.PublishRaw(LiveLobbyDataKey, payload);
        MyceliumNetwork.RPC(Plugin.HardpointModId, nameof(Plugin.SyncHardpointLiveState),
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, assignmentsData,
            TeamAssignment.TeamCount, scoresData, CurrentObjectiveIndex, ObjectiveElapsedSeconds,
            ContestTimeRemaining, CurrentController, IsSuddenDeath, GameModeManager.RoundId,
            revision);
    }

    private static void SendLiveStateTo(CSteamID player)
    {
        MyceliumNetwork.RPCTarget(Plugin.HardpointModId,
            nameof(Plugin.SyncHardpointLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, TeamRules.SerializeAssignments(TeamAssignment.Current),
            TeamAssignment.TeamCount, ScoreCodec.Serialize(Scores), CurrentObjectiveIndex,
            ObjectiveElapsedSeconds, ContestTimeRemaining, CurrentController, IsSuddenDeath,
            GameModeManager.RoundId, Sync.LiveRevision);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 2, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !LobbySnapshotCodec.TryParseBool(fields[1], out _)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("hardpoint", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 8, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[1], out int teamCount)
            || !int.TryParse(fields[3], out int objectiveIndex)
            || !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture,
                out float objectiveElapsed)
            || !float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture,
                out float contestTimeRemaining)
            || !int.TryParse(fields[6], out int controller)
            || !LobbySnapshotCodec.TryParseBool(fields[7], out bool suddenDeath))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], teamCount, fields[2], objectiveIndex,
            objectiveElapsed, contestTimeRemaining, controller, suddenDeath, roundId, revision,
            ModeLobbyDataSync.Source("hardpoint", "live"));
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
}