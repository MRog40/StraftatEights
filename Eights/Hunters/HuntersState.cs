using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FishNet.Object;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class HuntersState
{
    internal const float TakeDurationSeconds = 90f;
    internal const float TieBreakDurationSeconds = HuntersRules.TieBreakDurationSeconds;
    internal const float ServerTickIntervalSeconds = 0.1f;
    internal const int PointsPerTakeWin = HuntersRules.PointsPerTakeWin;

    internal static readonly HashSet<int> AlivePlayers = new();
    internal static readonly Dictionary<int, int> Scores = new();
    internal static bool Enabled;
    internal static int TakeId { get; private set; }
    internal static int WinnerId { get; private set; } = -1;
    internal static int TakeWinnerId { get; private set; } = -1;
    internal static float TakeTimeRemaining { get; private set; }
    internal static bool IsTieBreakActive { get; private set; }
    internal static int TieBreakController { get; private set; } = -1;
    internal static float TieBreakHoldRemaining { get; private set; }
    internal static int TeamCount => TeamAssignment.TeamCount;
    internal static IReadOnlyDictionary<int, int> Assignments => TeamAssignment.Current;
    internal static GameMode Mode => Variant.Mode;
    internal static bool IsActive => GameModeManager.IsActive(Variant.Mode);
    internal static bool IsTankBattle => IsActive && Variant.ForceCrouch;
    internal static float HealthOverride => IsActive ? Variant.HealthOverride : 0f;
    internal static bool DisableHealthRegen => IsActive && Variant.DisableHealthRegen;
    internal static string TeamZeroName => Variant.TeamZeroName;
    internal static string TeamOneName => Variant.TeamOneName;

    private sealed class LoadoutRequest
    {
        internal int PlayerObjectId { get; set; }
        internal string WeaponName { get; set; } = string.Empty;
        internal bool RightRequested { get; set; }
        internal bool LeftRequested { get; set; }
        internal bool Complete { get; set; }
    }

    private static HuntersVariantDefinition Variant => GameModeManager.ActiveMode switch
    {
        GameMode.RabbitHunters => RabbitHuntersDefinition.Value,
        GameMode.TankBattle => TankBattleDefinition.Value,
        _ => NinjaHuntersDefinition.Value
    };
    private static readonly ModeSyncState Sync = new(livePushInterval: 0.5f);
    private static readonly Dictionary<int, LoadoutRequest> LoadoutRequests = new();
    private static int _lastTeamAssignmentRevision = -1;
    private static float _serverTickAccumulator;
    private static float _nextLoadoutCheckTime;
    private static float _tieBreakElapsed;
    private static bool _roundStarted;
    private static bool _takeEnding;

    private static bool ConfiguredEnabled => Variant.Mode switch
    {
        GameMode.RabbitHunters => Plugin.RabbitHuntersEnabled.Value,
        GameMode.TankBattle => Plugin.TankBattleEnabled.Value,
        _ => Plugin.NinjaHuntersEnabled.Value
    };

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    internal static void PushSettingsIfHost()
    {
        PushSettingsIfHost(Variant, ConfiguredEnabled);
    }

    internal static void PushSettingsIfHost(HuntersVariantDefinition variant, bool enabled)
    {
        if (GameModeManager.ActiveMode != variant.Mode)
        {
            return;
        }

        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettings(enabled);
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(variant.SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision,
            enabled ? "1" : "0", "1");
        MyceliumNetwork.RPC(variant.ModId, variant.SettingsRpcName, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            enabled, true);
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

    internal static void PollLiveStateIfClient()
    {
        if (!MyceliumNetwork.IsHost && MyceliumNetwork.InLobby
            && GameModeManager.IsActive(Variant.Mode))
        {
            ApplyLobbyLiveSnapshot();
        }
    }

    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        if (MyceliumNetwork.IsHost)
        {
            ApplySettings(ConfiguredEnabled);
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

        if (ModeLobbyDataSync.ContainsKey(keys, Variant.SettingsLobbyDataKey))
        {
            ApplyLobbySettingsSnapshot();
        }
        if (ModeLobbyDataSync.ContainsKey(keys, Variant.LiveLobbyDataKey))
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
            if (playerId >= 0 && TeamAssignment.AssignLatePlayer(playerId))
            {
                BroadcastLiveState();
            }
        }

        MyceliumNetwork.RPCTarget(Variant.ModId, Variant.SettingsRpcName, player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId,
            Sync.SettingsRevision, ConfiguredEnabled, true);
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

        AlivePlayers.Remove(playerId);
        LoadoutRequests.Remove(playerId);
        TeamAssignment.RemovePlayer(playerId);
        if (TryResolveTeamWipe(out int winningTeamId))
        {
            CompleteTake(winningTeamId);
        }
        else
        {
            BroadcastLiveState();
        }
    }

    internal static bool TryAcceptSettingsSnapshot(HuntersVariantDefinition variant,
        CSteamID hostId, int roundId, int revision)
    {
        return GameModeManager.ActiveMode == variant.Mode
            && Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        TeamAssignment.Reset();
        HuntersMarker.ResetState();
        AlivePlayers.Clear();
        Scores.Clear();
        LoadoutRequests.Clear();
        _lastTeamAssignmentRevision = -1;
        _serverTickAccumulator = 0f;
        _nextLoadoutCheckTime = 0f;
        _tieBreakElapsed = 0f;
        _roundStarted = false;
        _takeEnding = false;
        TakeId = 0;
        WinnerId = -1;
        TakeWinnerId = -1;
        TakeTimeRemaining = 0f;
        IsTieBreakActive = false;
        TieBreakController = -1;
        TieBreakHoldRemaining = 0f;
    }

    internal static void PrepareTeamsForRound()
    {
        if (MyceliumNetwork.IsHost && TeamAssignment.Current.Count == 0)
        {
            TeamAssignment.AssignHuntersRound();
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(Variant.Mode) || !MyceliumNetwork.IsHost
            || _roundStarted)
        {
            return;
        }

        Scores[0] = 0;
        Scores[1] = 0;
        PrepareTeamsForRound();
        _roundStarted = true;
        StartTake();
    }

    internal static bool EnsureTeamsAssigned()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return false;
        }

        int playerRevision = PlayerLookup.ConnectedPlayerRevision;
        if (_lastTeamAssignmentRevision == playerRevision
            && TeamAssignment.TeamCount == 2 && TeamAssignment.Current.Count > 0)
        {
            return false;
        }

        if (TeamAssignment.TeamCount != 2 || TeamAssignment.Current.Count == 0)
        {
            bool assigned = TeamAssignment.AssignHuntersRound();
            if (assigned)
            {
                _lastTeamAssignmentRevision = playerRevision;
            }
            return assigned;
        }

        bool changed = false;
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIdsReadOnly())
        {
            changed |= TeamAssignment.AssignLatePlayer(playerId);
        }

        _lastTeamAssignmentRevision = playerRevision;
        return changed;
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !MyceliumNetwork.IsHost || !GameModeManager.IsActive(Variant.Mode)
            || !GameModeManager.IsRoundGameplayActive || !_roundStarted || _takeEnding
            || WinnerId >= 0)
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
        bool changed = EnsureTeamsAssigned();
        TakeTimeRemaining = Mathf.Max(0f, TakeTimeRemaining - elapsed);
        changed = true;
        if (TakeTimeRemaining <= 0f)
        {
            if (TryGetControllingTeam(out int controllingTeam))
            {
                HuntersRules.AdvanceTieBreakHold(elapsed, TieBreakController,
                    controllingTeam, ref _tieBreakElapsed);
                IsTieBreakActive = true;
                TieBreakController = controllingTeam;
                TieBreakHoldRemaining = Mathf.Max(0f,
                    TieBreakDurationSeconds - _tieBreakElapsed);
                changed = true;
                if (_tieBreakElapsed >= TieBreakDurationSeconds)
                {
                    CompleteTake(controllingTeam);
                    return;
                }
            }
            else
            {
                if (IsTieBreakActive || _tieBreakElapsed > 0f)
                {
                    changed = true;
                }
                _tieBreakElapsed = 0f;
                IsTieBreakActive = false;
                TieBreakController = -1;
                TieBreakHoldRemaining = TieBreakDurationSeconds;
            }
        }

        if (TryResolveTeamWipe(out int winningTeamId))
        {
            CompleteTake(winningTeamId);
            return;
        }

        if (changed)
        {
            BroadcastLiveStateWhenDue();
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !MyceliumNetwork.IsHost || !_roundStarted || _takeEnding)
        {
            return;
        }

        if (AlivePlayers.Remove(deadPlayerId) && TryResolveTeamWipe(out int winningTeamId))
        {
            CompleteTake(winningTeamId);
        }
        else
        {
            BroadcastLiveState();
        }
    }

    internal static bool CanRespawn(PlayerManager manager)
    {
        if (!GameModeManager.IsActive(Variant.Mode) || !_roundStarted)
        {
            return true;
        }

        int playerId = FindPlayerId(manager);
        return _takeEnding || AlivePlayers.Contains(playerId);
    }

    internal static bool TryGetRoleSpawnPosition(PlayerManager manager, out Vector3 position)
    {
        position = default;
        if (!GameModeManager.IsActive(Variant.Mode)
            || !GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            || definition.TeamOrigins.Count < 2)
        {
            return false;
        }

        EnsureTeamsAssigned();
        int playerId = FindPlayerId(manager);
        if (!TeamAssignment.TryGetTeamId(playerId, out int teamId))
        {
            return false;
        }

        int originIndex = HuntersRules.GetOriginIndex(teamId, TakeId);
        List<int> rolePlayers = TeamAssignment.Current
            .Where(entry => entry.Value == teamId)
            .Select(entry => entry.Key)
            .OrderBy(id => id)
            .ToList();
        int playerIndex = rolePlayers.IndexOf(playerId);
        if (playerIndex < 0)
        {
            return false;
        }

        Vector3[] offsets =
        {
            Vector3.zero,
            Vector3.forward,
            Vector3.right,
            Vector3.back,
            Vector3.left
        };
        position = definition.TeamOrigins[originIndex]
            + offsets[playerIndex % offsets.Length];
        return true;
    }

    internal static bool TryGetTieBreakObjective(out HardpointObjective objective)
    {
        objective = null!;
        return GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            && definition.HardpointObjectives.Count > 0
            && (objective = definition.HardpointObjectives[0]) != null;
    }

    internal static string GetLocalObjectiveStatusText()
    {
        if (!Enabled || !GameModeManager.IsActive(Variant.Mode)
            || !IsTieBreakActive || ClientInstance.Instance == null)
        {
            return string.Empty;
        }

        int localPlayerId = ClientInstance.Instance.PlayerId;
        return TeamAssignment.TryGetTeamId(localPlayerId, out int teamId)
            ? (teamId == TieBreakController ? "YOUR TEAM CONTROLS\n" : "ENEMY CONTROLS\n")
                + Mathf.CeilToInt(TieBreakHoldRemaining) + "s"
            : string.Empty;
    }

    internal static int GetScore(int teamId)
    {
        return Scores.TryGetValue(teamId, out int score) ? score : 0;
    }

    internal static void ApplyHealth(PlayerHealth controller, HealthSettingsTuning.Memory memory)
    {
        float desiredHealth = HealthOverride;
        float previousHealth = controller.sync___get_value_health();
        int playerId = controller.playerValues?.playerClient?.PlayerId ?? -1;
        controller.fullHealth = desiredHealth;
        memory.LastModeSpecificHealth = true;
        memory.LastAppliedVersion = HealthSettingsState.TuningVersion;
        memory.LastAppliedHealthCompensationVersion = TeamAssignment.HealthCompensationVersion;
        memory.LastAppliedPlayerId = playerId;

        if (!controller.IsServer)
        {
            return;
        }

        float healthDelta = desiredHealth - previousHealth;
        if (Mathf.Approximately(healthDelta, 0f))
        {
            return;
        }

        HealthSettingsTuning.ApplyingPassiveHealth = true;
        try
        {
            FishNetCompatibility.TryRemoveHealth(controller, -healthDelta);
        }
        finally
        {
            HealthSettingsTuning.ApplyingPassiveHealth = false;
        }
    }

    internal static string GetExpectedWeaponName(int playerId)
    {
        return TeamAssignment.TryGetTeamId(playerId, out int teamId) && teamId == 1
            ? Variant.TeamOneWeaponName
            : Variant.TeamZeroWeaponName;
    }

    internal static bool IsExpectedWeapon(Weapon weapon, int playerId)
    {
        return weapon != null && weapon.name.StartsWith(GetExpectedWeaponName(playerId),
            StringComparison.Ordinal);
    }

    internal static bool IsExpectedWeapon(Weapon weapon)
    {
        if (weapon == null)
        {
            return false;
        }

        int playerId = weapon.playerController?.GetComponent<PlayerHealth>()
            ?.playerValues?.playerClient?.PlayerId ?? -1;
        return playerId >= 0 && IsExpectedWeapon(weapon!, playerId);
    }

    internal static bool IsWeaponAllowed(Weapon weapon, int playerId)
    {
        return IsExpectedWeapon(weapon, playerId);
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !GameModeManager.IsActive(Variant.Mode)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WeaponService.IsFinalGameScreen || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 0.5f;
        foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current)
        {
            if (!ClientInstance.playerInstances.TryGetValue(assignment.Key,
                out ClientInstance client) || client == null || !client
                || client.PlayerSpawner?.player == null || !client.PlayerSpawner.player)
            {
                continue;
            }

            EnsurePlayerLoadout(assignment.Key, client.PlayerSpawner.player);
        }
    }

    internal static void OnPlayerSpawned(PlayerManager manager)
    {
        if (!GameModeManager.IsActive(Variant.Mode) || !MyceliumNetwork.IsHost
            || manager == null || !manager
            || manager.player == null || !manager.player)
        {
            return;
        }

        int playerId = FindPlayerId(manager);
        if (playerId >= 0)
        {
            EnsurePlayerLoadout(playerId, manager.player);
        }
    }

    private static void EnsurePlayerLoadout(int playerId, FirstPersonController player)
    {
        PlayerPickup? pickup = player.playerPickupScript;
        if (pickup == null || !pickup)
        {
            return;
        }

        GameObject? heldObject = pickup.objInHand;
        Weapon? heldWeapon = heldObject == null || !heldObject
            ? null
            : heldObject.GetComponent<Weapon>();
        GameObject? heldLeftObject = pickup.objInLeftHand;
        Weapon? heldLeftWeapon = heldLeftObject == null || !heldLeftObject
            ? null
            : heldLeftObject.GetComponent<Weapon>();
        string expectedWeapon = GetExpectedWeaponName(playerId);
        bool dualWield = IsDualWieldPlayer(playerId);
        int playerObjectId = player.GetInstanceID();
        if (!LoadoutRequests.TryGetValue(playerId, out LoadoutRequest? request)
            || request.PlayerObjectId != playerObjectId
            || !string.Equals(request.WeaponName, expectedWeapon, StringComparison.Ordinal))
        {
            request = new LoadoutRequest
            {
                PlayerObjectId = playerObjectId,
                WeaponName = expectedWeapon
            };
            LoadoutRequests[playerId] = request;
        }

        bool rightReady = heldWeapon != null && IsExpectedWeapon(heldWeapon, playerId);
        bool leftReady = !dualWield
            || (heldLeftWeapon != null && IsExpectedWeapon(heldLeftWeapon, playerId));
        if (rightReady && leftReady)
        {
            if (!request.Complete)
            {
                WeaponAmmoTuning.InitializeUnlimited(heldWeapon!);
                if (dualWield)
                {
                    WeaponAmmoTuning.InitializeUnlimited(heldLeftWeapon!);
                }
            }
            request.Complete = true;
            return;
        }

        if (request.Complete || !pickup.IsServer)
        {
            return;
        }

        if (!rightReady && !request.RightRequested)
        {
            request.RightRequested = true;
            WeaponService.GiveWeapon(playerId, expectedWeapon, unlimitedAmmo: true);
        }

        if (dualWield && rightReady && !leftReady && !request.LeftRequested)
        {
            request.LeftRequested = true;
            WeaponService.GiveWeaponToLeftHand(playerId, expectedWeapon);
        }
    }

    private static bool IsDualWieldPlayer(int playerId)
    {
        return TeamAssignment.TryGetTeamId(playerId, out int teamId)
            && teamId == 0 && Variant.TeamZeroDualWield;
    }

    private static void StartTake()
    {
        _takeEnding = false;
        TakeId++;
        TakeWinnerId = -1;
        TakeTimeRemaining = TakeDurationSeconds;
        _tieBreakElapsed = 0f;
        IsTieBreakActive = false;
        TieBreakController = -1;
        TieBreakHoldRemaining = TieBreakDurationSeconds;
        AlivePlayers.Clear();
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIdsReadOnly())
        {
            if (TeamAssignment.Current.ContainsKey(playerId))
            {
                AlivePlayers.Add(playerId);
            }
        }
        BroadcastLiveState();
    }

    private static bool TryResolveTeamWipe(out int winningTeamId)
    {
        return HuntersRules.TryGetTeamWipeWinner(AlivePlayers, TeamAssignment.Current,
            out winningTeamId);
    }

    private static bool TryGetControllingTeam(out int teamId)
    {
        teamId = -1;
        if (!TryGetTieBreakObjective(out HardpointObjective objective))
        {
            return false;
        }

        HashSet<int> teams = new();
        float radiusSquared = objective.Radius * objective.Radius;
        foreach (int playerId in AlivePlayers)
        {
            if (!TeamAssignment.TryGetTeamId(playerId, out int playerTeam)
                || teams.Contains(playerTeam))
            {
                continue;
            }

            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(playerId);
            if (health != null && health && health.health > 0f
                && (health.transform.position - objective.Position).sqrMagnitude <= radiusSquared)
            {
                teams.Add(playerTeam);
            }
        }

        if (teams.Count != 1)
        {
            return false;
        }

        foreach (int controllingTeam in teams)
        {
            teamId = controllingTeam;
            return true;
        }

        return false;
    }

    private static void CompleteTake(int winningTeamId)
    {
        if (_takeEnding || WinnerId >= 0 || winningTeamId < 0)
        {
            return;
        }

        _takeEnding = true;
        TakeWinnerId = winningTeamId;
        Scores.TryGetValue(winningTeamId, out int score);
        Scores[winningTeamId] = HuntersRules.AddTakePoints(score,
            GameModeManager.EffectivePointsToWin);
        GameModeHud.BroadcastTakeResult(
            $"<b>{GetTeamName(winningTeamId)} won the take</b>\n<i>+{PointsPerTakeWin} points</i>",
            3f);
        if (MyceliumNetwork.IsHost)
        {
            foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current)
            {
                if (assignment.Value == winningTeamId)
                {
                    GameModeHud.ShowScorePopupForPlayer(assignment.Key, PointsPerTakeWin);
                }
            }
        }
        BroadcastLiveState();
        if (Scores[winningTeamId] >= GameModeManager.EffectivePointsToWin)
        {
            WinnerId = winningTeamId;
            BroadcastLiveState();
            GameModeManager.CompleteCustomRound(winningTeamId);
            return;
        }

        if (Plugin.Instance != null)
        {
            Plugin.Instance.StartCoroutine(StartNextTakeAfterDelay(
                3f, SessionState.Generation, GameModeManager.RoundId));
        }
    }

    private static IEnumerator StartNextTakeAfterDelay(float delay, int sessionGeneration,
        int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || !GameModeManager.IsActive(Variant.Mode)
            || GameModeManager.Phase != GameModePhase.ActiveRound || WinnerId >= 0)
        {
            yield break;
        }

        StartTake();
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIdsReadOnly())
        {
            GameModeRespawn.Schedule(playerId, 0f, protectOnRespawn: false);
        }
    }

    private static string GetTeamName(int teamId)
    {
        return teamId == 1 ? Variant.TeamOneName : Variant.TeamZeroName;
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
        ModeLobbyDataSync.Publish(Variant.LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, assignmentsData, TeamCount.ToString(), scoresData,
            TakeId.ToString(CultureInfo.InvariantCulture), TakeWinnerId.ToString(CultureInfo.InvariantCulture),
            WinnerId.ToString(CultureInfo.InvariantCulture),
            TakeTimeRemaining.ToString(CultureInfo.InvariantCulture), IsTieBreakActive ? "1" : "0",
            TieBreakController.ToString(CultureInfo.InvariantCulture),
            TieBreakHoldRemaining.ToString(CultureInfo.InvariantCulture));
        MyceliumNetwork.RPC(Variant.ModId, Variant.LiveRpcName, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, assignmentsData, TeamCount, scoresData, TakeId,
            TakeWinnerId, WinnerId, TakeTimeRemaining, IsTieBreakActive, TieBreakController,
            TieBreakHoldRemaining, GameModeManager.RoundId, revision);
    }

    private static void SendLiveStateTo(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Variant.ModId, Variant.LiveRpcName, player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost,
            TeamRules.SerializeAssignments(TeamAssignment.Current), TeamCount,
            ScoreCodec.Serialize(Scores), TakeId, TakeWinnerId, WinnerId, TakeTimeRemaining,
            IsTieBreakActive, TieBreakController, TieBreakHoldRemaining,
            GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static void ApplyLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int takeId, int takeWinnerId, int winnerId,
        float takeTimeRemaining, bool tieBreakActive, int tieBreakController,
        float tieBreakHoldProgress, int roundId, int revision)
    {
        ApplyLiveState(Variant, hostId, assignmentsData, teamCount, scoresData, takeId,
            takeWinnerId, winnerId, takeTimeRemaining, tieBreakActive, tieBreakController,
            tieBreakHoldProgress, roundId, revision);
    }

    internal static void ApplyLiveState(HuntersVariantDefinition variant, CSteamID hostId,
        string assignmentsData, int teamCount, string scoresData, int takeId, int takeWinnerId,
        int winnerId, float takeTimeRemaining, bool tieBreakActive, int tieBreakController,
        float tieBreakHoldProgress, int roundId, int revision)
    {
        if (GameModeManager.ActiveMode != variant.Mode
            || teamCount != 2 || takeId < 0 || takeWinnerId < -1 || takeWinnerId > 1
            || winnerId < -1 || winnerId > 1
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision))
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

        TakeId = takeId;
        TakeWinnerId = takeWinnerId;
        WinnerId = winnerId;
        TakeTimeRemaining = Mathf.Max(0f, takeTimeRemaining);
        IsTieBreakActive = tieBreakActive;
        TieBreakController = tieBreakController;
        TieBreakHoldRemaining = Mathf.Clamp(tieBreakHoldProgress, 0f,
            TieBreakDurationSeconds);
        _roundStarted = takeId > 0;
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(Variant.SettingsLobbyDataKey, 2,
            out CSteamID hostId, out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !LobbySnapshotCodec.TryParseBool(fields[1], out _)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("hunters", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(Variant.LiveLobbyDataKey, 10,
            out CSteamID hostId, out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[1], out int teamCount)
            || !int.TryParse(fields[3], out int takeId)
            || !int.TryParse(fields[4], out int takeWinnerId)
            || !int.TryParse(fields[5], out int winnerId)
            || !float.TryParse(fields[6], NumberStyles.Float, CultureInfo.InvariantCulture,
                out float takeTimeRemaining)
            || !LobbySnapshotCodec.TryParseBool(fields[7], out bool tieBreakActive)
            || !int.TryParse(fields[8], out int tieBreakController)
            || !float.TryParse(fields[9], NumberStyles.Float, CultureInfo.InvariantCulture,
                out float tieBreakHoldProgress))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], teamCount, fields[2], takeId, takeWinnerId,
            winnerId, takeTimeRemaining, tieBreakActive, tieBreakController,
            tieBreakHoldProgress, roundId, revision);
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

    private static int FindPlayerId(PlayerManager manager)
    {
        foreach (KeyValuePair<int, ClientInstance> entry in ClientInstance.playerInstances)
        {
            if (entry.Value != null && entry.Value
                && entry.Value.PlayerSpawner == manager)
            {
                return entry.Key;
            }
        }

        return -1;
    }
}
