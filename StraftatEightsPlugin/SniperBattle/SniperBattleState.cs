using System;
using System.Collections.Generic;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class SniperBattleState
{
    internal const string WeaponName = "M2000";
    internal const float PlayerHealth = 10f;
    internal static bool Enabled;
    internal static int PointsToWin = 10;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Points = new();

    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static float _nextSettingsPushTime;
    private static float _nextLiveStatePushTime;
    private static int _settingsRevision;
    private static int _lastSettingsRoundId = -1;
    private static int _lastSettingsRevision = -1;
    private static int _liveStateRevision;
    private static int _lastLiveStateRoundId = -1;
    private static int _lastLiveStateRevision = -1;
    private static float _nextLoadoutCheckTime;

    internal static void ApplySettings(bool enabled, int pointsToWin)
    {
        bool changed = Enabled != enabled || PointsToWin != pointsToWin;
        Enabled = enabled;
        PointsToWin = Mathf.Clamp(pointsToWin, 3, 30);
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplyFromConfig() => ApplySettings(Plugin.SniperBattleEnabled.Value, Plugin.SniperBattlePointsToWin.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplyFromConfig();
        MyceliumNetwork.RPC(Plugin.SniperBattleModId, nameof(Plugin.SyncSniperBattleSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, ++_settingsRevision,
            Plugin.SniperBattleEnabled.Value, Plugin.SniperBattlePointsToWin.Value);
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
           PeriodicPushSettingsIfHost();
           if (HostSettingsSync.IsDue(ref _nextLiveStatePushTime))
           {
               BroadcastLiveState();
           }
       }

    internal static void OnLobbyEntered()
    {
        _lastSettingsRoundId = -1;
        _lastSettingsRevision = -1;
        if (MyceliumNetwork.IsHost)
        {
            ApplyFromConfig();
            ResetMatchState();
        }
    }

    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }
        MyceliumNetwork.RPCTarget(Plugin.SniperBattleModId, nameof(Plugin.SyncSniperBattleSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, _settingsRevision,
            Plugin.SniperBattleEnabled.Value, Plugin.SniperBattlePointsToWin.Value);
        MyceliumNetwork.RPCTarget(Plugin.SniperBattleModId, nameof(Plugin.SyncSniperBattleLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializePoints(), WinnerId,
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
        WinnerId = -1;
        Points.Clear();
        PendingLoadouts.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string pointsData, int winnerId, int roundId, int revision)
    {
        if (winnerId < -1)
        {
            return;
        }
        if (!SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastLiveStateRoundId, ref _lastLiveStateRevision))
        {
            return;
        }
        WinnerId = winnerId;
        Points.Clear();
        foreach (string entry in (pointsData ?? string.Empty).Split(';'))
        {
            int separator = entry.IndexOf(':');
            if (separator > 0 && int.TryParse(entry.Substring(0, separator), out int id)
                && int.TryParse(entry.Substring(separator + 1), out int points)
                && id >= 0 && points >= 0 && points <= PointsToWin)
            {
                Points[id] = points;
            }
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || WinnerId >= 0 || killerId < 0 || killerId == deadPlayerId)
        {
            return;
        }

        Points.TryGetValue(killerId, out int currentPoints);
        int totalPoints = currentPoints + 1;
        Points[killerId] = totalPoints;
        if (totalPoints >= PointsToWin)
        {
            WinnerId = killerId;
            Announce(PlayerLookup.GetPlayerNameTag(killerId) + " reached " + PointsToWin + " points and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(killerId));
        }
        BroadcastLiveState();
    }

    internal static void ApplyHealth(PlayerHealth health)
    {
        health.fullHealth = PlayerHealth;
        if (!health.IsServer)
        {
            return;
        }

        float currentHealth = health.sync___get_value_health();
        if (currentHealth > PlayerHealth)
        {
            FishNetCompatibility.TryRemoveHealth(health, currentHealth - PlayerHealth);
        }
    }

    internal static bool IsSniperWeapon(Weapon weapon)
    {
        return weapon != null && weapon.name.StartsWith(WeaponName, StringComparison.Ordinal);
    }

    internal static void GiveStartingWeapon(int playerId)
    {
        if (Enabled && GameModeManager.IsActive(GameMode.SniperBattle))
        {
            PendingLoadouts[playerId] = Time.unscaledTime + 5f;
            WeaponService.GiveWeapon(playerId, WeaponName);
        }
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.SniperBattle) || WeaponService.IsFinalGameScreen
            || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 1f;
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner
                || client.PlayerSpawner.player == null || !client.PlayerSpawner.player)
            {
                continue;
            }

            PlayerPickup? pickup = client.PlayerSpawner.player.playerPickupScript;
            GameObject? heldObject = pickup?.objInHand;
            Weapon? heldWeapon = heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
            if (heldWeapon != null && IsSniperWeapon(heldWeapon))
            {
                PendingLoadouts.Remove(client.PlayerId);
                continue;
            }

            if (!PendingLoadouts.TryGetValue(client.PlayerId, out float retryTime) || Time.unscaledTime >= retryTime)
            {
                GiveStartingWeapon(client.PlayerId);
            }
        }
    }

    private static string SerializePoints()
    {
        StringBuilder result = new();
        foreach (KeyValuePair<int, int> entry in Points)
        {
            if (result.Length > 0) result.Append(';');
            result.Append(entry.Key).Append(':').Append(entry.Value);
        }
        return result.ToString();
    }

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            _liveStateRevision++;
            MyceliumNetwork.RPC(Plugin.SniperBattleModId, nameof(Plugin.SyncSniperBattleLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializePoints(), WinnerId, GameModeManager.RoundId, _liveStateRevision);
        }
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.SniperBattleModId, nameof(Plugin.SniperBattleAnnounce), ReliableType.Reliable, text);
        }
    }
}