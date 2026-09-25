using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class InfideltatState
{
    internal const string SettingsLobbyDataKey = "Eights_Infideltat_Settings";
    internal const string LiveLobbyDataKey = "Eights_Infideltat_Live";
    internal const string WeaponName = "AK-K";
    internal const int SpareMagazines = 2;
    internal const float InfideltatHealth = 200f / 25f;
    internal const float TerroristHealth = 100f / 25f;
    internal const float MovementMultiplier = 0.7f;
    internal const float WeaponDelaySeconds = 10f;
    internal const float InfideltatBeepVolume = 0.5f;
    private const float InfideltatBeepInitialDelaySeconds = 15f;
    private const float InfideltatBeepIntervalSeconds = 15f;
    internal const float DefaultTakeTimeLimitSeconds = ModeTimeoutRules.DefaultRoundSeconds;
    internal const float RoleAnnouncementDuration = WeaponDelaySeconds + 10f;

    internal static bool Enabled;
    internal static int InfideltatPlayerId { get; private set; } = -1;
    internal static int WinnerId { get; private set; } = -1;
    internal static int KillsToWin => GameModeManager.EffectivePointsToWin;
    internal static float TakeTimeRemaining => Mathf.Max(0f, _takeTimeRemaining);
    internal static bool WeaponsUnlocked { get; private set; }
    internal static bool LocalIsInfideltat { get; private set; }
    internal static readonly Dictionary<int, int> Scores = new();

    private static readonly HashSet<int> AlivePlayers = new();
    private static readonly HashSet<int> PendingHealthResets = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static float _nextLoadoutCheckTime;
    private static float _takeTimeRemaining;
    private static readonly ModeSyncState Sync = new();
    private static int _takeId;
    private static int _infidelBeepId;
    private static float _nextInfideltatBeepTime;
    private static int _lastReceivedInfideltatBeepTakeId = -1;
    private static int _lastReceivedInfideltatBeepId = -1;
    private static int _localRoleTakeId = -1;
    private static int _localRoleAnnouncedTakeId = -1;
    private static bool _localRoleAnnouncementPending;
    private static bool _startRetryPending;
    private static bool _takeEnding;

    internal static void ApplySettings(bool enabled)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Infideltat, enabled))
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

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.InfideltatEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.InfideltatEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.InfideltatModId, nameof(Plugin.SyncInfideltatSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.InfideltatEnabled.Value);
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
        if (!Sync.IsLivePushDue())
        {
            return;
        }

        BroadcastLiveState();
        SendRoleStates(true);
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

        MyceliumNetwork.RPCTarget(Plugin.InfideltatModId, nameof(Plugin.SyncInfideltatSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.InfideltatEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.InfideltatModId, nameof(Plugin.SyncInfideltatLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializeScores(), WinnerId,
            _takeId, WeaponsUnlocked, _takeTimeRemaining, GameModeManager.RoundId,
            Sync.LiveRevision);

        if (InfideltatPlayerId >= 0 && TakeIsActive())
        {
            int playerId = player.m_SteamID == 0 ? -1 : FindPlayerId(player);
            if (playerId >= 0)
            {
                AlivePlayers.Add(playerId);
                PendingHealthResets.Add(playerId);
                Scores.TryAdd(playerId, 0);
            }
            SendRoleToPlayer(player, playerId, true);
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
        if (wasAlive && GameModeManager.IsActive(GameMode.Infideltat)
            && WinnerId < 0 && !_takeEnding)
        {
            OnServerKill(playerId, -1);
        }

        bool changed = wasAlive || (playerId >= 0 && Scores.Remove(playerId));
        PendingHealthResets.Remove(playerId);
        PendingLoadouts.Remove(playerId);
        AlivePlayers.Remove(playerId);
        if (playerId >= 0 && InfideltatPlayerId == playerId)
        {
            InfideltatPlayerId = -1;
            changed = true;
        }

        if (changed && GameModeManager.IsActive(GameMode.Infideltat))
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
        StopTakeTransition();
        Sync.ResetLiveState();
        _nextLoadoutCheckTime = 0f;
        _takeTimeRemaining = 0f;
        _takeId = 0;
        _infidelBeepId = 0;
        _nextInfideltatBeepTime = 0f;
        _lastReceivedInfideltatBeepTakeId = -1;
        _lastReceivedInfideltatBeepId = -1;
        _localRoleTakeId = -1;
        _localRoleAnnouncedTakeId = -1;
        _localRoleAnnouncementPending = false;
        _startRetryPending = false;
        _takeEnding = false;
        InfideltatPlayerId = -1;
        WinnerId = -1;
        WeaponsUnlocked = false;
        LocalIsInfideltat = false;
        AlivePlayers.Clear();
        PendingHealthResets.Clear();
        PendingLoadouts.Clear();
        Scores.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string scoresData, int winnerId,
        int takeId, bool weaponsUnlocked, float takeTimeRemaining, int roundId, int revision,
        string source = "rpc")
    {
        int previousRoundId = Sync.LastLiveRoundId;
        if (winnerId < -1 || takeTimeRemaining < 0f
            || float.IsNaN(takeTimeRemaining) || float.IsInfinity(takeTimeRemaining)
            || takeTimeRemaining > DefaultTakeTimeLimitSeconds
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        WinnerId = winnerId;
        bool isCurrentOrNewTake = roundId != previousRoundId || takeId >= _takeId;
        if (isCurrentOrNewTake)
        {
            _takeId = roundId != previousRoundId
                ? takeId
                : Math.Max(_takeId, takeId);
            _takeTimeRemaining = takeTimeRemaining;
        }
        WeaponsUnlocked = weaponsUnlocked;
        Scores.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(scoresData, KillsToWin))
        {
            Scores[entry.Key] = entry.Value;
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infideltat) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        Scores.Clear();
        WinnerId = -1;
        StartTake();
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !MyceliumNetwork.IsHost
            || !GameModeManager.IsActive(GameMode.Infideltat)
            || !GameModeManager.IsRoundGameplayActive
            || WinnerId >= 0 || !TakeIsActive())
        {
            return;
        }

        _takeTimeRemaining = Mathf.Max(0f, _takeTimeRemaining - Mathf.Max(0f, deltaTime));
    TryEmitInfideltatBeep();
        if (_takeTimeRemaining <= 0f)
        {
            CompleteTimeoutWin();
        }
    }

    internal static void ClientTick(float deltaTime)
    {
        TryAnnounceLocalRole();
        if (MyceliumNetwork.IsHost || !Enabled
            || !GameModeManager.IsActive(GameMode.Infideltat)
            || !GameModeManager.IsRoundGameplayActive
            || WinnerId >= 0 || !TakeIsActive())
        {
            return;
        }

        _takeTimeRemaining = Mathf.Max(0f, _takeTimeRemaining - Mathf.Max(0f, deltaTime));
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infideltat)
            || WinnerId >= 0 || _takeEnding)
        {
            return;
        }

        bool deadWasInfideltat = deadPlayerId == InfideltatPlayerId;
        AlivePlayers.Remove(deadPlayerId);

        if (killerId >= 0 && killerId != deadPlayerId)
        {
            bool killerIsInfideltat = killerId == InfideltatPlayerId;
            int killerAward = InfideltatRules.GetKillerAward(deadWasInfideltat, killerIsInfideltat);
            if (killerAward > 0)
            {
                AwardScore(killerId, killerAward);
            }
        }

        bool infidelWon = !deadWasInfideltat && AlivePlayers.Count == 1
            && AlivePlayers.Contains(InfideltatPlayerId);
        int winnerAward = InfideltatRules.GetWinnerAward(infidelWon);
        if (winnerAward > 0)
        {
            AwardScore(InfideltatPlayerId, winnerAward);
        }

        BroadcastLiveState();
        if (deadWasInfideltat)
        {
            GameModeHud.BroadcastTakeResult(
                "<b>The terrorists won the take</b>\n<i>The Infideltat was eliminated</i>");
        }
        else if (infidelWon)
        {
            GameModeHud.BroadcastTakeResult("<b>"
                + PlayerLookup.GetPlayerNameTag(InfideltatPlayerId)
                + " won the take</b>\n<i>All terrorists were eliminated</i>");
        }
        if (WinnerId >= 0)
        {
            Announce(PlayerLookup.GetPlayerNameTag(WinnerId) + " reached " + KillsToWin
                + " points and won the round!");
            GameModeManager.CompleteCustomRound(TeamAssignment.ResolveTeamId(WinnerId));
            return;
        }

        if (deadWasInfideltat || (!deadWasInfideltat && AlivePlayers.Count == 1))
        {
            BeginNextTake();
        }
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infideltat) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        if (!WeaponsUnlocked)
        {
            PendingLoadouts.Remove(playerId);
            ClearPlayerWeapons(playerId);
            return;
        }

        GiveWeapon(playerId);
    }

    internal static void RequestHealthReset(int playerId)
    {
        if (playerId >= 0)
        {
            PendingHealthResets.Add(playerId);
        }
    }

    internal static void ApplyInfideltatBeep(CSteamID hostId, int takeId, int beepId,
        Vector3 position)
    {
        if (hostId != MyceliumNetwork.LobbyHost || !Enabled
            || !GameModeManager.IsActive(GameMode.Infideltat)
            || !GameModeManager.IsRoundGameplayActive || WinnerId >= 0
            || takeId <= 0 || beepId <= 0 || !IsFinitePosition(position))
        {
            return;
        }

        if (takeId < _takeId
            || (takeId == _lastReceivedInfideltatBeepTakeId
                && beepId <= _lastReceivedInfideltatBeepId))
        {
            return;
        }

        if (takeId > _takeId)
        {
            _takeId = takeId;
        }

        _lastReceivedInfideltatBeepTakeId = takeId;
        _lastReceivedInfideltatBeepId = beepId;
        BombBeepAudio.PlayAtPosition(position, InfideltatBeepVolume);
    }

    internal static void ApplyHealth(PlayerHealth health)
    {
        int playerId = health.playerValues?.playerClient?.PlayerId ?? -1;
        float maximumHealth = IsInfideltat(health) ? InfideltatHealth : TerroristHealth;
        health.fullHealth = maximumHealth;
        if (!health.IsServer)
        {
            return;
        }

        float healthDelta = maximumHealth - health.sync___get_value_health();
        bool resetHealth = playerId >= 0 && PendingHealthResets.Remove(playerId);
        if (!resetHealth && healthDelta >= 0f)
        {
            return;
        }

        HealthSettingsTuning.ApplyingPassiveHealth = true;
        try
        {
            FishNetCompatibility.TryRemoveHealth(health, -healthDelta);
        }
        finally
        {
            HealthSettingsTuning.ApplyingPassiveHealth = false;
        }
    }

    internal static bool IsInfideltat(PlayerHealth health)
    {
        int playerId = health.playerValues?.playerClient?.PlayerId ?? -1;
        if (MyceliumNetwork.IsHost)
        {
            return playerId >= 0 && playerId == InfideltatPlayerId;
        }

        return ClientInstance.Instance != null && ClientInstance.Instance.PlayerId == playerId
            && LocalIsInfideltat;
    }

    internal static bool IsAllowedWeapon(Weapon weapon, int playerId)
    {
        return weapon != null && WeaponsUnlocked
            && weapon.name.StartsWith(WeaponName, StringComparison.Ordinal);
    }

    internal static void ApplyLocalRole(CSteamID hostId, int takeId, bool isInfideltat, bool announce)
    {
        if (hostId != MyceliumNetwork.LobbyHost || takeId < _localRoleTakeId)
        {
            return;
        }

        _localRoleTakeId = takeId;
        LocalIsInfideltat = isInfideltat;
        if (announce && _localRoleAnnouncedTakeId != takeId)
        {
            _localRoleAnnouncementPending = true;
            TryAnnounceLocalRole();
        }
    }

    private static void TryAnnounceLocalRole()
    {
        if (!_localRoleAnnouncementPending || _localRoleTakeId < 0
            || !Enabled || !GameModeManager.IsActive(GameMode.Infideltat)
            || !GameModeManager.IsRoundGameplayActive || GameModeManager.IsMatchOver
            || _localRoleAnnouncedTakeId == _localRoleTakeId)
        {
            return;
        }

        _localRoleAnnouncementPending = false;
        _localRoleAnnouncedTakeId = _localRoleTakeId;
        GameModeHud.AnnounceTarget(LocalIsInfideltat
            ? "You are the <color=#CC2222><b>Infideltat</b></color>."
            : "You are a <color=#4D9BFF><b>terrorist</b></color>.", RoleAnnouncementDuration);
    }

    private static bool TakeIsActive()
    {
        return _takeId > 0 && !_takeEnding;
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
            InfideltatPlayerId = -1;
            ScheduleStartTakeRetry();
            return;
        }

        _takeId++;
        _takeEnding = false;
        _takeTimeRemaining = DefaultTakeTimeLimitSeconds;
        WeaponsUnlocked = false;
        _nextLoadoutCheckTime = 0f;
        PendingLoadouts.Clear();
        AlivePlayers.Clear();
        foreach (int playerId in players)
        {
            AlivePlayers.Add(playerId);
            PendingHealthResets.Add(playerId);
            Scores.TryAdd(playerId, 0);
        }

        InfideltatPlayerId = DistributionRandom.SelectPlayer("Infideltat", players);
        ClearCurrentWeapons();
        SendRoleStates(true);
        BroadcastLiveState();

        if (Plugin.Instance != null)
        {
            int token = _takeId;
            Plugin.Instance.StartCoroutine(UnlockWeaponsAfterDelay(token, SessionState.Generation,
                GameModeManager.RoundId));
        }
    }

    private static void ScheduleStartTakeRetry()
    {
        if (_startRetryPending || Plugin.Instance == null)
        {
            return;
        }

        _startRetryPending = true;
        Plugin.Instance.StartCoroutine(RetryStartTake(SessionState.Generation,
            GameModeManager.RoundId, _takeId));
    }

    private static IEnumerator RetryStartTake(int sessionGeneration, int roundId,
        int previousTakeId)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            yield return new WaitForSeconds(0.25f);
            if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
                || WinnerId >= 0 || _takeEnding || !GameModeManager.IsActive(GameMode.Infideltat))
            {
                break;
            }

            StartTake();
            if (_takeId > previousTakeId)
            {
                break;
            }
        }

        _startRetryPending = false;
    }

    private static IEnumerator UnlockWeaponsAfterDelay(int token, int sessionGeneration, int roundId)
    {
        yield return new WaitForSeconds(WeaponDelaySeconds);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || token != _takeId || _takeEnding || WinnerId >= 0
            || !GameModeManager.IsActive(GameMode.Infideltat))
        {
            yield break;
        }

        WeaponsUnlocked = true;
        _infidelBeepId = 0;
        _nextInfideltatBeepTime = Time.unscaledTime + InfideltatBeepInitialDelaySeconds;
        EnsureLoadouts();
        BroadcastLiveState();
    }

    private static void BeginNextTake()
    {
        if (_takeEnding || Plugin.Instance == null)
        {
            return;
        }

        _takeEnding = true;
        WeaponsUnlocked = false;
        ClearCurrentWeapons();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                GameModeRespawn.Schedule(client.PlayerId, GameModeManager.EffectiveRespawnDelaySeconds,
                    protectOnRespawn: false);
            }
        }

        Plugin.Instance.StartCoroutine(StartNextTakeAfterRespawn(
            GameModeManager.EffectiveRespawnDelaySeconds + 0.75f,
            SessionState.Generation, GameModeManager.RoundId));
    }

    private static void CompleteTimeoutWin()
    {
        if (_takeEnding || WinnerId >= 0 || InfideltatPlayerId < 0)
        {
            return;
        }

        AwardScore(InfideltatPlayerId, InfideltatRules.GetWinnerAward(true));
        GameModeHud.BroadcastTakeResult("<b>"
            + PlayerLookup.GetPlayerNameTag(InfideltatPlayerId)
            + " won the take</b>\n<i>The terrorists ran out of time</i>");
        BroadcastLiveState();

        if (WinnerId >= 0)
        {
            Announce(PlayerLookup.GetPlayerNameTag(WinnerId) + " reached " + KillsToWin
                + " points and won the round!");
            GameModeManager.CompleteCustomRound(TeamAssignment.ResolveTeamId(WinnerId));
            return;
        }

        BeginNextTake();
    }

    private static IEnumerator StartNextTakeAfterRespawn(float delay, int sessionGeneration, int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Infideltat))
        {
            yield break;
        }

        StartTake();
    }

    private static void StopTakeTransition()
    {
        _takeEnding = false;
    }

    private static void ClearCurrentWeapons()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                ClearPlayerWeapons(client.PlayerId);
            }
        }
    }

    private static void ClearPlayerWeapons(int playerId)
    {
        if (!ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client)
            || client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner
            || client.PlayerSpawner.player == null || !client.PlayerSpawner.player)
        {
            return;
        }

        PlayerPickup? pickup = client.PlayerSpawner.player.playerPickupScript;
        if (pickup != null)
        {
            WeaponService.ClearHeldWeapons(pickup);
        }
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infideltat) || !MyceliumNetwork.InLobby
            || !MyceliumNetwork.IsHost || WeaponService.IsFinalGameScreen
            || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 0.5f;
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner
                || client.PlayerSpawner.player == null || !client.PlayerSpawner.player
                || !client.PlayerSpawner.player.gameObject.activeInHierarchy)
            {
                continue;
            }

            PlayerPickup? pickup = client.PlayerSpawner.player.playerPickupScript;
            if (!WeaponsUnlocked)
            {
                if (pickup != null && ((pickup.objInHand != null && pickup.objInHand)
                    || (pickup.objInLeftHand != null && pickup.objInLeftHand)))
                {
                    WeaponService.ClearHeldWeapons(pickup);
                }
                continue;
            }

            Weapon? weapon = GetWeapon(pickup?.objInHand);
            if (weapon != null && weapon.name.StartsWith(WeaponName, StringComparison.Ordinal))
            {
                PendingLoadouts.Remove(client.PlayerId);
                continue;
            }

            if (!PendingLoadouts.TryGetValue(client.PlayerId, out float retryTime)
                || Time.unscaledTime >= retryTime)
            {
                GiveWeapon(client.PlayerId);
            }
        }
    }

    private static void GiveWeapon(int playerId)
    {
        PendingLoadouts[playerId] = Time.unscaledTime + 3f;
        WeaponService.GiveWeapon(playerId, WeaponName, SpareMagazines);
    }

    private static void TryEmitInfideltatBeep()
    {
        if (!WeaponsUnlocked || Time.unscaledTime < _nextInfideltatBeepTime)
        {
            return;
        }

        PlayerHealth? infidel = PlayerLookup.FindActivePlayerHealthById(InfideltatPlayerId);
        if (infidel == null || !infidel || !infidel.gameObject.activeInHierarchy)
        {
            _nextInfideltatBeepTime = Time.unscaledTime + 1f;
            return;
        }

        int beepId = ++_infidelBeepId;
        Vector3 position = infidel.transform.position;
        _nextInfideltatBeepTime = Time.unscaledTime + InfideltatBeepIntervalSeconds;
        ApplyInfideltatBeep(MyceliumNetwork.LobbyHost, _takeId, beepId, position);
        MyceliumNetwork.RPC(Plugin.InfideltatModId, nameof(Plugin.SyncInfideltatBeep),
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, _takeId, beepId, position);
    }

    private static void AwardScore(int playerId, int amount)
    {
        Scores.TryGetValue(playerId, out int currentScore);
            int nextScore = ScoreRules.AddPoints(currentScore, amount, KillsToWin);
            int awardedPoints = nextScore - currentScore;
        Scores[playerId] = nextScore;
            if (awardedPoints > 0)
        {
                GameModeHud.ShowScorePopupForPlayer(playerId, awardedPoints);
        }
        if (WinnerId < 0 && nextScore >= KillsToWin)
        {
            WinnerId = playerId;
        }
    }

    private static void SendRoleStates(bool announce)
    {
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client && client.PlayerSteamID != 0)
            {
                SendRoleToPlayer(new CSteamID(client.PlayerSteamID), client.PlayerId, announce);
            }
        }

        if (ClientInstance.Instance != null)
        {
            ApplyLocalRole(MyceliumNetwork.LobbyHost, _takeId,
                ClientInstance.Instance.PlayerId == InfideltatPlayerId, announce);
        }
    }

    private static void SendRoleToPlayer(CSteamID target, int playerId, bool announce)
    {
        if (playerId < 0 || target.m_SteamID == 0)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.InfideltatModId, nameof(Plugin.SyncInfideltatRole), target,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, _takeId,
            playerId == InfideltatPlayerId, announce);
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

    private static Weapon? GetWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static bool IsFinitePosition(Vector3 position)
    {
        return !float.IsNaN(position.x) && !float.IsInfinity(position.x)
            && !float.IsNaN(position.y) && !float.IsInfinity(position.y)
            && !float.IsNaN(position.z) && !float.IsInfinity(position.z);
    }

    private static string SerializeScores() => ScoreCodec.Serialize(Scores);

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            int revision = Sync.NextLiveRevision();
            ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
                GameModeManager.RoundId, revision, SerializeScores(), WinnerId.ToString(),
                _takeId.ToString(), WeaponsUnlocked ? "1" : "0",
                _takeTimeRemaining.ToString(CultureInfo.InvariantCulture));
            MyceliumNetwork.RPC(Plugin.InfideltatModId, nameof(Plugin.SyncInfideltatLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeScores(), WinnerId, _takeId,
                WeaponsUnlocked, _takeTimeRemaining, GameModeManager.RoundId, revision);
        }
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("infideltat", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 5, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[1], out int winnerId)
            || !int.TryParse(fields[2], out int takeId)
            || !LobbySnapshotCodec.TryParseBool(fields[3], out bool weaponsUnlocked)
            || !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture,
                out float takeTimeRemaining))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], winnerId, takeId, weaponsUnlocked,
            takeTimeRemaining, roundId, revision, ModeLobbyDataSync.Source("infideltat", "live"));
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            GameModeHud.BroadcastTakeResult(text);
        }
    }
}