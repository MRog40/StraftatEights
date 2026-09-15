using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyState
{
    private const KeyCode InteractionKey = KeyCode.P;
    internal const string SettingsLobbyDataKey = "StraftatEights_SnD_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_SnD_Live";
    internal const float PlantDurationSeconds = SearchAndDestroyRules.PlantDurationSeconds;
    internal const float DefuseDurationSeconds = SearchAndDestroyRules.DefuseDurationSeconds;
    internal const float FuseDurationSeconds = SearchAndDestroyRules.FuseDurationSeconds;
    internal const float InteractionRadius = SearchAndDestroyRules.InteractionRadius;
    internal const float ServerTickIntervalSeconds = 0.1f;
    internal const int PointsPerRoundWin = SearchAndDestroyRules.PointsPerRoundWin;

    internal static bool Enabled;
    internal static int SubRoundId { get; private set; }
    internal static int WinnerId { get; private set; } = -1;
    internal static int SubRoundWinnerId { get; private set; } = -1;
    internal static int OffensiveTeamId { get; private set; } = 0;
    internal static int DefensiveTeamId => SearchAndDestroyRules.GetOtherTeamId(OffensiveTeamId);
    internal static int BombCarrierPlayerId { get; private set; } = -1;
    internal static int PlantingPlayerId { get; private set; } = -1;
    internal static int DefuserPlayerId { get; private set; } = -1;
    internal static int BombSiteIndex { get; private set; } = -1;
    internal static SearchAndDestroyBombStatus BombStatus { get; private set; }
    internal static float PlantProgress { get; private set; }
    internal static float DefuseProgress { get; private set; }
    internal static float SubRoundTimeRemaining { get; private set; }
    internal static float FuseTimeRemaining { get; private set; }
    internal static Vector3 BombPosition { get; private set; }
    internal static int TeamCount => TeamAssignment.TeamCount;
    internal static IReadOnlyDictionary<int, int> Assignments => TeamAssignment.Current;
    internal static readonly HashSet<int> AlivePlayers = new();
    internal static readonly Dictionary<int, int> Scores = new();

    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static readonly Dictionary<int, bool> HeldInteractions = new();
    private static readonly Dictionary<int, bool> LookingAtBomb = new();
    private static readonly NetworkCommandTracker InteractionCommands = new();
    private static float _serverTickAccumulator;
    private static int _nextLocalCommandId;
    private static bool _localInteractionHeld;
    private static bool _localLookingAtBomb;
    private static bool _roundStarted;
    private static bool _subRoundEnding;

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
            GameModeManager.RoundId, revision, Plugin.SearchAndDestroyEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.SearchAndDestroyModId,
            nameof(Plugin.SyncSearchAndDestroySettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.SearchAndDestroyEnabled.Value);
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

        MyceliumNetwork.RPCTarget(Plugin.SearchAndDestroyModId,
            nameof(Plugin.SyncSearchAndDestroySettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.SearchAndDestroyEnabled.Value);
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
        _localInteractionHeld = false;
        _localLookingAtBomb = false;
        _roundStarted = false;
        _subRoundEnding = false;
        SubRoundId = 0;
        WinnerId = -1;
        SubRoundWinnerId = -1;
        OffensiveTeamId = 0;
        BombCarrierPlayerId = -1;
        PlantingPlayerId = -1;
        DefuserPlayerId = -1;
        BombSiteIndex = -1;
        BombStatus = SearchAndDestroyBombStatus.Home;
        PlantProgress = 0f;
        DefuseProgress = 0f;
        SubRoundTimeRemaining = 0f;
        FuseTimeRemaining = 0f;
        BombPosition = default;
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
        StartSubRound();
    }

    internal static void EnsureTeamsAssigned()
    {
        if (!MyceliumNetwork.IsHost || TeamAssignment.TeamCount >= 2)
        {
            return;
        }

        PrepareTeamsForRound();
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !MyceliumNetwork.IsHost
            || !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WinnerId >= 0 || !_roundStarted || SubRoundId <= 0 || _subRoundEnding)
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
        SubRoundTimeRemaining = Mathf.Max(0f, SubRoundTimeRemaining - elapsed);
        if (SubRoundTimeRemaining <= 0f)
        {
            CompleteSubRound(DefensiveTeamId);
            return;
        }

        bool stateChanged = ProcessBombInteractions(elapsed);
        if (BombStatus == SearchAndDestroyBombStatus.Planted)
        {
            FuseTimeRemaining = Mathf.Max(0f, FuseTimeRemaining - elapsed);
            stateChanged = true;
            if (FuseTimeRemaining <= 0f)
            {
                CompleteSubRound(OffensiveTeamId);
                return;
            }
        }

        if (stateChanged)
        {
            BroadcastLiveStateWhenDue();
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!MyceliumNetwork.IsHost || !_roundStarted || _subRoundEnding
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

        if (TryResolveTeamWipe(out int winningTeamId))
        {
            CompleteSubRound(winningTeamId);
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
        }

        if (TryResolveTeamWipe(out int winningTeamId))
        {
            CompleteSubRound(winningTeamId);
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
        if (interactionHeld != _localInteractionHeld
            || lookingAtBomb != _localLookingAtBomb)
        {
            SendInteractionRequest(playerId, interactionHeld, lookingAtBomb);
            _localInteractionHeld = interactionHeld;
            _localLookingAtBomb = lookingAtBomb;
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

        if (BombStatus == SearchAndDestroyBombStatus.Carried
            && BombCarrierPlayerId == playerId
            && FindNearbySite(playerId) >= 0)
        {
            return "Press P to plant";
        }

        if (BombStatus == SearchAndDestroyBombStatus.Dropped
            && IsOffensePlayer(playerId)
            && IsNearPlayer(playerId, BombPosition))
        {
            return "Press P to pick up bomb";
        }

        if (BombStatus == SearchAndDestroyBombStatus.Planted
            && IsDefensePlayer(playerId)
            && IsNearPlayer(playerId, BombPosition))
        {
            return "Press P to defuse";
        }

        return string.Empty;
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

        int playerId = FindPlayerId(manager);
        if (!TeamAssignment.TryGetTeamId(playerId, out int teamId))
        {
            return false;
        }

        int activeOffensiveTeamId = _subRoundEnding
            ? SearchAndDestroyRules.GetOffensiveTeamId(SubRoundId + 1)
            : OffensiveTeamId;
        int roleOriginIndex = teamId == activeOffensiveTeamId ? 0 : 1;
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
        return _subRoundEnding || AlivePlayers.Contains(playerId);
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

    private static void StartSubRound()
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

        SubRoundId++;
        OffensiveTeamId = SearchAndDestroyRules.GetOffensiveTeamId(SubRoundId);
        SubRoundWinnerId = -1;
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
        SubRoundTimeRemaining = SearchAndDestroyRules.GetSubRoundTimeLimit(
            GameModeManager.EffectivePointsToWin);
        FuseTimeRemaining = 0f;
        BombPosition = GetPlayerPosition(BombCarrierPlayerId);
        _subRoundEnding = false;
        BroadcastLiveState();
    }

    private static bool ProcessBombInteractions(float elapsed)
    {
        bool changed = false;
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
                CancelPlanting();
            }
            else
            {
                int siteIndex = FindNearbySite(BombCarrierPlayerId);
                if (siteIndex < 0)
                {
                    CancelPlanting();
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
                    }

                    PlantProgress += elapsed;
                    changed = true;
                    if (SearchAndDestroyRules.TryCompletePlant(BombStatus, PlantProgress,
                        out SearchAndDestroyBombStatus plantedStatus))
                    {
                        BombStatus = plantedStatus;
                        BombPosition = GetSitePositionOrDefault(BombSiteIndex);
                        BombSiteIndex = Math.Max(0, BombSiteIndex);
                        BombCarrierPlayerId = -1;
                        PlantingPlayerId = -1;
                        PlantProgress = 0f;
                        FuseTimeRemaining = FuseDurationSeconds;
                        changed = true;
                    }
                }
            }
        }
        else if (PlantingPlayerId >= 0)
        {
            CancelPlanting();
            changed = true;
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
                    CompleteSubRound(DefensiveTeamId);
                    return true;
                }
            }
        }

        return changed;
    }

    private static bool TryResolveTeamWipe(out int winningTeamId)
    {
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

    private static void CompleteSubRound(int winningTeamId)
    {
        if (_subRoundEnding || WinnerId >= 0 || winningTeamId < 0)
        {
            return;
        }

        _subRoundEnding = true;
        SubRoundWinnerId = winningTeamId;
        Scores.TryGetValue(winningTeamId, out int score);
        Scores[winningTeamId] = score + PointsPerRoundWin;
        BroadcastLiveState();
        if (SearchAndDestroyRules.IsMatchWon(Scores[winningTeamId],
            GameModeManager.EffectivePointsToWin))
        {
            WinnerId = winningTeamId;
            BroadcastLiveState();
            GameModeManager.CompleteCustomRound(winningTeamId);
            return;
        }

        foreach (int playerId in PlayerLookup.GetConnectedPlayerIds())
        {
            GameModeRespawn.Schedule(playerId, 0f);
        }

        if (Plugin.Instance != null)
        {
            Plugin.Instance.StartCoroutine(StartNextSubRoundAfterDelay(
                SearchAndDestroyRules.BetweenSubRoundDelaySeconds,
                SessionState.Generation, GameModeManager.RoundId));
        }
    }

    private static IEnumerator StartNextSubRoundAfterDelay(float delay, int sessionGeneration,
        int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.Phase != GameModePhase.ActiveRound || WinnerId >= 0)
        {
            yield break;
        }

        StartSubRound();
    }

    private static bool IsInteractionHeld(int playerId)
    {
        return HeldInteractions.TryGetValue(playerId, out bool held) && held;
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
        return (closestPoint - BombPosition).sqrMagnitude <= 0.04f;
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
            && IsNearPlayer(playerId, BombPosition);
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

            float distance = (playerPosition - sitePosition).sqrMagnitude;
            if (distance <= InteractionRadius * InteractionRadius && distance < selectedDistance)
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

    private static Vector3 GetSitePositionOrDefault(int siteIndex)
    {
        return TryGetSitePosition(siteIndex, out Vector3 position) ? position : BombPosition;
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
                CultureInfo.InvariantCulture), scoresData, stateData, SubRoundId.ToString(
                CultureInfo.InvariantCulture), WinnerId.ToString(CultureInfo.InvariantCulture));
        MyceliumNetwork.RPC(Plugin.SearchAndDestroyModId,
            nameof(Plugin.SyncSearchAndDestroyLiveState), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, assignmentsData, TeamAssignment.TeamCount, scoresData,
            stateData, SubRoundId, WinnerId, GameModeManager.RoundId, revision);
    }

    private static void SendLiveStateTo(CSteamID player)
    {
        MyceliumNetwork.RPCTarget(Plugin.SearchAndDestroyModId,
            nameof(Plugin.SyncSearchAndDestroyLiveState), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, TeamRules.SerializeAssignments(TeamAssignment.Current),
            TeamAssignment.TeamCount, ScoreCodec.Serialize(Scores), SerializeState(), SubRoundId,
            WinnerId, GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static void ApplyLiveState(CSteamID hostId, string assignmentsData, int teamCount,
        string scoresData, string stateData, int subRoundId, int winnerId, int roundId, int revision,
        string source = "rpc")
    {
        if (teamCount != 2 || subRoundId < 0 || winnerId < -1 || winnerId > 1
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

        SubRoundId = subRoundId;
        WinnerId = winnerId;
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
            PlantingPlayerId, SubRoundWinnerId,
            SubRoundTimeRemaining.ToString(CultureInfo.InvariantCulture), aliveData);
    }

    private static bool TryParseState(string data)
    {
        string[] fields = (data ?? string.Empty).Split(';');
        if (fields.Length != 15 || !int.TryParse(fields[0], out int offenseTeam)
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
            || !int.TryParse(fields[12], out int subRoundWinner)
            || !TryParseFloat(fields[13], out float subRoundTimeRemaining))
        {
            return false;
        }

        if (offenseTeam < 0 || offenseTeam > 1 || bombStatus < 0 || bombStatus > 3
            || siteIndex < -1 || siteIndex > 1 || fuse < 0f || fuse > FuseDurationSeconds
            || plant < 0f || plant > PlantDurationSeconds || defuse < 0f
            || defuse > DefuseDurationSeconds || subRoundWinner < -1 || subRoundWinner > 1
            || subRoundTimeRemaining < 0f
            || subRoundTimeRemaining > SearchAndDestroyRules.GetSubRoundTimeLimit(
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
        SubRoundTimeRemaining = subRoundTimeRemaining;
        DefuserPlayerId = defuserId;
        PlantingPlayerId = plantingId;
        SubRoundWinnerId = subRoundWinner;
        AlivePlayers.Clear();
        foreach (string value in fields[14].Split(','))
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
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
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
            || !int.TryParse(fields[4], out int subRoundId)
            || !int.TryParse(fields[5], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], teamCount, fields[2], fields[3], subRoundId,
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
