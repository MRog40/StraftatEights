using System;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class OneInTheChamberState
{
    internal const float PlayerHealth = 10f;
    internal const string PistolWeaponName = "Pistol";
    internal const string CouperetWeaponName = "Couperet";
    internal static bool Enabled;
    internal static int AliveCount => AlivePlayers.Count;
    internal static readonly HashSet<int> AlivePlayers = new();
    internal static readonly Dictionary<int, int> ReserveBullets = new();

    private static float _nextSettingsPushTime;
    private static float _nextLiveStatePushTime;
    private static float _nextLoadoutCheckTime;
    private static int _settingsRevision;
    private static int _lastSettingsRoundId = -1;
    private static int _lastSettingsRevision = -1;
    private static int _liveStateRevision;
    private static int _lastLiveStateRoundId = -1;
    private static int _lastLiveStateRevision = -1;
    private static readonly HashSet<int> RoundPlayers = new();
    private static readonly Dictionary<int, float> PendingRightLoadouts = new();
    private static readonly Dictionary<int, float> PendingLeftLoadouts = new();

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
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, ++_settingsRevision,
            Plugin.OneInTheChamberEnabled.Value);
    }

    internal static void PeriodicPushSettingsIfHost()
    {
        if (HostSettingsSync.IsDue(ref _nextSettingsPushTime))
        {
            PushSettingsIfHost();
        }
    }

    internal static void PeriodicPushIfHost()
    {
        if (HostSettingsSync.IsDue(ref _nextLiveStatePushTime))
        {
            SyncReserveBulletsFromWeapons();
            BroadcastLiveState();
        }
    }

    internal static void OnLobbyEntered()
    {
        _lastSettingsRoundId = -1;
        _lastSettingsRevision = -1;
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
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, _settingsRevision,
            Plugin.OneInTheChamberEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.OneInTheChamberModId, nameof(Plugin.SyncOneInTheChamberLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializeAlive(), SerializeBullets(),
            GameModeManager.RoundId, _liveStateRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastSettingsRoundId, ref _lastSettingsRevision);
    }

    internal static void ResetMatchState()
    {
        _liveStateRevision++;
        _lastLiveStateRoundId = -1;
        _lastLiveStateRevision = -1;
        _nextLoadoutCheckTime = 0f;
        AlivePlayers.Clear();
        RoundPlayers.Clear();
        ReserveBullets.Clear();
        PendingRightLoadouts.Clear();
        PendingLeftLoadouts.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string aliveData, string bulletsData,
        int roundId, int revision)
    {
        if (!SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastLiveStateRoundId, ref _lastLiveStateRevision))
        {
            return;
        }

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
        ApplyLocalReserveBullets();
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetMatchState();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                RoundPlayers.Add(client.PlayerId);
                AlivePlayers.Add(client.PlayerId);
                ReserveBullets[client.PlayerId] = 0;
            }
        }

        EnsureLoadouts();
        BroadcastLiveState();
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber))
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
            FinishRound(GetLastAlivePlayerId());
            return;
        }

        SyncReserveBulletsFromWeapons();
        BroadcastLiveState();
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.OneInTheChamber)
            || !MyceliumNetwork.IsHost)
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
        WeaponService.GiveWeapon(playerId, PistolWeaponName);
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

    private static void FinishRound(int winnerId)
    {
        if (winnerId < 0 || ScoreManager.Instance == null)
        {
            return;
        }

        Announce(PlayerLookup.GetPlayerNameTag(winnerId) + " won the <b>ONE IN THE CHAMBER</b> round!");
        BroadcastLiveState();
        GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(winnerId));
    }

    private static string SerializeAlive() => string.Join(",", AlivePlayers);

    private static string SerializeBullets() => ScoreCodec.Serialize(ReserveBullets);

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
                MyceliumNetwork.LobbyHost, SerializeAlive(), SerializeBullets(),
                GameModeManager.RoundId, ++_liveStateRevision);
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