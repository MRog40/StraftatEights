using System;
using System.Collections;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class OneInTheChamberState
{
    internal const string SettingsLobbyDataKey = "StraftatEights_OneInTheChamber_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_OneInTheChamber_Live";
    internal const float PlayerHealth = 0.4f;
    internal const int PointsPerRoundWin = ScoreRules.PointsPerRoundWin;
    internal const string PistolWeaponName = "Revolver";
    internal const string CouperetWeaponName = "Couperet";
    private const float LoadoutDelaySeconds = 5f;
    internal static bool Enabled;
    internal static int AliveCount => AlivePlayers.Count;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static int TakeId { get; private set; }
    internal static int WinnerId { get; private set; } = -1;
    internal static readonly HashSet<int> AlivePlayers = new();
    internal static readonly Dictionary<int, int> ReserveBullets = new();
    internal static readonly Dictionary<int, int> Scores = new();

    private static float _nextLoadoutCheckTime;
    private static float _loadoutsAvailableAt;
    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static readonly HashSet<int> RoundPlayers = new();
    private static readonly Dictionary<int, float> PendingRightLoadouts = new();
    private static readonly Dictionary<int, float> PendingLeftLoadouts = new();
    private static bool _startRetryPending;
    private static bool _takeEnding;
    private static float _nextClientLivePollTime;
    private static float _loadoutCountdownEndsAt;

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.OneInTheChamberEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.OneInTheChamberEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.OneInTheChamberModId, nameof(Plugin.SyncOneInTheChamberSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.OneInTheChamberEnabled.Value);
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
        if (Sync.IsLivePushDue())
        {
            SyncReserveBulletsFromWeapons();
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

    internal static void PollLiveStateIfClient()
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !GameModeManager.IsActive(GameMode.OneInTheChamber)
            || Time.unscaledTime < _nextClientLivePollTime)
        {
            return;
        }

        _nextClientLivePollTime = Time.unscaledTime + 1f;
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

        MyceliumNetwork.RPCTarget(Plugin.OneInTheChamberModId, nameof(Plugin.SyncOneInTheChamberSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.OneInTheChamberEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.OneInTheChamberModId, nameof(Plugin.SyncOneInTheChamberLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializeAlive(), SerializeBullets(),
            SerializeScores(), TakeId, WinnerId, GetLoadoutCountdownSeconds(),
            GameModeManager.RoundId, Sync.LiveRevision);

        if (TakeId > 0 && WinnerId < 0 && !_takeEnding)
        {
            int playerId = FindPlayerId(player);
            if (playerId >= 0)
            {
                RoundPlayers.Add(playerId);
                AlivePlayers.Add(playerId);
                ReserveBullets.TryAdd(playerId, 0);
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
        if (wasAlive && GameModeManager.IsActive(GameMode.OneInTheChamber)
            && WinnerId < 0 && !_takeEnding)
        {
            OnServerKill(playerId, -1);
        }

        bool changed = wasAlive || (playerId >= 0 && RoundPlayers.Remove(playerId));
        AlivePlayers.Remove(playerId);
        ReserveBullets.Remove(playerId);
        Scores.Remove(playerId);
        PendingRightLoadouts.Remove(playerId);
        PendingLeftLoadouts.Remove(playerId);
        if (changed && GameModeManager.IsActive(GameMode.OneInTheChamber))
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
        _loadoutsAvailableAt = 0f;
        _loadoutCountdownEndsAt = 0f;
        _startRetryPending = false;
        TakeId = 0;
        WinnerId = -1;
        AlivePlayers.Clear();
        RoundPlayers.Clear();
        ReserveBullets.Clear();
        Scores.Clear();
        PendingRightLoadouts.Clear();
        PendingLeftLoadouts.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string aliveData, string bulletsData,
        string scoresData, int takeId, int winnerId, float loadoutSecondsRemaining,
        int roundId, int revision,
        string source = "rpc")
    {
        int previousRoundId = Sync.LastLiveRoundId;
        if (takeId < 0 || winnerId < -1 || loadoutSecondsRemaining < 0f
            || float.IsNaN(loadoutSecondsRemaining) || float.IsInfinity(loadoutSecondsRemaining)
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        TakeId = roundId != previousRoundId
            ? takeId
            : Math.Max(TakeId, takeId);
        WinnerId = winnerId;
        AlivePlayers.Clear();
        foreach (int playerId in ParseIds(aliveData))
        {
            AlivePlayers.Add(playerId);
        }

        ReserveBullets.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(bulletsData, int.MaxValue))
        {
            ReserveBullets[entry.Key] = entry.Value;
        }
        Scores.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(scoresData, PointsToWin))
        {
            Scores[entry.Key] = entry.Value;
        }
        ApplyLocalReserveBullets();
        _loadoutCountdownEndsAt = loadoutSecondsRemaining > 0f
            ? Time.unscaledTime + loadoutSecondsRemaining
            : 0f;
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetMatchState();
        StartTake();
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber)
            || WinnerId >= 0 || _takeEnding)
        {
            return;
        }

        EnsureTrackedPlayers();
        if (!OneInTheChamberRules.ApplyDeath(AlivePlayers, ReserveBullets, deadPlayerId, killerId))
        {
            return;
        }

        if (killerId >= 0 && killerId != deadPlayerId && AlivePlayers.Contains(killerId))
        {
            AddBulletToPistol(killerId);
        }

        if (AlivePlayers.Count <= 1)
        {
            int roundWinnerId = GetLastAlivePlayerId();
            if (roundWinnerId >= 0)
            {
                AwardScore(roundWinnerId, PointsPerRoundWin);
                GameModeHud.BroadcastTakeResult("<b>"
                    + PlayerLookup.GetPlayerNameTag(roundWinnerId)
                    + " WON THE TAKE</b>\n<i>LAST PLAYER STANDING</i>");
            }

            if (WinnerId >= 0)
            {
                FinishMatch(roundWinnerId);
            }
            else
            {
                BeginNextTake();
            }
            return;
        }

        SyncReserveBulletsFromWeapons();
        BroadcastLiveState();
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber)
            || !MyceliumNetwork.IsHost || WinnerId >= 0 || _takeEnding
            || Time.unscaledTime < _loadoutsAvailableAt)
        {
            return;
        }

        RequestRightLoadout(playerId);
    }

    internal static bool IsPistol(Weapon weapon)
    {
        return weapon != null && weapon.name.StartsWith(PistolWeaponName, StringComparison.Ordinal);
    }

    internal static bool IsCouperet(Weapon weapon)
    {
        return weapon != null && weapon.name.StartsWith(CouperetWeaponName, StringComparison.Ordinal);
    }

    internal static void ApplyHealth(PlayerHealth health)
    {
        health.fullHealth = PlayerHealth;
        if (!health.IsServer)
        {
            return;
        }

        float excessHealth = health.sync___get_value_health() - PlayerHealth;
        if (excessHealth <= 0f)
        {
            return;
        }

        HealthSettingsTuning.ApplyingPassiveHealth = true;
        try
        {
            FishNetCompatibility.TryRemoveHealth(health, excessHealth);
        }
        finally
        {
            HealthSettingsTuning.ApplyingPassiveHealth = false;
        }
    }

    internal static void EnforcePistolAmmo(Weapon weapon, int playerId)
    {
        ReserveBullets.TryGetValue(playerId, out int spareRounds);
        WeaponAmmoTuning.InitializeSingleShot(weapon, spareRounds);
        WeaponAmmoTuning.TryStartManualReload(weapon, true, 0);

        if (weapon.IsOwner && weapon.inRightHand && PauseManager.Instance != null)
        {
            PauseManager.Instance.MoveAmmoDisplay(true, true);
            PauseManager.Instance.ChangeAmmoText(Math.Max(0, weapon.currentAmmo).ToString(),
                WeaponAmmoTuning.GetSpareRounds(weapon) + " / ", true);
        }
    }

    private static void EnsureTrackedPlayers()
    {
        if (RoundPlayers.Count != 0)
        {
            return;
        }

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                RoundPlayers.Add(client.PlayerId);
                AlivePlayers.Add(client.PlayerId);
                ReserveBullets.TryAdd(client.PlayerId, 0);
            }
        }
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber)
            || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost || WeaponService.IsFinalGameScreen
            || WinnerId >= 0 || _takeEnding || Time.unscaledTime < _loadoutsAvailableAt
            || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 0.5f;
        foreach (int playerId in AlivePlayers)
        {
            if (!ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client)
                || client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner)
            {
                continue;
            }

            PlayerPickup? pickup = client.PlayerSpawner.player?.playerPickupScript;
            if (pickup == null)
            {
                continue;
            }

            Weapon? rightWeapon = GetWeapon(pickup.objInHand);
            Weapon? leftWeapon = GetWeapon(pickup.objInLeftHand);
            if (rightWeapon != null && IsPistol(rightWeapon))
            {
                ReserveBullets.TryGetValue(playerId, out int spareRounds);
                WeaponAmmoTuning.InitializeSingleShot(rightWeapon, spareRounds);
                PendingRightLoadouts.Remove(playerId);

                if (leftWeapon != null && IsCouperet(leftWeapon))
                {
                    PendingLeftLoadouts.Remove(playerId);
                }
                else if (!PendingLeftLoadouts.TryGetValue(playerId, out float leftRetry)
                    || Time.unscaledTime >= leftRetry)
                {
                    PendingLeftLoadouts[playerId] = Time.unscaledTime + 2f;
                    WeaponService.GiveWeaponToLeftHand(playerId, CouperetWeaponName);
                }
            }
            else if (!PendingRightLoadouts.TryGetValue(playerId, out float rightRetry)
                || Time.unscaledTime >= rightRetry)
            {
                RequestRightLoadout(playerId);
            }
        }
    }

    private static void RequestRightLoadout(int playerId)
    {
        PendingRightLoadouts[playerId] = Time.unscaledTime + 2f;
        WeaponService.GiveWeapon(playerId, PistolWeaponName, clearBothHands: false);
    }

    private static void AddBulletToPistol(int playerId)
    {
        PlayerPickup? pickup = FindPickup(playerId);
        Weapon? weapon = pickup == null ? null : GetWeapon(pickup.objInHand);
        if (weapon != null && IsPistol(weapon))
        {
            WeaponAmmoTuning.AddSingleShotSpareRounds(weapon, 1);
        }
    }

    private static void SyncReserveBulletsFromWeapons()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        foreach (int playerId in AlivePlayers)
        {
            PlayerPickup? pickup = FindPickup(playerId);
            Weapon? weapon = pickup == null ? null : GetWeapon(pickup.objInHand);
            if (weapon != null && IsPistol(weapon) && WeaponAmmoTuning.IsSingleShot(weapon))
            {
                ReserveBullets[playerId] = WeaponAmmoTuning.GetSpareRounds(weapon);
            }
        }
    }

    private static void ApplyLocalReserveBullets()
    {
        if (ClientInstance.Instance == null)
        {
            return;
        }

        int playerId = ClientInstance.Instance.PlayerId;
        if (!ReserveBullets.TryGetValue(playerId, out int spareRounds))
        {
            return;
        }

        PlayerPickup? pickup = ClientInstance.Instance.PlayerSpawner?.player?.playerPickupScript;
        Weapon? weapon = pickup == null ? null : GetWeapon(pickup.objInHand);
        if (weapon != null && IsPistol(weapon))
        {
            WeaponAmmoTuning.SetSingleShotSpareRounds(weapon, spareRounds);
        }
    }

    private static PlayerPickup? FindPickup(int playerId)
    {
        if (!ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client)
            || client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner)
        {
            return null;
        }

        return client.PlayerSpawner.player?.playerPickupScript;
    }

    private static Weapon? GetWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static int GetLastAlivePlayerId()
    {
        foreach (int playerId in AlivePlayers)
        {
            return playerId;
        }
        return -1;
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

    private static void StopTakeTransition()
    {
        _takeEnding = false;
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
            ScheduleStartTakeRetry();
            return;
        }

        TakeId++;
        _takeEnding = false;
        _nextLoadoutCheckTime = 0f;
        _loadoutsAvailableAt = Time.unscaledTime + LoadoutDelaySeconds;
        _loadoutCountdownEndsAt = _loadoutsAvailableAt;
        PendingRightLoadouts.Clear();
        PendingLeftLoadouts.Clear();
        AlivePlayers.Clear();
        RoundPlayers.Clear();
        ReserveBullets.Clear();
        foreach (int playerId in players)
        {
            RoundPlayers.Add(playerId);
            AlivePlayers.Add(playerId);
            ReserveBullets[playerId] = 0;
            Scores.TryAdd(playerId, 0);
        }

        ClearCurrentWeapons();
        EnsureLoadouts();
        BroadcastLiveState();
    }

    internal static string GetLoadoutCountdownText()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber)
            || GameModeManager.Phase != GameModePhase.ActiveRound)
        {
            return string.Empty;
        }

        float countdownEnd = MyceliumNetwork.IsHost
            ? _loadoutsAvailableAt
            : _loadoutCountdownEndsAt;
        int secondsRemaining = Mathf.CeilToInt(countdownEnd - Time.unscaledTime);
        return secondsRemaining > 0
            ? $"<color=#FFD35A><b>WEAPONS IN {secondsRemaining}</b></color>"
            : string.Empty;
    }

    private static void ScheduleStartTakeRetry()
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
                || WinnerId >= 0 || _takeEnding
                || !GameModeManager.IsActive(GameMode.OneInTheChamber))
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

    private static void BeginNextTake()
    {
        if (_takeEnding || Plugin.Instance == null)
        {
            return;
        }

        _takeEnding = true;
        ClearCurrentWeapons();
        foreach (int playerId in RoundPlayers)
        {
            if (ClientInstance.playerInstances.ContainsKey(playerId))
            {
                GameModeRespawn.Schedule(playerId, GameModeManager.EffectiveRespawnDelaySeconds);
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
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.OneInTheChamber))
        {
            yield break;
        }

        StartTake();
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

    private static void AwardScore(int playerId, int amount)
    {
        Scores.TryGetValue(playerId, out int currentScore);
        int nextScore = currentScore + amount;
        Scores[playerId] = nextScore;
        if (amount > 0)
        {
            GameModeHud.ShowScorePopupForPlayer(playerId, amount);
        }
        if (WinnerId < 0 && nextScore >= PointsToWin)
        {
            WinnerId = playerId;
        }
    }

    private static void FinishMatch(int winnerId)
    {
        if (winnerId < 0 || ScoreManager.Instance == null)
        {
            return;
        }

        Announce(PlayerLookup.GetPlayerNameTag(winnerId) + " won the <b>ONE IN THE CHAMBER</b> match!");
        BroadcastLiveState();
        GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(winnerId));
    }

    private static string SerializeAlive() => string.Join(",", AlivePlayers);

    private static string SerializeBullets() => ScoreCodec.Serialize(ReserveBullets);

    private static string SerializeScores() => ScoreCodec.Serialize(Scores);

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
            float loadoutSecondsRemaining = GetLoadoutCountdownSeconds();
            ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
                GameModeManager.RoundId, revision, SerializeAlive(), SerializeBullets(),
                SerializeScores(), TakeId.ToString(), WinnerId.ToString(),
                loadoutSecondsRemaining.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            MyceliumNetwork.RPC(Plugin.OneInTheChamberModId, nameof(Plugin.SyncOneInTheChamberLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeAlive(), SerializeBullets(), SerializeScores(),
                TakeId, WinnerId, loadoutSecondsRemaining, GameModeManager.RoundId, revision);
        }
    }

    private static float GetLoadoutCountdownSeconds()
    {
        return _loadoutsAvailableAt <= 0f
            ? 0f
            : Mathf.Max(0f, _loadoutsAvailableAt - Time.unscaledTime);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("one-in-the-chamber", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 6, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[3], out int takeId)
            || !int.TryParse(fields[4], out int winnerId)
            || !float.TryParse(fields[5], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float loadoutSecondsRemaining))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], fields[1], fields[2], takeId, winnerId,
            loadoutSecondsRemaining, roundId, revision,
            ModeLobbyDataSync.Source("one-in-the-chamber", "live"));
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.OneInTheChamberModId, nameof(Plugin.OneInTheChamberAnnounce), ReliableType.Reliable, text);
        }
    }
}