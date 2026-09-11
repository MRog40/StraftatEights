using System;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HotPotatoState
{
    internal const string SettingsLobbyDataKey = "StraftatEights_HotPotato_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_HotPotato_Live";
    internal const string BatWeaponName = "BaseballBat";
    internal const string ShotgunWeaponName = "Shotgun";
    internal static bool Enabled;
    internal static int PotatoPlayerId { get; private set; } = -1;
    internal static int WinnerId { get; private set; } = -1;
    internal static int KillsToWin => GameModeManager.EffectivePointsToWin;
    internal static readonly Dictionary<int, int> Kills = new();

    private static float _nextLoadoutCheckTime;
    private static readonly ModeSyncState Sync = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();

    internal static void ApplySettings(bool enabled)
    {
        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.HotPotatoEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.HotPotatoEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.HotPotatoModId, nameof(Plugin.SyncHotPotatoSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.HotPotatoEnabled.Value);
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

        MyceliumNetwork.RPCTarget(Plugin.HotPotatoModId, nameof(Plugin.SyncHotPotatoSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.HotPotatoEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.HotPotatoModId, nameof(Plugin.SyncHotPotatoLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializeKills(), PotatoPlayerId, WinnerId,
            GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        _nextLoadoutCheckTime = 0f;
        PotatoPlayerId = -1;
        WinnerId = -1;
        Kills.Clear();
        PendingLoadouts.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, string killsData, int potatoPlayerId,
        int winnerId, int roundId, int revision, string source = "rpc")
    {
        if (potatoPlayerId < -1 || winnerId < -1)
        {
            return;
        }
        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        PotatoPlayerId = potatoPlayerId;
        WinnerId = winnerId;
        Kills.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(killsData, KillsToWin))
        {
            Kills[entry.Key] = entry.Value;
        }
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.HotPotato) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetMatchState();
        List<int> players = new();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                players.Add(client.PlayerId);
                Kills[client.PlayerId] = 0;
            }
        }

        if (players.Count == 0)
        {
            return;
        }

        PotatoPlayerId = players[UnityEngine.Random.Range(0, players.Count)];
        PendingLoadouts.Clear();
        ClearCurrentWeapons();
        Announce(PlayerLookup.GetPlayerNameTag(PotatoPlayerId) + " has the <b>HOT POTATO</b>!");
        foreach (int playerId in players)
        {
            GiveExpectedWeapon(playerId);
        }
        BroadcastLiveState();
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.HotPotato)
            || WinnerId >= 0 || killerId < 0 || killerId == deadPlayerId)
        {
            return;
        }

        Kills.TryGetValue(killerId, out int currentKills);
        int totalKills = currentKills + ScoreRules.PointsPerKill;
        Kills[killerId] = totalKills;
        GameModeHud.ShowScorePopupForPlayer(killerId, ScoreRules.PointsPerKill);

        bool passedPotato = killerId == PotatoPlayerId;
        if (passedPotato)
        {
            PotatoPlayerId = deadPlayerId;
            PendingLoadouts.Remove(killerId);
            WeaponService.GiveWeapon(killerId, ShotgunWeaponName);
            Announce(PlayerLookup.GetPlayerNameTag(deadPlayerId) + " got the <b>HOT POTATO</b>!");
        }

        if (totalKills >= KillsToWin)
        {
            WinnerId = killerId;
            Announce(PlayerLookup.GetPlayerNameTag(killerId) + " reached " + KillsToWin
                + " points and won the round!");
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(killerId));
        }

        BroadcastLiveState();
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.HotPotato) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        PendingLoadouts[playerId] = Time.unscaledTime + 2f;
        GiveExpectedWeapon(playerId);
    }

    internal static bool IsAllowedWeapon(Weapon weapon, int playerId)
    {
        if (weapon == null)
        {
            return true;
        }

        string expectedWeapon = playerId == PotatoPlayerId ? BatWeaponName : ShotgunWeaponName;
        return weapon.name.StartsWith(expectedWeapon, StringComparison.Ordinal);
    }

    internal static string GetExpectedWeapon(int playerId)
    {
        return playerId == PotatoPlayerId ? BatWeaponName : ShotgunWeaponName;
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.HotPotato) || !MyceliumNetwork.InLobby
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
            Weapon? heldWeapon = GetWeapon(pickup?.objInHand);
            if (heldWeapon != null && IsAllowedWeapon(heldWeapon, client.PlayerId))
            {
                PendingLoadouts.Remove(client.PlayerId);
                continue;
            }

            if (!PendingLoadouts.TryGetValue(client.PlayerId, out float retryTime)
                || Time.unscaledTime >= retryTime)
            {
                GiveExpectedWeapon(client.PlayerId);
            }
        }
    }

    private static void GiveExpectedWeapon(int playerId)
    {
        PendingLoadouts[playerId] = Time.unscaledTime + 2f;
        WeaponService.GiveWeapon(playerId, GetExpectedWeapon(playerId));
    }

    private static void ClearCurrentWeapons()
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner
                || client.PlayerSpawner.player == null || !client.PlayerSpawner.player)
            {
                continue;
            }

            PlayerPickup? pickup = client.PlayerSpawner.player.playerPickupScript;
            if (pickup != null)
            {
                WeaponService.ClearHeldWeapons(pickup);
            }
        }
    }

    private static Weapon? GetWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static string SerializeKills() => ScoreCodec.Serialize(Kills);

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            int revision = Sync.NextLiveRevision();
            ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
                GameModeManager.RoundId, revision, SerializeKills(), PotatoPlayerId.ToString(),
                WinnerId.ToString());
            MyceliumNetwork.RPC(Plugin.HotPotatoModId, nameof(Plugin.SyncHotPotatoLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeKills(), PotatoPlayerId, WinnerId,
                GameModeManager.RoundId, revision);
        }
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("hot-potato", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 3, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[1], out int potatoPlayerId)
            || !int.TryParse(fields[2], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], potatoPlayerId, winnerId, roundId, revision,
            ModeLobbyDataSync.Source("hot-potato", "live"));
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.HotPotatoModId, nameof(Plugin.HotPotatoAnnounce), ReliableType.Reliable, text);
        }
    }
}