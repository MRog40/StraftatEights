using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class InfidelState
{
    internal const string SettingsLobbyDataKey = "StraftatEights_Infidel_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_Infidel_Live";
    internal const string WeaponName = "AK-K";
    internal const int SpareMagazines = 2;
    internal const float InfidelHealth = 200f / 25f;
    internal const float TerroristHealth = 100f / 25f;
    internal const float MovementMultiplier = 0.7f;
    internal const float WeaponDelaySeconds = 10f;
    internal const float DefaultTakeTimeLimitSeconds = ModeTimeoutRules.DefaultRoundSeconds;
    internal const float RoleAnnouncementDuration = WeaponDelaySeconds + 10f;

    internal static bool Enabled;
    internal static int InfidelPlayerId { get; private set; } = -1;
    internal static int WinnerId { get; private set; } = -1;
    internal static int KillsToWin => GameModeManager.EffectivePointsToWin;
    internal static float TakeTimeRemaining => Mathf.Max(0f, _takeTimeRemaining);
    internal static bool WeaponsUnlocked { get; private set; }
    internal static bool LocalIsInfidel { get; private set; }
    internal static readonly Dictionary<int, int> Scores = new();

    private static readonly HashSet<int> AlivePlayers = new();
    private static readonly HashSet<int> PendingHealthResets = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static float _nextLoadoutCheckTime;
    private static float _takeTimeRemaining;
    private static readonly ModeSyncState Sync = new();
    private static int _takeId;
    private static int _localRoleTakeId = -1;
    private static int _localRoleAnnouncedTakeId = -1;
    private static bool _startRetryPending;
    private static bool _takeEnding;

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.InfidelEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.InfidelEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.InfidelModId, nameof(Plugin.SyncInfidelSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.InfidelEnabled.Value);
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

        MyceliumNetwork.RPCTarget(Plugin.InfidelModId, nameof(Plugin.SyncInfidelSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.InfidelEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.InfidelModId, nameof(Plugin.SyncInfidelLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializeScores(), WinnerId,
            _takeId, WeaponsUnlocked, _takeTimeRemaining, GameModeManager.RoundId,
            Sync.LiveRevision);

        if (InfidelPlayerId >= 0 && TakeIsActive())
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
        if (wasAlive && GameModeManager.IsActive(GameMode.Infidel)
            && WinnerId < 0 && !_takeEnding)
        {
            OnServerKill(playerId, -1);
        }

        bool changed = wasAlive || (playerId >= 0 && Scores.Remove(playerId));
        PendingHealthResets.Remove(playerId);
        PendingLoadouts.Remove(playerId);
        AlivePlayers.Remove(playerId);
        if (playerId >= 0 && InfidelPlayerId == playerId)
        {
            InfidelPlayerId = -1;
            changed = true;
        }

        if (changed && GameModeManager.IsActive(GameMode.Infidel))
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
        _localRoleTakeId = -1;
        _localRoleAnnouncedTakeId = -1;
        _startRetryPending = false;
        _takeEnding = false;
        InfidelPlayerId = -1;
        WinnerId = -1;
        WeaponsUnlocked = false;
        LocalIsInfidel = false;
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
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infidel) || !MyceliumNetwork.IsHost)
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
            || !GameModeManager.IsActive(GameMode.Infidel)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WinnerId >= 0 || !TakeIsActive())
        {
            return;
        }

        _takeTimeRemaining = Mathf.Max(0f, _takeTimeRemaining - Mathf.Max(0f, deltaTime));
        if (_takeTimeRemaining <= 0f)
        {
            CompleteTimeoutWin();
        }
    }

    internal static void ClientTick(float deltaTime)
    {
        if (MyceliumNetwork.IsHost || !Enabled
            || !GameModeManager.IsActive(GameMode.Infidel)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WinnerId >= 0 || !TakeIsActive())
        {
            return;
        }

        _takeTimeRemaining = Mathf.Max(0f, _takeTimeRemaining - Mathf.Max(0f, deltaTime));
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infidel)
            || WinnerId >= 0 || _takeEnding)
        {
            return;
        }

        bool deadWasInfidel = deadPlayerId == InfidelPlayerId;
        AlivePlayers.Remove(deadPlayerId);

        if (killerId >= 0 && killerId != deadPlayerId)
        {
            bool killerIsInfidel = killerId == InfidelPlayerId;
            int killerAward = InfidelRules.GetKillerAward(deadWasInfidel, killerIsInfidel);
            if (killerAward > 0)
            {
                AwardScore(killerId, killerAward);
            }
        }

        bool infidelWon = !deadWasInfidel && AlivePlayers.Count == 1
            && AlivePlayers.Contains(InfidelPlayerId);
        int winnerAward = InfidelRules.GetWinnerAward(infidelWon);
        if (winnerAward > 0)
        {
            AwardScore(InfidelPlayerId, winnerAward);
        }

        BroadcastLiveState();
        if (deadWasInfidel)
        {
            GameModeHud.BroadcastTakeResult(
                "<b>The terrorists won the take</b>\n<i>The Infidel was eliminated</i>");
        }
        else if (infidelWon)
        {
            GameModeHud.BroadcastTakeResult("<b>"
                + PlayerLookup.GetPlayerNameTag(InfidelPlayerId)
                + " won the take</b>\n<i>All terrorists were eliminated</i>");
        }
        if (WinnerId >= 0)
        {
            Announce(PlayerLookup.GetPlayerNameTag(WinnerId) + " reached " + KillsToWin
                + " points and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(WinnerId));
            return;
        }

        if (deadWasInfidel || (!deadWasInfidel && AlivePlayers.Count == 1))
        {
            BeginNextTake();
        }
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infidel) || !MyceliumNetwork.IsHost)
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

    internal static void ApplyHealth(PlayerHealth health)
    {
        int playerId = health.playerValues?.playerClient?.PlayerId ?? -1;
        float maximumHealth = IsInfidel(health) ? InfidelHealth : TerroristHealth;
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

    internal static bool IsInfidel(PlayerHealth health)
    {
        int playerId = health.playerValues?.playerClient?.PlayerId ?? -1;
        if (MyceliumNetwork.IsHost)
        {
            return playerId >= 0 && playerId == InfidelPlayerId;
        }

        return ClientInstance.Instance != null && ClientInstance.Instance.PlayerId == playerId
            && LocalIsInfidel;
    }

    internal static bool IsAllowedWeapon(Weapon weapon, int playerId)
    {
        return weapon != null && WeaponsUnlocked
            && weapon.name.StartsWith(WeaponName, StringComparison.Ordinal);
    }

    internal static void ApplyLocalRole(CSteamID hostId, int takeId, bool isInfidel, bool announce)
    {
        if (hostId != MyceliumNetwork.LobbyHost || takeId < _localRoleTakeId)
        {
            return;
        }

        _localRoleTakeId = takeId;
        LocalIsInfidel = isInfidel;
        if (announce && GameModeManager.IsActive(GameMode.Infidel)
            && GameModeManager.Phase == GameModePhase.ActiveRound
            && !GameModeManager.IsMatchOver && _localRoleAnnouncedTakeId != takeId)
        {
            _localRoleAnnouncedTakeId = takeId;
            GameModeHud.AnnounceTarget(isInfidel
                ? "You are the <color=#CC2222><b>Infidel</b></color>."
                : "You are a <color=#4D9BFF><b>terrorist</b></color>.", RoleAnnouncementDuration);
        }
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
            InfidelPlayerId = -1;
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

        InfidelPlayerId = players[UnityEngine.Random.Range(0, players.Count)];
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
                || WinnerId >= 0 || _takeEnding || !GameModeManager.IsActive(GameMode.Infidel))
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
            || !GameModeManager.IsActive(GameMode.Infidel))
        {
            yield break;
        }

        WeaponsUnlocked = true;
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
        if (_takeEnding || WinnerId >= 0 || InfidelPlayerId < 0)
        {
            return;
        }

        AwardScore(InfidelPlayerId, InfidelRules.GetWinnerAward(true));
        GameModeHud.BroadcastTakeResult("<b>"
            + PlayerLookup.GetPlayerNameTag(InfidelPlayerId)
            + " won the take</b>\n<i>The terrorists ran out of time</i>");
        BroadcastLiveState();

        if (WinnerId >= 0)
        {
            Announce(PlayerLookup.GetPlayerNameTag(WinnerId) + " reached " + KillsToWin
                + " points and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(WinnerId));
            return;
        }

        BeginNextTake();
    }

    private static IEnumerator StartNextTakeAfterRespawn(float delay, int sessionGeneration, int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.Infidel))
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
        if (!Enabled || !GameModeManager.IsActive(GameMode.Infidel) || !MyceliumNetwork.InLobby
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

    private static void AwardScore(int playerId, int amount)
    {
        Scores.TryGetValue(playerId, out int currentScore);
        int nextScore = currentScore + amount;
        Scores[playerId] = nextScore;
        if (amount > 0)
        {
            GameModeHud.ShowScorePopupForPlayer(playerId, amount);
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
                ClientInstance.Instance.PlayerId == InfidelPlayerId, announce);
        }
    }

    private static void SendRoleToPlayer(CSteamID target, int playerId, bool announce)
    {
        if (playerId < 0 || target.m_SteamID == 0)
        {
            return;
        }

        MyceliumNetwork.RPCTarget(Plugin.InfidelModId, nameof(Plugin.SyncInfidelRole), target,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, _takeId,
            playerId == InfidelPlayerId, announce);
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
            MyceliumNetwork.RPC(Plugin.InfidelModId, nameof(Plugin.SyncInfidelLiveState), ReliableType.Reliable,
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
                ModeLobbyDataSync.Source("infidel", "settings")))
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
            takeTimeRemaining, roundId, revision, ModeLobbyDataSync.Source("infidel", "live"));
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            GameModeHud.BroadcastTakeResult(text);
        }
    }
}