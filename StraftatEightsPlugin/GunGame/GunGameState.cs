using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class GunGameState
{
    internal static bool Enabled;
    internal static readonly Dictionary<int, int> Progress = new();
    internal static List<string> WeaponOrder { get; private set; } = new();
    internal static int ScoreLimit => GameModeManager.EffectivePointsToWin;
    private static float _nextLoadoutCheckTime;
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static readonly ModeSyncState Sync = new();

    internal static void ApplySettings(bool enabled, string weaponOrder)
    {
        List<string> nextWeaponOrder = WeaponService.ParseWeaponList(weaponOrder);
        bool changed = Enabled != enabled || !WeaponOrder.SequenceEqual(nextWeaponOrder, StringComparer.Ordinal);
        Enabled = enabled;
        WeaponOrder = nextWeaponOrder;
        if (changed) ResetMatchState();
    }

    private static void ApplyFromConfig() => ApplySettings(Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost) return;
        ApplyFromConfig();
        MyceliumNetwork.RPC(Plugin.GunGameModId, nameof(Plugin.SyncGunGameSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.NextSettingsRevision(),
            Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
    }
    internal static void PeriodicPushSettingsIfHost() { if (Sync.IsSettingsPushDue()) PushSettingsIfHost(); }
       internal static void PeriodicPushIfHost()
       {
           PeriodicPushSettingsIfHost();
           if (Sync.IsLivePushDue()) BroadcastLiveState();
       }
    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        if (MyceliumNetwork.IsHost) { ApplyFromConfig(); ResetMatchState(); }
    }
    internal static void OnPlayerEntered(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost) return;
        MyceliumNetwork.RPCTarget(Plugin.GunGameModId, nameof(Plugin.SyncGunGameSettings), player, ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
        MyceliumNetwork.RPCTarget(Plugin.GunGameModId, nameof(Plugin.SyncGunGameLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializeProgress(), GameModeManager.RoundId,
            Sync.LiveRevision);
    }
    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }
    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        _nextLoadoutCheckTime = 0f;
        PendingLoadouts.Clear();
        Progress.Clear();
    }
    internal static void ApplyLiveState(CSteamID hostId, string data, int roundId, int revision)
    {
        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision))
        {
            return;
        }
        Progress.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(data, ScoreLimit))
        {
            Progress[entry.Key] = entry.Value;
        }
    }
    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || killerId < 0 || killerId == deadPlayerId) return;
        Progress.TryGetValue(killerId, out int current);
        int next = current + ScoreRules.PointsPerKill;
        Progress[killerId] = next;
        GameModeHud.ShowScorePopupForPlayer(killerId, ScoreRules.PointsPerKill);
        if (ScoreLimit > 0 && next >= ScoreLimit) GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(killerId));
        else if (WeaponOrder.Count > 0) GiveWeaponForProgress(killerId, next);
        BroadcastLiveState();
    }

    private static int GetWeaponIndex(int progress)
    {
        if (WeaponOrder.Count <= 1 || ScoreLimit <= 1)
        {
            return 0;
        }

        long scaledIndex = (long)progress * (WeaponOrder.Count - 1) / (ScoreLimit - 1);
        return (int)System.Math.Min(scaledIndex, WeaponOrder.Count - 1);
    }
    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.GunGame) || !MyceliumNetwork.InLobby
            || !MyceliumNetwork.IsHost || WeaponService.IsFinalGameScreen
            || Time.unscaledTime < _nextLoadoutCheckTime)
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

            int progress = Progress.TryGetValue(client.PlayerId, out int currentProgress) ? currentProgress : 0;
            string? expectedWeapon = GetWeaponForProgress(progress);
            if (expectedWeapon == null)
            {
                continue;
            }

            PlayerPickup? pickup = client.PlayerSpawner.player.playerPickupScript;
            GameObject? heldObject = pickup?.objInHand;
            Weapon? heldWeapon = heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
            if (heldWeapon != null && heldWeapon.name.StartsWith(expectedWeapon, StringComparison.Ordinal))
            {
                PendingLoadouts.Remove(client.PlayerId);
                continue;
            }

            if (!PendingLoadouts.TryGetValue(client.PlayerId, out float retryTime)
                || Time.unscaledTime >= retryTime)
            {
                GiveStartingWeapon(client.PlayerId);
            }
        }
    }

    internal static void GiveStartingWeapon(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.GunGame) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        int progress = Progress.TryGetValue(playerId, out int currentProgress) ? currentProgress : 0;
        GiveWeaponForProgress(playerId, progress);
    }

    private static void GiveWeaponForProgress(int playerId, int progress)
    {
        string? weaponName = GetWeaponForProgress(progress);
        if (weaponName == null)
        {
            return;
        }

        PendingLoadouts[playerId] = Time.unscaledTime + 5f;
        WeaponService.GiveWeapon(playerId, weaponName, unlimitedAmmo: true);
    }

    private static string? GetWeaponForProgress(int progress)
    {
        return WeaponOrder.Count == 0 ? null : WeaponOrder[GetWeaponIndex(progress)];
    }
    internal static string SerializeProgress()
    {
        return ScoreCodec.Serialize(Progress);
    }
    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.GunGameModId, nameof(Plugin.SyncGunGameLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeProgress(), GameModeManager.RoundId,
                Sync.NextLiveRevision());
        }
    }
}