using System;
using System.Collections.Generic;
using System.Globalization;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HardpointState
{
    internal const string SettingsLobbyDataKey = "StraftatEights_Hardpoint_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_Hardpoint_Live";
    internal const float ObjectiveDurationSeconds = 30f;
    internal const float WarningDurationSeconds = 5f;

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

    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static readonly Dictionary<int, PlayerHealth> ConfiguredLoadouts = new();
    private static float _scoreAccumulator;
    private static bool _roundInitialized;
    private static bool _roundCompletionRequested;
    private static float _nextClientLivePollTime;

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
            GameModeManager.RoundId, revision, Plugin.HardpointEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.HardpointModId, nameof(Plugin.SyncHardpointSettings),
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId,
            revision, Plugin.HardpointEnabled.Value);
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
            Sync.SettingsRevision, Plugin.HardpointEnabled.Value);
        SendLiveStateTo(player);
    }

    internal static void PollLiveStateIfClient()
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !GameModeManager.IsActive(GameMode.Hardpoint)
            || Time.unscaledTime < _nextClientLivePollTime)
        {
            return;
        }

        _nextClientLivePollTime = Time.unscaledTime + 1f;
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
        HardpointOutline.ResetState();
        HardpointMarker.ResetState();
        Scores.Clear();
        CurrentObjectiveIndex = 0;
        ObjectiveElapsedSeconds = 0f;
        ContestTimeRemaining = 0f;
        CurrentController = -1;
        IsSuddenDeath = false;
        _scoreAccumulator = 0f;
        _roundInitialized = false;
        _roundCompletionRequested = false;
        ConfiguredLoadouts.Clear();
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled)
        {
            return;
        }

        _roundInitialized = true;
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetRoundState();
        if (!TeamAssignment.AssignForRound())
        {
            return;
        }

        BroadcastLiveState();
    }

    internal static bool EnsureTeamsAssigned()
    {
        if (!MyceliumNetwork.IsHost || TeamAssignment.TeamCount < 2)
        {
            return false;
        }

        if (TeamAssignment.Current.Count == 0)
        {
            return TeamAssignment.AssignForRound();
        }

        bool changed = false;
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIds())
        {
            changed |= TeamAssignment.AssignLatePlayer(playerId);
        }

        return changed;
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !MyceliumNetwork.IsHost || !GameModeManager.IsActive(GameMode.Hardpoint))
        {
            return;
        }

        foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current)
        {
            PlayerHealth? health = PlayerLookup.FindPlayerHealthById(assignment.Key);
            if (health == null || !health.gameObject.activeInHierarchy || health.health <= 0f)
            {
                ConfiguredLoadouts.Remove(assignment.Key);
                continue;
            }

            if (ConfiguredLoadouts.TryGetValue(assignment.Key, out PlayerHealth? configured)
                && configured == health)
            {
                continue;
            }

            ConfiguredLoadouts[assignment.Key] = health;
            WeaponService.GiveWeapon(assignment.Key, "Dispenser", WeaponSettingsState.SpareMagazines);
        }
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !MyceliumNetwork.IsHost || !GameModeManager.IsActive(GameMode.Hardpoint)
            || GameModeManager.Phase != GameModePhase.ActiveRound || _roundCompletionRequested)
        {
            return;
        }

        if (EnsureTeamsAssigned())
        {
            BroadcastLiveState();
        }
        if (!_roundInitialized || TeamAssignment.Current.Count == 0
            || !GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            || definition.HardpointObjectives.Count == 0)
        {
            return;
        }

        HardpointObjective objective = definition.HardpointObjectives[CurrentObjectiveIndex
            % definition.HardpointObjectives.Count];
        HashSet<int> teamsOnPoint = GetTeamsOnPoint(objective);
        int controller = TeamRules.ResolveController(teamsOnPoint);
        bool stateChanged = controller != CurrentController;
        CurrentController = controller;

        float elapsed = Mathf.Max(0f, deltaTime);
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

                bool roundWon = HardpointRules.TryAwardPoint(Scores, controller,
                    GameModeManager.EffectivePointsToWin, IsSuddenDeath, out int winningTeamId);
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

        if (stateChanged || IsWarningActive)
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
        _roundCompletionRequested = false;
        ConfiguredLoadouts.Clear();
    }

    private static HashSet<int> GetTeamsOnPoint(HardpointObjective objective)
    {
        HashSet<int> teams = new();
        float radiusSquared = objective.Radius * objective.Radius;
        foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current)
        {
            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(assignment.Key);
            if (health == null || !health.gameObject.activeInHierarchy || health.health <= 0f)
            {
                continue;
            }

            Vector3 delta = health.transform.position - objective.Position;
            if (delta.y >= -1f && delta.y <= 1f
                && delta.x * delta.x + delta.z * delta.z <= radiusSquared)
            {
                teams.Add(assignment.Value);
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
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
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