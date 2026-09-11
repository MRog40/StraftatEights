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
    internal const string SettingsLobbyDataKey = "StraftatEights_SniperBattle_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_SniperBattle_Live";
    internal static bool Enabled;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Points = new();

    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static float _nextLoadoutCheckTime;
    private static readonly ModeSyncState Sync = new();

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplyFromConfig() => ApplySettings(Plugin.SniperBattleEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }
        ApplyFromConfig();
        int revision = Sync.NextSettingsRevision();
        PublishSettingsSnapshot(revision);
        MyceliumNetwork.RPC(Plugin.SniperBattleModId, nameof(Plugin.SyncSniperBattleSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision, Plugin.SniperBattleEnabled.Value);
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
        MyceliumNetwork.RPCTarget(Plugin.SniperBattleModId, nameof(Plugin.SyncSniperBattleSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.SniperBattleEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.SniperBattleModId, nameof(Plugin.SyncSniperBattleLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializePoints(), WinnerId,
            GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision,
        string source = "unknown")
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision, source);
    }

    internal static void ResetMatchState()
    {
        DebugLog.Info($"SniperBattle match reset round={GameModeManager.RoundId} pointsCount={Points.Count} "
            + $"liveRevision={Sync.LiveRevision} lastLiveRound={Sync.LastLiveRoundId}");
        Sync.ResetLiveState();
        WinnerId = -1;
        Points.Clear();
        PendingLoadouts.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string pointsData, int winnerId, int roundId,
        int revision, string source = "unknown")
    {
        DebugLog.Info($"SniperBattle live state received source={source} host={hostId.m_SteamID} "
            + $"round={roundId} revision={revision} winner={winnerId} payloadLength={pointsData?.Length ?? 0}");
        if (winnerId < -1)
        {
            return;
        }
        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, "sniper-battle-" + source))
        {
            return;
        }
        WinnerId = winnerId;
        Points.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(pointsData ?? string.Empty, PointsToWin))
        {
            Points[entry.Key] = entry.Value;
        }
        Plugin.Logger.LogInfo($"[SniperBattle] Accepted live state via {source}: round={roundId} "
            + $"revision={revision} players={Points.Count}");
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        DebugLog.Info($"SniperBattle server kill dead={deadPlayerId} killer={killerId} enabled={Enabled} "
            + $"round={GameModeManager.RoundId} pointsCount={Points.Count}");
        if (!Enabled || WinnerId >= 0 || killerId < 0 || killerId == deadPlayerId)
        {
            return;
        }

        Points.TryGetValue(killerId, out int currentPoints);
        int totalPoints = currentPoints + ScoreRules.PointsPerKill;
        Points[killerId] = totalPoints;
        GameModeHud.ShowScorePopupForPlayer(killerId, ScoreRules.PointsPerKill);
        if (totalPoints >= PointsToWin)
        {
            WinnerId = killerId;
            Announce(PlayerLookup.GetPlayerNameTag(killerId) + " reached " + PointsToWin + " points and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(killerId));
        }
        DebugLog.Info($"SniperBattle score updated killer={killerId} points={totalPoints} "
            + $"round={GameModeManager.RoundId} winner={WinnerId}");
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
        return ScoreCodec.Serialize(Points);
    }

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            int revision = Sync.NextLiveRevision();
            string pointsData = SerializePoints();
            DebugLog.Info($"SniperBattle live broadcast host={MyceliumNetwork.LobbyHost.m_SteamID} "
                + $"round={GameModeManager.RoundId} revision={revision} winner={WinnerId} "
                + $"players={Points.Count} payloadLength={pointsData.Length}");
            PublishLiveSnapshot(revision, pointsData);
            MyceliumNetwork.RPC(Plugin.SniperBattleModId, nameof(Plugin.SyncSniperBattleLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, pointsData, WinnerId, GameModeManager.RoundId, revision);
        }
    }

    private static void PublishSettingsSnapshot(int revision)
    {
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.SniperBattleEnabled.Value ? "1" : "0");
    }

    private static void PublishLiveSnapshot(int revision, string pointsData)
    {
        ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, WinnerId.ToString(), pointsData ?? string.Empty);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled))
        {
            return;
        }

        if (Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision, "sniper-battle-lobby-data"))
        {
            DebugLog.Info($"SniperBattle settings accepted via lobby data round={roundId} "
                + $"revision={revision} enabled={enabled}");
            ApplySettings(enabled);
        }
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 2, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[0], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, fields[1], winnerId, roundId, revision,
            ModeLobbyDataSync.Source("sniper-battle", "live"));
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.SniperBattleModId, nameof(Plugin.SniperBattleAnnounce), ReliableType.Reliable, text);
        }
    }
}