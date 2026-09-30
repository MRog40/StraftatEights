using System;
using System.Collections;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class PotatotatState
{
    internal const string SettingsLobbyDataKey = "Eights_Potatotat_Settings";
    internal const string LiveLobbyDataKey = "Eights_Potatotat_Live";
    internal const string PotatoWeaponName = "HandGrenade";
    private const float LoadoutCheckIntervalSeconds = 0.2f;
    private const float GrenadeReplacementDelaySeconds = 0.1f;
    private const float GrenadeMultikillWindowSeconds = 1f;
    internal static bool Enabled;
    internal static IReadOnlyList<string> WeaponOrder => PotatotatRules.WeaponOrder;
    internal static int PotatoPlayerId { get; private set; } = -1;
    internal static int WinnerId { get; private set; } = -1;
    internal static int KillsToWin => PotatotatRules.PointsToWin;
    internal static readonly Dictionary<int, int> Kills = new();

    private static float _nextLoadoutCheckTime;
    private static readonly ModeSyncState Sync = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static readonly Dictionary<int, int> PendingGrenadeGrantVersions = new();
    private static readonly HashSet<int> PendingGrenadeDeaths = new();
    private static int _grenadeExplosionDepth;
    private static int _recentGrenadeSourcePlayerId = -1;
    private static float _recentGrenadeSourceExpiresAt;
    private static int _nextGrenadeGrantVersion;

    internal static void ApplySettings(bool enabled)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Potatotat, enabled))
        {
            return;
        }

        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.PotatotatEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.PotatotatEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.PotatotatModId, nameof(Plugin.SyncPotatotatSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision, Plugin.PotatotatEnabled.Value);
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
            Plugin.PotatotatEnabled.Value);
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
        PendingGrenadeGrantVersions.Remove(playerId);
        if (playerId >= 0 && PotatoPlayerId == playerId)
        {
            PotatoPlayerId = -1;
            changed = true;
        }
        if (playerId >= 0)
        {
            PendingGrenadeDeaths.Remove(playerId);
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
        PotatoPlayerId = -1;
        WinnerId = -1;
        Kills.Clear();
        PendingLoadouts.Clear();
        PendingGrenadeGrantVersions.Clear();
        PendingGrenadeDeaths.Clear();
        _grenadeExplosionDepth = 0;
        _recentGrenadeSourcePlayerId = -1;
        _recentGrenadeSourceExpiresAt = 0f;
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

        if (_recentGrenadeSourcePlayerId >= 0
            && Time.unscaledTime > _recentGrenadeSourceExpiresAt)
        {
            _recentGrenadeSourcePlayerId = -1;
        }
        if (killerId >= 0 && killerId == PotatoPlayerId)
        {
            _recentGrenadeSourcePlayerId = killerId;
            _recentGrenadeSourceExpiresAt = Time.unscaledTime + GrenadeMultikillWindowSeconds;
        }

        bool grenadeDeathMarked = PendingGrenadeDeaths.Remove(deadPlayerId);
        if (PotatotatRules.IsGrenadeDeath(killerId, PotatoPlayerId,
            _recentGrenadeSourcePlayerId, grenadeDeathMarked))
        {
            int lostKills = PotatotatRules.ResetKillStreakOnGrenadeDeath(Kills, deadPlayerId);
            if (lostKills > 0)
            {
                Announce(PlayerLookup.GetPlayerNameTag(deadPlayerId)
                    + "'s score was reset by the grenade!");
            }
        }

        bool validKiller = killerId >= 0 && killerId != deadPlayerId;
        int totalKills = -1;
        if (validKiller)
        {
            Kills.TryGetValue(killerId, out int currentKills);
            totalKills = PotatotatRules.AddKillPoints(currentKills);
            Kills[killerId] = totalKills;
            int awardedPoints = totalKills - currentKills;
            if (awardedPoints > 0)
            {
                GameModeHud.ShowScorePopupForPlayer(killerId, awardedPoints);
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
            Announce(PlayerLookup.GetPlayerNameTag(deadPlayerId) + " got the <b>Hot Potato</b>!");
        }

        if (validKiller && totalKills > 0)
        {
            GiveExpectedWeapon(killerId);
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

    internal static void BeginGrenadeExplosion()
    {
        _grenadeExplosionDepth++;
    }

    internal static void EndGrenadeExplosion()
    {
        if (_grenadeExplosionDepth > 0)
        {
            _grenadeExplosionDepth--;
        }
    }

    internal static void MarkGrenadeDeath(PlayerHealth playerHealth)
    {
        if (_grenadeExplosionDepth == 0 || !MyceliumNetwork.IsHost || !Enabled
            || !GameModeManager.IsActive(GameMode.Potatotat)
            || playerHealth == null || !playerHealth)
        {
            return;
        }

        int playerId = playerHealth.playerValues?.playerClient?.PlayerId ?? -1;
        if (playerId >= 0)
        {
            PendingGrenadeDeaths.Add(playerId);
        }
    }

    internal static void OnServerGrenadeThrown(DualLauncher launcher)
    {
        if (!Enabled || !MyceliumNetwork.IsHost
            || !GameModeManager.IsActive(GameMode.Potatotat)
            || launcher == null || !launcher)
        {
            return;
        }

        FirstPersonController? player = launcher.playerController;
        PlayerPickup? pickup = player != null && player ? player.playerPickupScript : null;
        Weapon? weapon = launcher.GetComponent<Weapon>();
        int playerId = pickup != null && pickup ? PlayerLookup.FindPlayerId(pickup) : -1;
        if (pickup == null || !pickup || playerId != PotatoPlayerId || weapon == null || !weapon
            || !weapon.name.StartsWith(PotatoWeaponName, StringComparison.Ordinal))
        {
            return;
        }

        if (pickup.objInHand == weapon.gameObject)
        {
            WeaponService.RemoveHeldWeapon(pickup, true, weapon);
        }

        GiveExpectedWeapon(playerId);
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Potatotat)
            || GameModeManager.Phase != GameModePhase.ActiveRound || !MyceliumNetwork.IsHost)
        {
            return;
        }

        GiveExpectedWeapon(playerId);
    }

    internal static bool IsAllowedWeapon(Weapon weapon, int playerId)
    {
        if (weapon == null)
        {
            return true;
        }

        Kills.TryGetValue(playerId, out int score);
        return PotatotatRules.IsAllowedWeapon(weapon.name,
            playerId == PotatoPlayerId, score);
    }

    internal static string GetExpectedWeapon(int playerId)
    {
        if (playerId == PotatoPlayerId)
        {
            return PotatoWeaponName;
        }

        Kills.TryGetValue(playerId, out int score);
        return PotatotatRules.GetWeaponForScore(score);
    }

    internal static bool IsPotatotatWeapon(Weapon weapon)
    {
        if (weapon == null)
        {
            return false;
        }

        foreach (string weaponName in WeaponOrder)
        {
            if (weapon.name.StartsWith(weaponName, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Potatotat) || !MyceliumNetwork.InLobby
            || !MyceliumNetwork.IsHost || GameModeManager.Phase != GameModePhase.ActiveRound
            || WeaponService.IsFinalGameScreen
            || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + LoadoutCheckIntervalSeconds;
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner
                || client.PlayerSpawner.player == null || !client.PlayerSpawner.player
                || !client.PlayerSpawner.player.gameObject.activeInHierarchy)
            {
                continue;
            }

            PlayerPickup? pickup = client.PlayerSpawner.player.playerPickupScript;
            if (HasExpectedWeaponHeld(client.PlayerId))
            {
                PendingLoadouts.Remove(client.PlayerId);
                PendingGrenadeGrantVersions.Remove(client.PlayerId);
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

        if (HasExpectedWeaponHeld(playerId))
        {
            PendingLoadouts.Remove(playerId);
            PendingGrenadeGrantVersions.Remove(playerId);
            return;
        }

        if (expectedWeapon == PotatoWeaponName)
        {
            if (PendingGrenadeGrantVersions.ContainsKey(playerId)
                && PendingLoadouts.TryGetValue(playerId, out float retryTime)
                && Time.unscaledTime < retryTime)
            {
                return;
            }

            int grantVersion = unchecked(++_nextGrenadeGrantVersion);
            PendingGrenadeGrantVersions[playerId] = grantVersion;
            PendingLoadouts[playerId] = Time.unscaledTime + 2f;
            if (Plugin.Instance == null)
            {
                PendingGrenadeGrantVersions.Remove(playerId);
                PendingLoadouts.Remove(playerId);
                return;
            }

            Plugin.Instance.StartCoroutine(GiveGrenadeAfterDelay(playerId, grantVersion,
                SessionState.Generation, GameModeManager.RoundId));
            return;
        }

        PendingGrenadeGrantVersions.Remove(playerId);
        PendingLoadouts[playerId] = Time.unscaledTime + 2f;
        WeaponService.GiveWeapon(playerId, expectedWeapon,
            unlimitedAmmo: playerId != PotatoPlayerId, requiredMode: GameMode.Potatotat);
    }

    private static IEnumerator GiveGrenadeAfterDelay(int playerId, int grantVersion,
        int sessionGeneration, int roundId)
    {
        yield return new WaitForSecondsRealtime(GrenadeReplacementDelaySeconds);
        if (!PendingGrenadeGrantVersions.TryGetValue(playerId, out int currentVersion)
            || currentVersion != grantVersion)
        {
            yield break;
        }

        if (!Enabled || !GameModeManager.IsActive(GameMode.Potatotat) || !MyceliumNetwork.IsHost
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || !SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId)
        {
            PendingGrenadeGrantVersions.Remove(playerId);
            PendingLoadouts.Remove(playerId);
            yield break;
        }

        if (GetExpectedWeapon(playerId) != PotatoWeaponName)
        {
            PendingGrenadeGrantVersions.Remove(playerId);
            PendingLoadouts.Remove(playerId);
            GiveExpectedWeapon(playerId);
            yield break;
        }

        if (HasExpectedWeaponHeld(playerId))
        {
            PendingGrenadeGrantVersions.Remove(playerId);
            PendingLoadouts.Remove(playerId);
            yield break;
        }

        WeaponService.GiveWeapon(playerId, PotatoWeaponName, spareMagazines: 0,
            requiredMode: GameMode.Potatotat);
    }

    private static bool HasExpectedWeaponHeld(int playerId)
    {
        PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(playerId);
        FirstPersonController? player = health != null && health
            ? health.GetComponent<FirstPersonController>()
            : null;
        PlayerPickup? pickup = player != null && player ? player.playerPickupScript : null;
        string expectedWeapon = GetExpectedWeapon(playerId);
        bool expectedRightHandWeapon = pickup != null && pickup.hasObjectInHand
            && IsExpectedWeapon(GetWeapon(pickup.objInHand), playerId);
        if (expectedWeapon == PotatoWeaponName)
        {
            GameObject? leftHandObject = pickup?.objInLeftHand;
            return expectedRightHandWeapon && pickup != null && !pickup.hasObjectInLeftHand
                && (leftHandObject == null || !leftHandObject);
        }

        return PotatotatRules.HasExpectedWeapon(expectedRightHandWeapon,
            IsExpectedWeapon(GetWeapon(pickup?.objInLeftHand), playerId));
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
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled))
        {
            return;
        }

        if (Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ModeLobbyDataSync.Source("potatotat", "settings")))
        {
            ApplySettings(enabled);
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