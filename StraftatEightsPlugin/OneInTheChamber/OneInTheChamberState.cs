using System;
using System.Collections;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class OneInTheChamberState
{
    internal const float PlayerHealth = 10f;
    internal const int PointsPerRoundWin = ScoreRules.PointsPerRoundWin;
    internal const string PistolWeaponName = "Pistol";
    internal const string CouperetWeaponName = "Couperet";
    internal static bool Enabled;
    internal static int AliveCount => AlivePlayers.Count;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static int SubRoundId { get; private set; }
    internal static int WinnerId { get; private set; } = -1;
    internal static readonly HashSet<int> AlivePlayers = new();
    internal static readonly Dictionary<int, int> ReserveBullets = new();
    internal static readonly Dictionary<int, int> Scores = new();

    private static float _nextLoadoutCheckTime;
    private static readonly ModeSyncState Sync = new();
    private static readonly HashSet<int> RoundPlayers = new();
    private static readonly Dictionary<int, float> PendingRightLoadouts = new();
    private static readonly Dictionary<int, float> PendingLeftLoadouts = new();
    private static bool _startRetryPending;
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

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.OneInTheChamberEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        MyceliumNetwork.RPC(Plugin.OneInTheChamberModId, nameof(Plugin.SyncOneInTheChamberSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.NextSettingsRevision(),
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
        if (MyceliumNetwork.IsHost)
        {
            ApplySettingsFromHostConfig();
            ResetMatchState();
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
            SerializeScores(), SubRoundId, WinnerId, GameModeManager.RoundId, Sync.LiveRevision);

        if (SubRoundId > 0 && WinnerId < 0 && !_subRoundEnding)
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

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        StopSubRoundTransition();
        Sync.ResetLiveState();
        _nextLoadoutCheckTime = 0f;
        _startRetryPending = false;
        SubRoundId = 0;
        WinnerId = -1;
        AlivePlayers.Clear();
        RoundPlayers.Clear();
        ReserveBullets.Clear();
        Scores.Clear();
        PendingRightLoadouts.Clear();
        PendingLeftLoadouts.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string aliveData, string bulletsData,
        string scoresData, int subRoundId, int winnerId, int roundId, int revision)
    {
        int previousRoundId = Sync.LastLiveRoundId;
        if (subRoundId < 0 || winnerId < -1
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision))
        {
            return;
        }

        SubRoundId = roundId != previousRoundId
            ? subRoundId
            : Math.Max(SubRoundId, subRoundId);
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
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetMatchState();
        StartSubRound();
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber)
            || WinnerId >= 0 || _subRoundEnding)
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
            }

            if (WinnerId >= 0)
            {
                FinishMatch(roundWinnerId);
            }
            else
            {
                BeginNextSubRound();
            }
            return;
        }

        SyncReserveBulletsFromWeapons();
        BroadcastLiveState();
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber)
            || !MyceliumNetwork.IsHost || WinnerId >= 0 || _subRoundEnding)
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
            || WinnerId >= 0 || _subRoundEnding || Time.unscaledTime < _nextLoadoutCheckTime)
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
            }
            else if (!PendingRightLoadouts.TryGetValue(playerId, out float rightRetry)
                || Time.unscaledTime >= rightRetry)
            {
                RequestRightLoadout(playerId);
            }

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

    private static void StopSubRoundTransition()
    {
        _subRoundEnding = false;
    }

    private static void StartSubRound()
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
            ScheduleStartSubRoundRetry();
            return;
        }

        SubRoundId++;
        _subRoundEnding = false;
        _nextLoadoutCheckTime = 0f;
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

    private static void ScheduleStartSubRoundRetry()
    {
        if (_startRetryPending || Plugin.Instance == null)
        {
            return;
        }

        _startRetryPending = true;
        Plugin.Instance.StartCoroutine(RetryStartSubRound(SessionState.Generation,
            GameModeManager.RoundId, SubRoundId));
    }

    private static IEnumerator RetryStartSubRound(int sessionGeneration, int roundId,
        int previousSubRoundId)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            yield return new WaitForSeconds(0.25f);
            if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
                || WinnerId >= 0 || _subRoundEnding
                || !GameModeManager.IsActive(GameMode.OneInTheChamber))
            {
                break;
            }

            StartSubRound();
            if (SubRoundId > previousSubRoundId)
            {
                break;
            }
        }

        _startRetryPending = false;
    }

    private static void BeginNextSubRound()
    {
        if (_subRoundEnding || Plugin.Instance == null)
        {
            return;
        }

        _subRoundEnding = true;
        ClearCurrentWeapons();
        foreach (int playerId in RoundPlayers)
        {
            if (ClientInstance.playerInstances.ContainsKey(playerId))
            {
                GameModeRespawn.Schedule(playerId, GameModeManager.EffectiveRespawnDelaySeconds);
            }
        }

        Plugin.Instance.StartCoroutine(StartNextSubRoundAfterRespawn(
            GameModeManager.EffectiveRespawnDelaySeconds + 0.75f,
            SessionState.Generation, GameModeManager.RoundId));
    }

    private static IEnumerator StartNextSubRoundAfterRespawn(float delay, int sessionGeneration,
        int roundId)
    {
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || WinnerId >= 0 || !GameModeManager.IsActive(GameMode.OneInTheChamber))
        {
            yield break;
        }

        StartSubRound();
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
            MyceliumNetwork.RPC(Plugin.OneInTheChamberModId, nameof(Plugin.SyncOneInTheChamberLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeAlive(), SerializeBullets(), SerializeScores(),
                SubRoundId, WinnerId, GameModeManager.RoundId, Sync.NextLiveRevision());
        }
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.OneInTheChamberModId, nameof(Plugin.OneInTheChamberAnnounce), ReliableType.Reliable, text);
        }
    }
}