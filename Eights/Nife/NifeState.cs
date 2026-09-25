using System;
using System.Collections.Generic;
using System.Linq;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class NifeState
{
    internal const string SettingsLobbyDataKey = "Eights_Nife_Settings";
    internal const string LiveLobbyDataKey = "Eights_Nife_Live";
    internal static readonly float PlayerHealth = HealthUnits.ToInternal(10f);
    internal static readonly IReadOnlyList<string> MeleeWeaponNames = new[]
    {
        "JahvalMahmaerd",
        "CurvedKnife",
        "DF_GodSword",
        "Flamberge",
        "Katana",
        "Impetus",
        "Nizeh",
        "BigFattyBro",
        "Stylus",
        "Couperet",
        "BaseballBat"
    };

    internal static bool Enabled;
    internal static string SelectedWeapon { get; private set; } = string.Empty;
    internal static int KillsToWin => GameModeManager.EffectivePointsToWin;
    internal static int WinnerId { get; private set; } = -1;
    internal static readonly Dictionary<int, int> Kills = new();

    private static readonly Dictionary<int, int> PlayerObjectIds = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static readonly HashSet<int> ResolvedLoadouts = new();
    private static readonly ModeSyncState Sync = new(livePushInterval: 1f);
    private static TeamWeaponSequence? _weaponSequence;
    private static int _weaponSequenceIndex;

    internal static void ApplySettings(bool enabled)
    {
        if (GameModeManager.ShouldDeferModeDisable(GameMode.Nife, enabled))
        {
            return;
        }

        bool changed = Enabled != enabled;
        Enabled = enabled;
        if (changed)
        {
            ResetWeaponSequence();
            ResetMatchState();
        }
    }

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.NifeEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.NifeEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.NifeModId, nameof(Plugin.SyncNifeSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision, Plugin.NifeEnabled.Value);
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
            ResetWeaponSequence();
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

        MyceliumNetwork.RPCTarget(Plugin.NifeModId, nameof(Plugin.SyncNifeSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId,
            Sync.SettingsRevision, Plugin.NifeEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.NifeModId, nameof(Plugin.SyncNifeLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, SelectedWeapon,
            SerializeKills(), WinnerId, GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static void OnPlayerLeft(CSteamID player)
    {
        if (!MyceliumNetwork.IsHost)
        {
            return;
        }

        int playerId = PlayerLookup.FindPlayerId(player);
        RemoveLoadoutState(playerId);
        if (playerId >= 0 && Kills.Remove(playerId)
            && GameModeManager.IsActive(GameMode.Nife))
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
        SelectedWeapon = string.Empty;
        WinnerId = -1;
        Kills.Clear();
        PlayerObjectIds.Clear();
        PendingLoadouts.Clear();
        ResolvedLoadouts.Clear();
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Nife)
            || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetMatchState();
        SelectedWeapon = SelectRoundWeapon();
        BroadcastLiveState();
    }

    internal static void ApplyLiveState(CSteamID hostId, string selectedWeapon, string killsData,
        int winnerId, int roundId, int revision, string source = "rpc")
    {
        if (winnerId < -1 || (selectedWeapon.Length > 0 && !IsMeleeWeaponName(selectedWeapon))
            || !Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        SelectedWeapon = selectedWeapon;
        WinnerId = winnerId;
        Kills.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(killsData, KillsToWin))
        {
            Kills[entry.Key] = entry.Value;
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Nife)
            || WinnerId >= 0 || killerId < 0 || killerId == deadPlayerId)
        {
            return;
        }

        Kills.TryGetValue(killerId, out int currentKills);
        int totalKills = ScoreRules.AddPoints(currentKills, ScoreRules.PointsPerKill,
            KillsToWin);
        Kills[killerId] = totalKills;
        int awardedKills = totalKills - currentKills;
        if (awardedKills > 0)
        {
            GameModeHud.ShowScorePopupForPlayer(killerId, awardedKills);
        }
        if (totalKills >= KillsToWin)
        {
            WinnerId = killerId;
            GameModeHud.BroadcastAnnouncement(PlayerLookup.GetPlayerNameTag(killerId)
                + " reached " + KillsToWin + " points and won the round!");
            GameModeManager.CompleteCustomRound(TeamAssignment.ResolveTeamId(killerId));
        }

        BroadcastLiveState();
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.Nife)
            || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || SelectedWeapon.Length == 0 || WeaponService.IsFinalGameScreen)
        {
            return;
        }

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerSpawner == null
                || !client.PlayerSpawner || client.PlayerSpawner.player == null
                || !client.PlayerSpawner.player)
            {
                continue;
            }

            EnsurePlayerLoadout(client.PlayerId, client.PlayerSpawner.player);
        }
    }

    internal static void ApplyHealth(PlayerHealth health, HealthSettingsTuning.Memory memory)
    {
        float previousHealth = health.sync___get_value_health();
        int playerId = health.playerValues?.playerClient?.PlayerId ?? -1;
        bool targetChanged = HealthSettingsTuning.ApplyModeHealth(health, memory,
            PlayerHealth, playerId);
        if (!health.IsServer || !targetChanged)
        {
            return;
        }

        float healthDelta = PlayerHealth - previousHealth;
        if (Mathf.Approximately(healthDelta, 0f))
        {
            return;
        }

        HealthSettingsTuning.ApplyingPassiveHealth = true;
        try
        {
            FishNetCompatibility.TryRemoveHealth(health, -healthDelta);
        }
        finally
        {
            HealthSettingsTuning.ApplyingPassiveHealth = false;
        }
    }

    internal static bool IsSelectedWeapon(Weapon weapon)
    {
        return weapon != null && SelectedWeapon.Length > 0
            && weapon.name.StartsWith(SelectedWeapon, StringComparison.Ordinal);
    }

    internal static bool IsMeleeWeaponName(string weaponName)
    {
        return MeleeWeaponNames.Contains(weaponName, StringComparer.Ordinal);
    }

    internal static string SerializeKills()
    {
        return ScoreCodec.Serialize(Kills);
    }

    private static void EnsurePlayerLoadout(int playerId, FirstPersonController player)
    {
        int objectId = player.GetInstanceID();
        if (!PlayerObjectIds.TryGetValue(playerId, out int previousObjectId)
            || previousObjectId != objectId)
        {
            PlayerObjectIds[playerId] = objectId;
            PendingLoadouts[playerId] = Time.unscaledTime + 1f;
            ResolvedLoadouts.Remove(playerId);
        }

        if (ResolvedLoadouts.Contains(playerId))
        {
            return;
        }

        PlayerPickup? pickup = player.playerPickupScript;
        if (pickup == null || !pickup)
        {
            return;
        }

        Weapon? rightWeapon = GetHeldWeapon(pickup.objInHand);
        Weapon? leftWeapon = GetHeldWeapon(pickup.objInLeftHand);
        if (rightWeapon != null && IsSelectedWeapon(rightWeapon) && leftWeapon == null)
        {
            ResolvedLoadouts.Add(playerId);
            PendingLoadouts.Remove(playerId);
            return;
        }

        if (PendingLoadouts.TryGetValue(playerId, out float retryTime)
            && Time.unscaledTime < retryTime)
        {
            return;
        }

        PendingLoadouts[playerId] = Time.unscaledTime + 1f;
        WeaponService.GiveWeapon(playerId, SelectedWeapon);
    }

    private static string SelectRoundWeapon()
    {
        _weaponSequence ??= new TeamWeaponSequence(MeleeWeaponNames,
            UnityEngine.Random.Range(0, int.MaxValue));
        return _weaponSequence.GetAt(_weaponSequenceIndex++);
    }

    private static void ResetWeaponSequence()
    {
        _weaponSequence = null;
        _weaponSequenceIndex = 0;
    }

    private static Weapon? GetHeldWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static void RemoveLoadoutState(int playerId)
    {
        if (playerId < 0)
        {
            return;
        }

        PlayerObjectIds.Remove(playerId);
        PendingLoadouts.Remove(playerId);
        ResolvedLoadouts.Remove(playerId);
    }

    private static void BroadcastLiveState()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        int revision = Sync.NextLiveRevision();
        ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, SelectedWeapon, SerializeKills(), WinnerId.ToString());
        MyceliumNetwork.RPC(Plugin.NifeModId, nameof(Plugin.SyncNifeLiveState), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, SelectedWeapon, SerializeKills(), WinnerId,
            GameModeManager.RoundId, revision);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("nife", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 3, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[2], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, fields[0], fields[1], winnerId, roundId, revision,
            ModeLobbyDataSync.Source("nife", "live"));
    }
}