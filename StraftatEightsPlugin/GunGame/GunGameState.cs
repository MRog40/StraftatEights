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
    internal const string SettingsLobbyDataKey = "StraftatEights_GunGame_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_GunGame_Live";
    private static float _nextLoadoutCheckTime;
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static readonly ModeSyncState Sync = new();

    internal static void ApplySettings(bool enabled, string weaponOrder)
    {
        List<string> nextWeaponOrder = WeaponService.ParseWeaponList(weaponOrder);
        bool changed = Enabled != enabled || !WeaponOrder.SequenceEqual(nextWeaponOrder, StringComparer.Ordinal);
        DebugLog.Info($"GunGame settings applied enabled={enabled} weaponCount={nextWeaponOrder.Count} changed={changed} "
            + $"previousEnabled={Enabled} previousWeaponCount={WeaponOrder.Count}");
        Enabled = enabled;
        WeaponOrder = nextWeaponOrder;
        if (changed) ResetMatchState();
    }

    private static void ApplyFromConfig() => ApplySettings(Plugin.GunGameEnabled.Value, Plugin.GunGameWeaponOrder.Value);
    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost) return;
        ApplyFromConfig();
        int revision = Sync.NextSettingsRevision();
        PublishSettingsSnapshot(revision);
        MyceliumNetwork.RPC(Plugin.GunGameModId, nameof(Plugin.SyncGunGameSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
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
        DebugLog.Info($"GunGame lobby event host={MyceliumNetwork.IsHost} lobby={MyceliumNetwork.InLobby} "
            + $"round={GameModeManager.RoundId} settingsRevision={Sync.SettingsRevision} liveRevision={Sync.LiveRevision}");
        Sync.ResetForLobby();
        if (MyceliumNetwork.IsHost)
        {
            ApplyFromConfig();
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
    internal static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby)
        {
            return;
        }

        if (keys.Contains(SettingsLobbyDataKey))
        {
            ApplyLobbySettingsSnapshot();
        }
        if (keys.Contains(LiveLobbyDataKey))
        {
            ApplyLobbyLiveSnapshot();
        }
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
    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision,
        string source = "unknown")
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision, source);
    }
    internal static void ResetMatchState()
    {
        DebugLog.Info($"GunGame match reset round={GameModeManager.RoundId} progressCount={Progress.Count} "
            + $"liveRevision={Sync.LiveRevision} lastLiveRound={Sync.LastLiveRoundId}");
        Sync.ResetLiveState();
        _nextLoadoutCheckTime = 0f;
        PendingLoadouts.Clear();
        Progress.Clear();
    }
    internal static void ApplyLiveState(CSteamID hostId, string data, int roundId, int revision,
        string source = "unknown")
    {
        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }
        Progress.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(data, ScoreLimit))
        {
            Progress[entry.Key] = entry.Value;
        }
        Plugin.Logger.LogInfo($"[GunGame] Accepted live state via {source}: round={roundId} "
            + $"revision={revision} players={Progress.Count}");
    }
    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        DebugLog.Info($"GunGame server kill dead={deadPlayerId} killer={killerId} enabled={Enabled} "
            + $"round={GameModeManager.RoundId} progressCount={Progress.Count}");
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
                WeaponAmmoTuning.ApplyUnlimitedToWeapon(heldWeapon);
                bool ammoReady = !heldWeapon.needsAmmo
                    || heldWeapon.currentAmmo > 0
                    || WeaponAmmoTuning.IsReloading(heldWeapon)
                    || (heldWeapon.reloadWeapon && heldWeapon.chargedBullets > 0);
                if (ammoReady)
                {
                    PendingLoadouts.Remove(client.PlayerId);
                    continue;
                }
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
            int revision = Sync.NextLiveRevision();
            string progressData = SerializeProgress();
            DebugLog.Info($"GunGame live broadcast host={MyceliumNetwork.LobbyHost.m_SteamID} "
                + $"round={GameModeManager.RoundId} revision={revision} players={Progress.Count} "
                + $"payloadLength={progressData.Length}");
            PublishLiveSnapshot(revision, progressData);
            MyceliumNetwork.RPC(Plugin.GunGameModId, nameof(Plugin.SyncGunGameLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, progressData, GameModeManager.RoundId, revision);
        }
    }

    private static void PublishSettingsSnapshot(int revision)
    {
        string encodedOrder = Convert.ToBase64String(Encoding.UTF8.GetBytes(Plugin.GunGameWeaponOrder.Value ?? string.Empty));
        string payload = string.Join("|", MyceliumNetwork.LobbyHost.m_SteamID,
            GameModeManager.RoundId, revision, Plugin.GunGameEnabled.Value ? "1" : "0", encodedOrder);
        MyceliumNetwork.SetLobbyData(SettingsLobbyDataKey, payload);
    }

    private static void PublishLiveSnapshot(int revision, string progressData)
    {
        string payload = string.Join("|", MyceliumNetwork.LobbyHost.m_SteamID,
            GameModeManager.RoundId, revision, progressData ?? string.Empty);
        MyceliumNetwork.SetLobbyData(LiveLobbyDataKey, payload);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        string payload = MyceliumNetwork.GetLobbyData<string>(SettingsLobbyDataKey) ?? string.Empty;
        string[] parts = payload.Split('|');
        if (parts.Length != 5 || !ulong.TryParse(parts[0], out ulong hostSteamId)
            || !int.TryParse(parts[1], out int roundId) || !int.TryParse(parts[2], out int revision)
            || (parts[3] != "0" && parts[3] != "1"))
        {
            return;
        }

        try
        {
            string weaponOrder = Encoding.UTF8.GetString(Convert.FromBase64String(parts[4]));
            if (Sync.TryAcceptSettingsSnapshot(new CSteamID(hostSteamId), roundId, revision,
                "gun-game-lobby-data"))
            {
                ApplySettings(parts[3] == "1", weaponOrder);
                Plugin.Logger.LogInfo($"[GunGame] Accepted settings via lobby data: round={roundId} "
                    + $"revision={revision}");
            }
        }
        catch (FormatException)
        {
            // Steam lobby data can contain an incomplete update while the value is changing.
        }
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        string payload = MyceliumNetwork.GetLobbyData<string>(LiveLobbyDataKey) ?? string.Empty;
        string[] parts = payload.Split(new[] { '|' }, 4);
        if (parts.Length != 4 || !ulong.TryParse(parts[0], out ulong hostSteamId)
            || !int.TryParse(parts[1], out int roundId) || !int.TryParse(parts[2], out int revision))
        {
            return;
        }

        ApplyLiveState(new CSteamID(hostSteamId), parts[3], roundId, revision, "lobby-data");
    }
}