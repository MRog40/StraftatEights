using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal enum SearchAndDestroyWinReason
{
    None,
    TimeExpired,
    BombExploded,
    BombDefused,
    AttackersEliminated,
    DefendersEliminated
}

internal static class SearchAndDestroyState
{
    private const KeyCode InteractionKey = KeyCode.P;
    internal const string SettingsLobbyDataKey = "StraftatEights_SnD_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_SnD_Live";
    internal const float PlantDurationSeconds = SearchAndDestroyRules.PlantDurationSeconds;
    internal const float DefuseDurationSeconds = SearchAndDestroyRules.DefuseDurationSeconds;
    internal const float FuseDurationSeconds = SearchAndDestroyRules.FuseDurationSeconds;
    internal const float BombExplosionDelaySeconds = 2f;
    internal const float BombExplosionRadius = 17.5f;
    internal const float InteractionRadius = SearchAndDestroyRules.InteractionRadius;
    internal const float PlantSiteRadius = SearchAndDestroyRules.PlantSiteRadius;
    internal const float ServerTickIntervalSeconds = 0.05f;
    private const float BombAimRadius = 0.45f;
    private const float InteractionRequestResendSeconds = 0.25f;
    internal const int PointsPerRoundWin = SearchAndDestroyRules.PointsPerRoundWin;

    internal static bool Enabled;
    internal static int TakeId { get; private set; }
    internal static int WinnerId { get; private set; } = -1;
    internal static int TakeWinnerId { get; private set; } = -1;
    internal static SearchAndDestroyWinReason TakeWinReason { get; private set; }
    internal static int OffensiveTeamId { get; private set; } = 0;
    internal static int DefensiveTeamId => SearchAndDestroyRules.GetOtherTeamId(OffensiveTeamId);
    internal static int BombCarrierPlayerId { get; private set; } = -1;
    internal static int PlantingPlayerId { get; private set; } = -1;
    internal static int DefuserPlayerId { get; private set; } = -1;
    internal static int BombSiteIndex { get; private set; } = -1;
    internal static SearchAndDestroyBombStatus BombStatus { get; private set; }
    internal static float PlantProgress { get; private set; }
    internal static float DefuseProgress { get; private set; }
    internal static float TakeTimeRemaining { get; private set; }
    internal static float FuseTimeRemaining { get; private set; }
    internal static Vector3 BombPosition { get; private set; }
    internal static int TeamCount => TeamAssignment.TeamCount;
    internal static IReadOnlyDictionary<int, int> Assignments => TeamAssignment.Current;
    internal static readonly HashSet<int> AlivePlayers = new();
    internal static readonly Dictionary<int, int> Scores = new();

    private static readonly ModeSyncState Sync = new(livePushInterval: 0.1f);
    private static readonly Dictionary<int, bool> HeldInteractions = new();
    private static readonly Dictionary<int, bool> LookingAtBomb = new();
    private static readonly NetworkCommandTracker InteractionCommands = new();
    private static float _serverTickAccumulator;
    private static int _nextLocalCommandId;
    private static float _nextInteractionRequestTime;
    private static bool _localInteractionHeld;
    private static bool _localLookingAtBomb;
    private static bool _roundStarted;
    private static bool _takeEnding;
    private static bool _bombExplosionPending;
    private static int _lastAnnouncedTakeId = -1;
    private static float _lastLiveStateAppliedTime;
    private static float _lastLivePlantProgress;
    private static float _lastLiveDefuseProgress;
    private static int _lastLivePlantingPlayerId = -1;
    private static int _lastLiveDefuserPlayerId = -1;

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
        ApplySettings(Plugin.SearchAndDestroyEnabled.Value);
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
            GameModeManager.RoundId, revision, Plugin.SearchAndDestroyEnabled.Value ? "1" : "0",
            "1");
        MyceliumNetwork.RPC(Plugin.SearchAndDestroyModId,
            nameof(Plugin.SyncSearchAndDestroySettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.SearchAndDestroyEnabled.Value, true);
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
        _nextLocalCommandId = 0;
        _nextInteractionRequestTime = 0f;
        InteractionCommands.Clear();
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
        InteractionCommands.Clear();
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
        if (GameModeManager.Phase == GameModePhase.ActiveRound
            && EnsureTeamsAssigned())
        {
            BroadcastLiveState();
        }

        MyceliumNetwork.RPCTarget(Plugin.SearchAndDestroyModId,
            nameof(Plugin.SyncSearchAndDestroySettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.SearchAndDestroyEnabled.Value, true);
        SendLiveStateTo(player);
    }

    internal static void PollLiveStateIfClient()
    {
        if (!MyceliumNetwork.IsHost && MyceliumNetwork.InLobby
            && GameModeManager.IsActive(GameMode.SearchAndDestroy))
        {
            ApplyLobbyLiveSnapshot();
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
        AlivePlayers.Clear();
        Scores.Clear();
        HeldInteractions.Clear();
        LookingAtBomb.Clear();
        _serverTickAccumulator = 0f;
        _nextInteractionRequestTime = 0f;
        _localInteractionHeld = false;
        _localLookingAtBomb = false;
        _roundStarted = false;
        _takeEnding = false;
        _bombExplosionPending = false;
        TakeId = 0;
        WinnerId = -1;
        TakeWinnerId = -1;
        TakeWinReason = SearchAndDestroyWinReason.None;
        _lastAnnouncedTakeId = -1;
        OffensiveTeamId = 0;
        BombCarrierPlayerId = -1;
        PlantingPlayerId = -1;
        DefuserPlayerId = -1;
        BombSiteIndex = -1;
        BombStatus = SearchAndDestroyBombStatus.Home;
        PlantProgress = 0f;
        DefuseProgress = 0f;
        TakeTimeRemaining = 0f;
        FuseTimeRemaining = 0f;
        BombPosition = default;
        _lastLiveStateAppliedTime = 0f;
        _lastLivePlantProgress = 0f;
        _lastLiveDefuseProgress = 0f;
        _lastLivePlantingPlayerId = -1;
        _lastLiveDefuserPlayerId = -1;
    }

    internal static void PrepareTeamsForRound()
    {
        if (MyceliumNetwork.IsHost && TeamAssignment.Current.Count == 0)
        {
            TeamAssignment.AssignSearchAndDestroyRound();
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || !MyceliumNetwork.IsHost)
        {
            return;
        }

        if (_roundStarted)
        {
            return;
        }

        _roundStarted = true;
        Scores.Clear();
        Scores[0] = 0;
        Scores[1] = 0;
        PrepareTeamsForRound();
        StartTake();
    }

    internal static bool EnsureTeamsAssigned()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return false;
        }

        if (TeamAssignment.TeamCount != 2 || TeamAssignment.Current.Count == 0)
        {
            return TeamAssignment.AssignSearchAndDestroyRound();
        }

        bool changed = false;
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIds())
        {
            changed |= TeamAssignment.AssignLatePlayer(playerId);
        }

        return changed;
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !MyceliumNetwork.IsHost
            || !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WinnerId >= 0 || !_roundStarted || TakeId <= 0 || _takeEnding)
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
        if (EnsureTeamsAssigned())
        {
            BroadcastLiveState();
        }
        if (BombStatus != SearchAndDestroyBombStatus.Planted)
        {
            TakeTimeRemaining = Mathf.Max(0f, TakeTimeRemaining - elapsed);
            if (TakeTimeRemaining <= 0f)
            {
                CompleteTake(DefensiveTeamId, SearchAndDestroyWinReason.TimeExpired);
                return;
            }
        }

        if (_bombExplosionPending)
        {
            return;
        }

        bool stateChanged = ProcessBombInteractions(elapsed, out bool broadcastImmediately);
        if (BombStatus == SearchAndDestroyBombStatus.Planted)
        {
            FuseTimeRemaining = Mathf.Max(0f, FuseTimeRemaining - elapsed);
            stateChanged = true;
            if (FuseTimeRemaining <= 0f)
            {
                BeginBombExplosion();
                return;
            }
        }

        if (stateChanged)
        {
            if (broadcastImmediately)
            {
                BroadcastLiveState();
            }
            else
            {
                BroadcastLiveStateWhenDue();
            }
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!MyceliumNetwork.IsHost || !_roundStarted || _takeEnding
            || !AlivePlayers.Remove(deadPlayerId))
        {
            return;
        }

        HeldInteractions.Remove(deadPlayerId);
        LookingAtBomb.Remove(deadPlayerId);
        PlayerHealth? deadHealth = PlayerLookup.FindPlayerHealthById(deadPlayerId);
        Vector3 deathPosition = deadHealth != null && deadHealth
            ? deadHealth.transform.position
            : BombPosition;
        if (BombStatus == SearchAndDestroyBombStatus.Carried
            && BombCarrierPlayerId == deadPlayerId)
        {
            BombStatus = SearchAndDestroyBombStatus.Dropped;
            BombCarrierPlayerId = -1;
            BombPosition = deathPosition;
        }

        if (PlantingPlayerId == deadPlayerId)
        {
            CancelPlanting();
        }
        if (DefuserPlayerId == deadPlayerId)
        {
            CancelDefusing();
        }

        if (!_bombExplosionPending && TryResolveTeamWipe(out int winningTeamId))
        {
            CompleteTake(winningTeamId, GetEliminationWinReason(winningTeamId));
        }
        else
        {
            BroadcastLiveState();
        }
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
        HeldInteractions.Remove(playerId);
        LookingAtBomb.Remove(playerId);
        InteractionCommands.Remove(player);
        if (BombStatus == SearchAndDestroyBombStatus.Carried
            && BombCarrierPlayerId == playerId)
        {
            BombStatus = SearchAndDestroyBombStatus.Dropped;
            BombCarrierPlayerId = -1;
            PlayerHealth? leavingHealth = PlayerLookup.FindPlayerHealthById(playerId);
            if (leavingHealth != null && leavingHealth)
            {
                BombPosition = leavingHealth.transform.position;
            }
        }

        TeamAssignment.RemovePlayer(playerId);

        if (!_bombExplosionPending && TryResolveTeamWipe(out int winningTeamId))
        {
            CompleteTake(winningTeamId, GetEliminationWinReason(winningTeamId));
        }
        else
        {
            BroadcastLiveState();
        }
    }

    internal static void HandleInteractionRequest(int playerId, int commandId, bool pressed,
        bool lookingAtBomb)
    {
        if (!MyceliumNetwork.IsHost || !NetworkAuthority.IsLocalPlayer(playerId)
            || commandId < 0)
        {
            return;
        }

        SetInteractionHeld(playerId, commandId, pressed, lookingAtBomb);
    }

    internal static void HandleInteractionRequest(int playerId, int commandId, bool pressed,
        bool lookingAtBomb, RPCInfo info)
    {
        if (!MyceliumNetwork.IsHost || !NetworkAuthority.IsPlayerSender(info, playerId)
            || !InteractionCommands.TryAccept(info.SenderSteamID, commandId))
        {
            return;
        }

        SetInteractionHeld(playerId, commandId, pressed, lookingAtBomb);
    }

    internal static void PollLocalInput()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || ClientInstance.Instance == null || !MyceliumNetwork.InLobby)
        {
            return;
        }

        int playerId = ClientInstance.Instance.PlayerId;
        if (playerId < 0)
        {
            return;
        }

        bool interactionHeld = Input.GetKey(InteractionKey);
        bool lookingAtBomb = interactionHeld && IsLocalPlayerLookingAtBomb();
        bool stateChanged = interactionHeld != _localInteractionHeld
            || lookingAtBomb != _localLookingAtBomb;
        bool resendDue = interactionHeld && !MyceliumNetwork.IsHost
            && Time.unscaledTime >= _nextInteractionRequestTime;
        if (stateChanged || resendDue)
        {
            SendInteractionRequest(playerId, interactionHeld, lookingAtBomb);
            _localInteractionHeld = interactionHeld;
            _localLookingAtBomb = lookingAtBomb;
            _nextInteractionRequestTime = interactionHeld
                ? Time.unscaledTime + InteractionRequestResendSeconds
                : 0f;
        }
    }

    internal static void ApplyLocalMovementLock()
    {
        if (ClientInstance.Instance == null || !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.Phase != GameModePhase.ActiveRound)
        {
            return;
        }

        int playerId = ClientInstance.Instance.PlayerId;
        PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(playerId);
        if (health == null || !health || health.controller == null)
        {
            return;
        }

        bool lockMovement = IsLocalActionCandidate(playerId);
        if (lockMovement)
        {
            health.controller.canMove = false;
        }
        else if (PauseManager.Instance == null || !PauseManager.Instance.startRound)
        {
            health.controller.canMove = true;
        }
    }

    internal static string GetLocalInteractionPrompt()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || ClientInstance.Instance == null)
        {
            return string.Empty;
        }

        int playerId = ClientInstance.Instance.PlayerId;
        if (playerId < 0 || !AlivePlayers.Contains(playerId))
        {
            return string.Empty;
        }

        if (PlantingPlayerId == playerId)
        {
            float progress = GetDisplayedInteractionProgress(PlantProgress,
                _lastLivePlantProgress, PlantingPlayerId, _lastLivePlantingPlayerId,
                PlantDurationSeconds);
            return FormatInteractionCountdown("PLANTING BOMB", progress,
                PlantDurationSeconds);
        }

        if (DefuserPlayerId == playerId)
        {
            float progress = GetDisplayedInteractionProgress(DefuseProgress,
                _lastLiveDefuseProgress, DefuserPlayerId, _lastLiveDefuserPlayerId,
                DefuseDurationSeconds);
            return FormatInteractionCountdown("DEFUSING BOMB", progress,
                DefuseDurationSeconds);
        }

        if (BombStatus == SearchAndDestroyBombStatus.Carried
            && BombCarrierPlayerId == playerId
            && FindNearbySite(playerId) >= 0)
        {
            return "Hold P to plant";
        }

        if (BombStatus == SearchAndDestroyBombStatus.Dropped
            && IsOffensePlayer(playerId)
            && IsNearPlayer(playerId, BombPosition))
        {
            return "Hold P to pick up bomb";
        }

        if (BombStatus == SearchAndDestroyBombStatus.Planted
            && IsDefensePlayer(playerId)
            && DefuserPlayerId < 0
            && IsNearPlayer(playerId, BombPosition)
            && IsLocalPlayerLookingAtBomb())
        {
            return "Hold P to defuse";
        }

        return string.Empty;
    }

    internal static string GetLocalBombStatusText()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || ClientInstance.Instance == null)
        {
            return string.Empty;
        }

        int playerId = ClientInstance.Instance.PlayerId;
        if (playerId < 0 || !AlivePlayers.Contains(playerId))
        {
            return string.Empty;
        }

        if (PlantingPlayerId == playerId)
        {
            return GetRoleLabel(playerId) + "\nPlanting the bomb";
        }

        if (DefuserPlayerId == playerId)
        {
            return GetRoleLabel(playerId) + "\nDefusing the bomb";
        }

        string action = BombStatus == SearchAndDestroyBombStatus.Carried
            && BombCarrierPlayerId == playerId
            ? "Carrying the bomb"
            : string.Empty;
        return GetRoleLabel(playerId) + (action.Length > 0 ? "\n" + action : string.Empty);
    }

    internal static bool IsOffensePlayer(int playerId)
    {
        return TeamAssignment.TryGetTeamId(playerId, out int teamId)
            && teamId == OffensiveTeamId;
    }

    internal static bool IsDefensePlayer(int playerId)
    {
        return TeamAssignment.TryGetTeamId(playerId, out int teamId)
            && teamId == DefensiveTeamId;
    }

    internal static int GetScore(int teamId)
    {
        return Scores.TryGetValue(teamId, out int score) ? score : 0;
    }

    internal static bool TryGetSitePosition(int siteIndex, out Vector3 position)
    {
        position = default;
        if (siteIndex < 0 || siteIndex >= 2
            || !GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            || definition.SndObjectives.Count != 2)
        {
            return false;
        }

        position = definition.SndObjectives[siteIndex];
        return true;
    }

    internal static bool TryGetBombPosition(out Vector3 position)
    {
        position = BombPosition;
        if (BombStatus == SearchAndDestroyBombStatus.Carried)
        {
            PlayerHealth? carrier = PlayerLookup.FindActivePlayerHealthById(BombCarrierPlayerId);
            if (carrier != null && carrier)
            {
                position = carrier.transform.position;
            }
        }

        return BombStatus != SearchAndDestroyBombStatus.Home;
    }

    internal static bool TryGetRoleSpawnPosition(PlayerManager manager, out Vector3 position)
    {
        position = default;
        if (!GameModeManager.IsActive(GameMode.SearchAndDestroy)
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

        int roleOriginIndex = teamId == OffensiveTeamId ? 0 : 1;
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
        position = definition.TeamOrigins[roleOriginIndex]
            + offsets[playerIndex % offsets.Length];
        return true;
    }

    internal static bool CanRespawn(PlayerManager manager)
    {
        if (!GameModeManager.IsActive(GameMode.SearchAndDestroy) || !_roundStarted)
        {
            return true;
        }

        int playerId = FindPlayerId(manager);
        return _takeEnding || AlivePlayers.Contains(playerId);
    }

    private static void SetInteractionHeld(int playerId, int commandId, bool pressed,
        bool lookingAtBomb)
    {
        if (commandId < 0 || !AlivePlayers.Contains(playerId))
        {
            return;
        }

        HeldInteractions[playerId] = pressed;
        LookingAtBomb[playerId] = pressed && lookingAtBomb;
    }

    private static void SendInteractionRequest(int playerId, bool pressed, bool lookingAtBomb)
    {
        int commandId = ++_nextLocalCommandId;
        if (MyceliumNetwork.IsHost)
        {
            SetInteractionHeld(playerId, commandId, pressed, lookingAtBomb);
            return;
        }

        MyceliumNetwork.RPC(Plugin.SearchAndDestroyModId,
            nameof(Plugin.RequestSearchAndDestroyInteraction), ReliableType.Reliable,
            playerId, commandId, pressed, lookingAtBomb);
    }

    private static void StartTake()
    {
        if (!MyceliumNetwork.IsHost || WinnerId >= 0)
        {
            return;
        }

        PrepareTeamsForRound();
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIds())
        {
            if (!TeamAssignment.Current.ContainsKey(playerId))
            {
                TeamAssignment.AssignLatePlayer(playerId);
            }
        }

        List<int> players = PlayerLookup.GetConnectedPlayerIds()
            .Where(playerId => TeamAssignment.Current.ContainsKey(playerId))
            .ToList();
        if (players.Count == 0)
        {
            return;
        }

        TakeId++;
        OffensiveTeamId = SearchAndDestroyRules.GetOffensiveTeamId(TakeId);
        TakeWinnerId = -1;
        TakeWinReason = SearchAndDestroyWinReason.None;
        AlivePlayers.Clear();
        HeldInteractions.Clear();
        LookingAtBomb.Clear();
        foreach (int playerId in players)
        {
            AlivePlayers.Add(playerId);
        }

        List<int> carriers = players.Where(IsOffensePlayer).ToList();
        BombCarrierPlayerId = carriers.Count > 0
            ? carriers[UnityEngine.Random.Range(0, carriers.Count)]
            : -1;
        BombStatus = BombCarrierPlayerId >= 0
            ? SearchAndDestroyBombStatus.Carried
            : SearchAndDestroyBombStatus.Home;
        BombSiteIndex = -1;
        PlantingPlayerId = -1;
        DefuserPlayerId = -1;
        PlantProgress = 0f;
        DefuseProgress = 0f;
        TakeTimeRemaining = SearchAndDestroyRules.GetTakeTimeLimit(
            GameModeManager.EffectivePointsToWin);
        FuseTimeRemaining = 0f;
        BombPosition = GetPlayerPosition(BombCarrierPlayerId);
        _takeEnding = false;
        _bombExplosionPending = false;
        BroadcastLiveState();
    }

    private static bool ProcessBombInteractions(float elapsed, out bool broadcastImmediately)
    {
        bool changed = false;
        broadcastImmediately = false;
        if (BombStatus == SearchAndDestroyBombStatus.Dropped)
        {
            foreach (int playerId in AlivePlayers)
            {
                if (!IsOffensePlayer(playerId) || !IsInteractionHeld(playerId)
                    || !IsNearPlayer(playerId, BombPosition))
                {
                    continue;
                }

                if (SearchAndDestroyRules.TryRecoverBomb(BombStatus, true, true,
                    out SearchAndDestroyBombStatus carriedStatus))
                {
                    BombStatus = carriedStatus;
                    BombCarrierPlayerId = playerId;
                    BombSiteIndex = -1;
                    changed = true;
                    broadcastImmediately = true;
                    break;
                }
            }
        }

        if (BombStatus == SearchAndDestroyBombStatus.Carried)
        {
            BombPosition = GetPlayerPosition(BombCarrierPlayerId);
            if (!IsInteractionHeld(BombCarrierPlayerId)
                || !AlivePlayers.Contains(BombCarrierPlayerId))
            {
                bool wasPlanting = PlantingPlayerId >= 0;
                CancelPlanting();
                if (wasPlanting)
                {
                    changed = true;
                    broadcastImmediately = true;
                }
            }
            else
            {
                int siteIndex = FindNearbySite(BombCarrierPlayerId);
                if (siteIndex < 0)
                {
                    bool wasPlanting = PlantingPlayerId >= 0;
                    CancelPlanting();
                    if (wasPlanting)
                    {
                        changed = true;
                        broadcastImmediately = true;
                    }
                }
                else
                {
                    SearchAndDestroyRules.TryStartPlant(BombStatus, true, true, out _);
                    if (PlantingPlayerId != BombCarrierPlayerId || BombSiteIndex != siteIndex)
                    {
                        PlantingPlayerId = BombCarrierPlayerId;
                        BombSiteIndex = siteIndex;
                        PlantProgress = 0f;
                        changed = true;
                        broadcastImmediately = true;
                    }

                    PlantProgress += elapsed;
                    changed = true;
                    if (SearchAndDestroyRules.TryCompletePlant(BombStatus, PlantProgress,
                        out SearchAndDestroyBombStatus plantedStatus))
                    {
                        if (TryGetPlayerPosition(BombCarrierPlayerId, out Vector3 plantedPosition))
                        {
                            BombPosition = plantedPosition;
                        }
                        BombStatus = plantedStatus;
                        BombSiteIndex = Math.Max(0, BombSiteIndex);
                        BombCarrierPlayerId = -1;
                        PlantingPlayerId = -1;
                        PlantProgress = 0f;
                        FuseTimeRemaining = FuseDurationSeconds;
                        changed = true;
                        broadcastImmediately = true;
                    }
                }
            }
        }
        else if (PlantingPlayerId >= 0)
        {
            CancelPlanting();
            changed = true;
            broadcastImmediately = true;
        }

        if (BombStatus == SearchAndDestroyBombStatus.Planted)
        {
            if (DefuserPlayerId >= 0
                && (!IsInteractionHeld(DefuserPlayerId)
                    || !IsLookingAtBomb(DefuserPlayerId)
                    || !AlivePlayers.Contains(DefuserPlayerId)
                    || !IsDefensePlayer(DefuserPlayerId)
                    || !IsNearPlayer(DefuserPlayerId, BombPosition)))
            {
                CancelDefusing();
                changed = true;
                broadcastImmediately = true;
            }

            if (DefuserPlayerId < 0)
            {
                foreach (int playerId in AlivePlayers)
                {
                    if (!IsDefensePlayer(playerId) || !IsInteractionHeld(playerId)
                        || !IsLookingAtBomb(playerId)
                        || !IsNearPlayer(playerId, BombPosition))
                    {
                        continue;
                    }

                    if (SearchAndDestroyRules.TryStartDefuse(BombStatus, true, true, out _))
                    {
                        DefuserPlayerId = playerId;
                        DefuseProgress = 0f;
                        changed = true;
                        broadcastImmediately = true;
                        break;
                    }
                }
            }

            if (DefuserPlayerId >= 0)
            {
                DefuseProgress += elapsed;
                changed = true;
                if (SearchAndDestroyRules.TryCompleteDefuse(BombStatus, DefuseProgress,
                    out SearchAndDestroyBombStatus defusedStatus))
                {
                    BombStatus = defusedStatus;
                    BombSiteIndex = -1;
                    FuseTimeRemaining = 0f;
                    DefuserPlayerId = -1;
                    DefuseProgress = 0f;
                    CompleteTake(DefensiveTeamId, SearchAndDestroyWinReason.BombDefused);
                    return true;
                }
            }
        }

        return changed;
    }

    private static bool TryResolveTeamWipe(out int winningTeamId)
    {
        if (!SearchAndDestroyRules.CanResolveTeamWipe(BombStatus))
        {
            winningTeamId = -1;
            return false;
        }

        bool teamZeroWiped = SearchAndDestroyRules.IsTeamWiped(AlivePlayers,
            TeamAssignment.Current, 0);
        bool teamOneWiped = SearchAndDestroyRules.IsTeamWiped(AlivePlayers,
            TeamAssignment.Current, 1);
        if (teamZeroWiped == teamOneWiped)
        {
            winningTeamId = -1;
            return false;
        }

        winningTeamId = teamZeroWiped ? 1 : 0;
        return true;
    }

    private static void CompleteTake(int winningTeamId, SearchAndDestroyWinReason reason)
    {
        if (_takeEnding || WinnerId >= 0 || winningTeamId < 0)
        {
            return;
        }

        _takeEnding = true;
        TakeWinnerId = winningTeamId;
        TakeWinReason = reason;
        Scores.TryGetValue(winningTeamId, out int score);
        Scores[winningTeamId] = score + PointsPerRoundWin;
        AnnounceTakeResult();
        BroadcastLiveState();
        if (SearchAndDestroyRules.IsMatchWon(Scores[winningTeamId],
            GameModeManager.EffectivePointsToWin))
        {
            WinnerId = winningTeamId;
            BroadcastLiveState();
            GameModeManager.CompleteCustomRound(winningTeamId);
            return;
        }

        if (Plugin.Instance != null)
        {
            Plugin.Instance.StartCoroutine(StartNextTakeAfterDelay(
                SearchAndDestroyRules.BetweenTakeDelaySeconds,
                SessionState.Generation, GameModeManager.RoundId));
        }
    }

    private static void BeginBombExplosion()
    {
        if (_bombExplosionPending || _takeEnding)
        {
            return;
        }

        _bombExplosionPending = true;
        FuseTimeRemaining = 0f;
        TakeWinReason = SearchAndDestroyWinReason.BombExploded;
        TriggerBombBlastPlayers();
        BroadcastLiveState();

        if (Plugin.Instance != null)
        {
            Plugin.Instance.StartCoroutine(CompleteBombExplosionAfterDelay(
                SessionState.Generation, GameModeManager.RoundId));
        }
        else
        {
            CompleteTake(OffensiveTeamId, SearchAndDestroyWinReason.BombExploded);
        }
    }

    private static IEnumerator CompleteBombExplosionAfterDelay(int sessionGeneration, int roundId)
    {
        yield return new WaitForSeconds(BombExplosionDelaySeconds);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || !GameModeManager.IsActive(GameMode.SearchAndDestroy) || WinnerId >= 0
            || !_bombExplosionPending)
        {
            yield break;
        }

        _bombExplosionPending = false;
        CompleteTake(OffensiveTeamId, SearchAndDestroyWinReason.BombExploded);
    }

    private static void TriggerBombBlastPlayers()
    {
        float radiusSquared = BombExplosionRadius * BombExplosionRadius;
        foreach (int playerId in AlivePlayers.ToArray())
        {
            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(playerId);
            if (health == null || !health || health.health <= 0f
                || (health.transform.position - BombPosition).sqrMagnitude > radiusSquared)
            {
                continue;
            }

            Vector3 ejectDirection = health.transform.position - BombPosition;
            if (ejectDirection.sqrMagnitude < 0.001f)
            {
                ejectDirection = Vector3.up;
            }
            else
            {
                ejectDirection.Normalize();
            }

            health.Explode(true, false, string.Empty, ejectDirection, 35f, BombPosition);
            FishNetCompatibility.TryRemoveHealth(health, health.health + 1f);
        }
    }

    private static SearchAndDestroyWinReason GetEliminationWinReason(int winningTeamId)
    {
        return winningTeamId == OffensiveTeamId
            ? SearchAndDestroyWinReason.DefendersEliminated
            : SearchAndDestroyWinReason.AttackersEliminated;
    }

    private static void AnnounceTakeResult()
    {
        if (TakeWinnerId < 0 || _lastAnnouncedTakeId == TakeId)
        {
            return;
        }

        _lastAnnouncedTakeId = TakeId;
        TeamColorData teamColor = TeamRules.GetColor(TakeWinnerId);
        string teamColorMarkup = $"#{teamColor.Red:X2}{teamColor.Green:X2}{teamColor.Blue:X2}";
        string reason = TakeWinReason switch
        {
            SearchAndDestroyWinReason.TimeExpired => "Time expired",
            SearchAndDestroyWinReason.BombExploded => "Bomb exploded",
            SearchAndDestroyWinReason.BombDefused => "Bomb defused",
            SearchAndDestroyWinReason.AttackersEliminated => "All attackers eliminated",
            SearchAndDestroyWinReason.DefendersEliminated => "All defenders eliminated",
            _ => "Round complete"
        };
        string resultText = TakeWinReason == SearchAndDestroyWinReason.BombExploded
            ? $"<color=#FF5A36><b>Boom! Bomb exploded</b></color>\n"
                + $"<color={teamColorMarkup}><b>Team {TakeWinnerId + 1} won the take</b></color>"
            : $"<color={teamColorMarkup}><b>Team {TakeWinnerId + 1} "
                + $"won the take</b></color>\n<i>{reason}</i>";
        GameModeHud.BroadcastTakeResult(resultText,
            TakeWinReason == SearchAndDestroyWinReason.BombExploded ? 4f : 3f);

        if (MyceliumNetwork.IsHost)
        {
            foreach (KeyValuePair<int, int> assignment in TeamAssignment.Current)
            {
                if (assignment.Value == TakeWinnerId)
                {
                    GameModeHud.ShowScorePopupForPlayer(assignment.Key, PointsPerRoundWin);
                }
            }
        }
        else if (ClientInstance.Instance != null
            && TeamAssignment.TryGetTeamId(ClientInstance.Instance.PlayerId, out int localTeamId)
            && localTeamId == TakeWinnerId)
        {
            GameModeHud.ShowScorePopup(PointsPerRoundWin);
        }
    }

    private static IEnumerator StartNextTakeAfterDelay(float delay, int sessionGeneration,
        int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.Phase != GameModePhase.ActiveRound || WinnerId >= 0)
        {
            yield break;
        }

        StartTake();
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIds())
        {
            GameModeRespawn.Schedule(playerId, 0f);
        }
    }

    private static bool IsInteractionHeld(int playerId)
    {
        return HeldInteractions.TryGetValue(playerId, out bool held) && held;
    }

    private static float GetDisplayedInteractionProgress(float progress,
        float snapshotProgress, int activePlayerId, int snapshotPlayerId, float duration)
    {
        if (MyceliumNetwork.IsHost || activePlayerId != snapshotPlayerId)
        {
            return progress;
        }

        float extrapolatedProgress = snapshotProgress
            + Mathf.Max(0f, Time.unscaledTime - _lastLiveStateAppliedTime);
        return Mathf.Min(duration, extrapolatedProgress);
    }

    private static string FormatInteractionCountdown(string label, float progress,
        float duration)
    {
        float remaining = Mathf.Max(0f, duration - Mathf.Clamp(progress, 0f, duration));
        return label + "\n" + remaining.ToString("F1", CultureInfo.InvariantCulture) + "s";
    }

    private static string GetRoleLabel(int playerId)
    {
        return IsOffensePlayer(playerId)
            ? "<color=#F05A47>OFFENSE</color>"
            : "<color=#5797F2>DEFENSE</color>";
    }

    private static bool IsLookingAtBomb(int playerId)
    {
        return LookingAtBomb.TryGetValue(playerId, out bool looking) && looking;
    }

    private static bool IsLocalPlayerLookingAtBomb()
    {
        if (BombStatus != SearchAndDestroyBombStatus.Planted
            || ClientInstance.Instance == null)
        {
            return false;
        }

        PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(
            ClientInstance.Instance.PlayerId);
        Camera? camera = health?.controller?.playerCamera;
        if (camera == null || !camera.enabled)
        {
            return false;
        }

        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 toBomb = BombPosition - ray.origin;
        float distanceAlongRay = Vector3.Dot(toBomb, ray.direction);
        if (distanceAlongRay <= 0f)
        {
            return false;
        }

        Vector3 closestPoint = ray.origin + ray.direction * distanceAlongRay;
        return (closestPoint - BombPosition).sqrMagnitude <= BombAimRadius * BombAimRadius;
    }

    private static bool IsLocalActionCandidate(int playerId)
    {
        if (!Input.GetKey(InteractionKey)
            || !AlivePlayers.Contains(playerId))
        {
            return false;
        }

        if (BombStatus == SearchAndDestroyBombStatus.Dropped && IsOffensePlayer(playerId))
        {
            return IsNearPlayer(playerId, BombPosition);
        }

        if (BombStatus == SearchAndDestroyBombStatus.Carried
            && BombCarrierPlayerId == playerId)
        {
            return FindNearbySite(playerId) >= 0;
        }

        return BombStatus == SearchAndDestroyBombStatus.Planted
            && IsDefensePlayer(playerId)
            && DefuserPlayerId < 0
            && IsNearPlayer(playerId, BombPosition)
            && IsLocalPlayerLookingAtBomb();
    }

    private static int FindNearbySite(int playerId)
    {
        if (!TryGetPlayerPosition(playerId, out Vector3 playerPosition))
        {
            return -1;
        }

        int selectedSite = -1;
        float selectedDistance = float.MaxValue;
        for (int siteIndex = 0; siteIndex < 2; siteIndex++)
        {
            if (!TryGetSitePosition(siteIndex, out Vector3 sitePosition))
            {
                continue;
            }

            Vector3 horizontalOffset = playerPosition - sitePosition;
            horizontalOffset.y = 0f;
            float distance = horizontalOffset.sqrMagnitude;
            if (distance <= PlantSiteRadius * PlantSiteRadius && distance < selectedDistance)
            {
                selectedSite = siteIndex;
                selectedDistance = distance;
            }
        }

        return selectedSite;
    }

    private static bool IsNearPlayer(int playerId, Vector3 position)
    {
        return TryGetPlayerPosition(playerId, out Vector3 playerPosition)
            && (playerPosition - position).sqrMagnitude <= InteractionRadius * InteractionRadius;
    }

    private static Vector3 GetPlayerPosition(int playerId)
    {
        return TryGetPlayerPosition(playerId, out Vector3 position) ? position : Vector3.zero;
    }

    private static bool TryGetPlayerPosition(int playerId, out Vector3 position)
    {
        PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(playerId);
        if (health == null || !health || !health.gameObject.activeInHierarchy || health.health <= 0f)
        {
            position = default;
            return false;
        }

        position = health.transform.position;
        return true;
    }

    private static void CancelPlanting()
    {
        PlantingPlayerId = -1;
        BombSiteIndex = -1;
        PlantProgress = 0f;
    }

    private static void CancelDefusing()
    {
        DefuserPlayerId = -1;
        DefuseProgress = 0f;
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
        string stateData = SerializeState();
        ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, assignmentsData, TeamAssignment.TeamCount.ToString(
                CultureInfo.InvariantCulture), scoresData, stateData, TakeId.ToString(
                CultureInfo.InvariantCulture), WinnerId.ToString(CultureInfo.InvariantCulture));
        MyceliumNetwork.RPC(Plugin.SearchAndDestroyModId,
            nameof(Plugin.SyncSearchAndDestroyLiveState), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, assignmentsData, TeamAssignment.TeamCount, scoresData,
            stateData, TakeId, WinnerId, GameModeManager.RoundId, revision);
    }

    private static void SendLiveStateTo(CSteamID player)
    {
        MyceliumNetwork.RPCTarget(Plugin.SearchAndDestroyModId,
            nameof(Plugin.SyncSearchAndDestroyLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, TeamRules.SerializeAssignments(TeamAssignment.Current),
            TeamAssignment.TeamCount, ScoreCodec.Serialize(Scores), SerializeState(), TakeId,
            WinnerId, GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static void ApplyLiveState(CSteamID hostId, string assignmentsData, int teamCount,
        string scoresData, string stateData, int takeId, int winnerId, int roundId, int revision,
        string source = "rpc")
    {
        if (teamCount != 2 || takeId < 0 || winnerId < -1 || winnerId > 1
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        TeamAssignment.ApplySnapshot(assignmentsData, teamCount);
        Scores.Clear();
        foreach (KeyValuePair<int, int> score in ScoreCodec.Parse(scoresData,
            int.MaxValue))
        {
            Scores[score.Key] = score.Value;
        }

        if (!TryParseState(stateData))
        {
            return;
        }

        TakeId = takeId;
        WinnerId = winnerId;
        AnnounceTakeResult();
    }

    private static string SerializeState()
    {
        string aliveData = string.Join(",", AlivePlayers.OrderBy(playerId => playerId));
        return string.Join(";", OffensiveTeamId, (int)BombStatus, BombCarrierPlayerId,
            BombSiteIndex, BombPosition.x.ToString(CultureInfo.InvariantCulture),
            BombPosition.y.ToString(CultureInfo.InvariantCulture),
            BombPosition.z.ToString(CultureInfo.InvariantCulture),
            FuseTimeRemaining.ToString(CultureInfo.InvariantCulture),
            PlantProgress.ToString(CultureInfo.InvariantCulture),
            DefuseProgress.ToString(CultureInfo.InvariantCulture), DefuserPlayerId,
            PlantingPlayerId, TakeWinnerId, (int)TakeWinReason,
            TakeTimeRemaining.ToString(CultureInfo.InvariantCulture), aliveData);
    }

    private static bool TryParseState(string data)
    {
        string[] fields = (data ?? string.Empty).Split(';');
        if (fields.Length != 16 || !int.TryParse(fields[0], out int offenseTeam)
            || !int.TryParse(fields[1], out int bombStatus)
            || !int.TryParse(fields[2], out int carrierId)
            || !int.TryParse(fields[3], out int siteIndex)
            || !TryParseFloat(fields[4], out float x)
            || !TryParseFloat(fields[5], out float y)
            || !TryParseFloat(fields[6], out float z)
            || !TryParseFloat(fields[7], out float fuse)
            || !TryParseFloat(fields[8], out float plant)
            || !TryParseFloat(fields[9], out float defuse)
            || !int.TryParse(fields[10], out int defuserId)
            || !int.TryParse(fields[11], out int plantingId)
            || !int.TryParse(fields[12], out int takeWinner)
            || !int.TryParse(fields[13], out int winReason)
            || !TryParseFloat(fields[14], out float takeTimeRemaining))
        {
            return false;
        }

        if (offenseTeam < 0 || offenseTeam > 1 || bombStatus < 0 || bombStatus > 3
            || siteIndex < -1 || siteIndex > 1 || fuse < 0f || fuse > FuseDurationSeconds
            || plant < 0f || plant > PlantDurationSeconds || defuse < 0f
            || defuse > DefuseDurationSeconds || takeWinner < -1 || takeWinner > 1
            || winReason < (int)SearchAndDestroyWinReason.None
            || winReason > (int)SearchAndDestroyWinReason.DefendersEliminated
            || takeTimeRemaining < 0f
            || takeTimeRemaining > SearchAndDestroyRules.GetTakeTimeLimit(
                GameModeManager.EffectivePointsToWin))
        {
            return false;
        }

        OffensiveTeamId = offenseTeam;
        BombStatus = (SearchAndDestroyBombStatus)bombStatus;
        BombCarrierPlayerId = carrierId;
        BombSiteIndex = siteIndex;
        BombPosition = new Vector3(x, y, z);
        FuseTimeRemaining = fuse;
        PlantProgress = plant;
        DefuseProgress = defuse;
        TakeTimeRemaining = takeTimeRemaining;
        DefuserPlayerId = defuserId;
        PlantingPlayerId = plantingId;
        TakeWinnerId = takeWinner;
        TakeWinReason = (SearchAndDestroyWinReason)winReason;
        _lastLiveStateAppliedTime = Time.unscaledTime;
        _lastLivePlantProgress = PlantProgress;
        _lastLiveDefuseProgress = DefuseProgress;
        _lastLivePlantingPlayerId = PlantingPlayerId;
        _lastLiveDefuserPlayerId = DefuserPlayerId;
        AlivePlayers.Clear();
        foreach (string value in fields[15].Split(','))
        {
            if (int.TryParse(value, out int playerId) && playerId >= 0)
            {
                AlivePlayers.Add(playerId);
            }
        }

        return true;
    }

    private static bool TryParseFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
            out result) && !float.IsNaN(result) && !float.IsInfinity(result);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 2, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !LobbySnapshotCodec.TryParseBool(fields[1], out _)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("snd", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 6, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[1], out int teamCount)
            || !int.TryParse(fields[4], out int takeId)
            || !int.TryParse(fields[5], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], teamCount, fields[2], fields[3], takeId,
            winnerId, roundId, revision, ModeLobbyDataSync.Source("snd", "live"));
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

    private static int FindPlayerId(PlayerManager manager)
    {
        foreach (KeyValuePair<int, ClientInstance> entry in ClientInstance.playerInstances)
        {
            if (entry.Value != null && entry.Value && entry.Value.PlayerSpawner == manager)
            {
                return entry.Key;
            }
        }

        return -1;
    }
}
