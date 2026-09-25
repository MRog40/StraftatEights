using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class PotatotatState
{
    internal const string SettingsLobbyDataKey = "Eights_Potatotat_Settings";
    internal const string LiveLobbyDataKey = "Eights_Potatotat_Live";
    internal const string PotatoWeaponName = "HandGrenade";
    internal static bool Enabled;
    internal static List<string> WeaponOrder { get; private set; } = new();
    internal static int PotatoPlayerId { get; private set; } = -1;
    internal static int WinnerId { get; private set; } = -1;
    internal static int KillsToWin => GameModeManager.EffectivePointsToWin;
    internal static readonly Dictionary<int, int> Kills = new();

    private static float _nextLoadoutCheckTime;
    private static int _weaponRotationIndex;
    private static readonly ModeSyncState Sync = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();

    internal static void ApplySettings(bool enabled, string weaponOrder)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Potatotat, enabled))
        {
            return;
        }

        List<string> nextWeaponOrder = WeaponService.ParseWeaponList(weaponOrder);
        bool changed = Enabled != enabled
            || !WeaponOrder.SequenceEqual(nextWeaponOrder, StringComparer.Ordinal);
        Enabled = enabled;
        WeaponOrder = nextWeaponOrder;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.PotatotatEnabled.Value,
        Plugin.PotatotatWeaponOrder.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        string encodedWeaponOrder = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(Plugin.PotatotatWeaponOrder.Value ?? string.Empty));
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.PotatotatEnabled.Value ? "1" : "0",
            encodedWeaponOrder);
        MyceliumNetwork.RPC(Plugin.PotatotatModId, nameof(Plugin.SyncPotatotatSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.PotatotatEnabled.Value, Plugin.PotatotatWeaponOrder.Value);
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

        MyceliumNetwork.RPCTarget(Plugin.PotatotatModId, nameof(Plugin.SyncPotatotatSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.PotatotatEnabled.Value, Plugin.PotatotatWeaponOrder.Value);
        MyceliumNetwork.RPCTarget(Plugin.PotatotatModId, nameof(Plugin.SyncPotatotatLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SerializeKills(), PotatoPlayerId, WinnerId,
            GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static void OnPlayerLeft(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        int playerId = PlayerLookup.FindPlayerId(player);
        bool changed = playerId >= 0 && Kills.Remove(playerId);
        PendingLoadouts.Remove(playerId);
        if (playerId >= 0 && PotatoPlayerId == playerId)
        {
            PotatoPlayerId = -1;
            changed = true;
        }

        if (changed && GameModeManager.IsActive(GameMode.Potatotat))
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
        Sync.ResetLiveState();
        _nextLoadoutCheckTime = 0f;
        _weaponRotationIndex = 0;
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
        if (!Enabled || !GameModeManager.IsActive(GameMode.Potatotat) || !MyceliumNetwork.IsHost)
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

        PendingLoadouts.Clear();
        ClearCurrentWeapons();
        foreach (int playerId in players)
        {
            GiveExpectedWeapon(playerId);
        }
        BroadcastLiveState();
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Potatotat)
            || WinnerId >= 0 || deadPlayerId < 0)
        {
            return;
        }

        bool validKiller = killerId >= 0 && killerId != deadPlayerId;
        int totalKills = -1;
        if (validKiller)
        {
            Kills.TryGetValue(killerId, out int currentKills);
            totalKills = ScoreRules.AddPoints(currentKills, ScoreRules.PointsPerKill,
                KillsToWin);
            Kills[killerId] = totalKills;
            int awardedKills = totalKills - currentKills;
            if (awardedKills > 0)
            {
                GameModeHud.ShowScorePopupForPlayer(killerId, awardedKills);
            }
        }

        int previousPotatoPlayerId = PotatoPlayerId;
        int nextPotatoPlayerId = PotatotatRules.ResolvePotato(
            PotatoPlayerId, validKiller ? killerId : -1, deadPlayerId);
        PotatoPlayerId = nextPotatoPlayerId;
        if (previousPotatoPlayerId < 0 && nextPotatoPlayerId >= 0)
        {
            Announce(PlayerLookup.GetPlayerNameTag(deadPlayerId) + " got the <b>Hot Potato</b>!");
        }
        else if (nextPotatoPlayerId != previousPotatoPlayerId)
        {
            RotateWeapons();
            Announce(PlayerLookup.GetPlayerNameTag(deadPlayerId) + " got the <b>Hot Potato</b>!");
        }

        if (validKiller && totalKills >= KillsToWin)
        {
            WinnerId = killerId;
            Announce(PlayerLookup.GetPlayerNameTag(killerId) + " reached " + KillsToWin
                + " points and won the round!");
            GameModeManager.CompleteCustomRound(TeamAssignment.ResolveTeamId(killerId));
        }

        BroadcastLiveState();
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Potatotat) || !MyceliumNetwork.IsHost)
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

        return PotatotatRules.IsAllowedWeapon(weapon.name, playerId == PotatoPlayerId,
            WeaponOrder);
    }

    internal static string GetExpectedWeapon(int playerId)
    {
        return playerId == PotatoPlayerId ? PotatoWeaponName : GetCurrentWeapon();
    }

    internal static bool IsPotatotatWeapon(Weapon weapon)
    {
        return weapon != null && WeaponOrder.Any(weaponName =>
            weapon.name.StartsWith(weaponName, StringComparison.Ordinal));
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Potatotat) || !MyceliumNetwork.InLobby
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
            Weapon? rightHandWeapon = GetWeapon(pickup?.objInHand);
            Weapon? leftHandWeapon = GetWeapon(pickup?.objInLeftHand);
            if (IsExpectedWeapon(rightHandWeapon, client.PlayerId)
                || IsExpectedWeapon(leftHandWeapon, client.PlayerId))
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
        string expectedWeapon = GetExpectedWeapon(playerId);
        if (expectedWeapon.Length == 0)
        {
            return;
        }

        PendingLoadouts[playerId] = Time.unscaledTime + 2f;
        WeaponService.GiveWeapon(playerId, expectedWeapon,
            unlimitedAmmo: playerId != PotatoPlayerId);
    }

    private static void RotateWeapons()
    {
        if (WeaponOrder.Count > 0)
        {
            _weaponRotationIndex = (_weaponRotationIndex + 1) % WeaponOrder.Count;
        }

        ClearCurrentWeapons();
        PendingLoadouts.Clear();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                GiveExpectedWeapon(client.PlayerId);
            }
        }
    }

    private static string GetCurrentWeapon()
    {
        return WeaponOrder.Count == 0 ? string.Empty : WeaponOrder[_weaponRotationIndex];
    }

    private static bool IsExpectedWeapon(Weapon? weapon, int playerId)
    {
        string expectedWeapon = GetExpectedWeapon(playerId);
        return weapon != null && expectedWeapon.Length > 0
            && weapon.name.StartsWith(expectedWeapon, StringComparison.Ordinal);
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
            MyceliumNetwork.RPC(Plugin.PotatotatModId, nameof(Plugin.SyncPotatotatLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, SerializeKills(), PotatoPlayerId, WinnerId,
                GameModeManager.RoundId, revision);
        }
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 2, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled))
        {
            return;
        }

        try
        {
            string weaponOrder = Encoding.UTF8.GetString(Convert.FromBase64String(fields[1]));
            if (Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("potatotat", "settings")))
            {
                ApplySettings(enabled, weaponOrder);
            }
        }
        catch (FormatException)
        {
            // Steam lobby data can contain an incomplete update while the value is changing.
        }
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
            ModeLobbyDataSync.Source("potatotat", "live"));
    }

    private static void Announce(string text)
    {
        GameModeHud.BroadcastAnnouncement(text);
    }
}