using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace Eights;

internal static class WeaponSettingsState
{
    internal const string SettingsLobbyDataKey = "Eights_GlobalWeapons_Settings";
    internal static bool Enabled;
    internal static bool Cycle;
    internal static bool DefaultKnife;
    internal static int SpareMagazines = 5;
    internal static List<string> Allowed = new();
    private static readonly Dictionary<int, string> SelectedWeapons = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();
    private static readonly Dictionary<int, int> DefaultKnifeObjects = new();
    private static readonly Dictionary<int, float> DefaultKnifeNextAttemptTimes = new();
    private static readonly Dictionary<int, int> DefaultKnifeResolvedObjects = new();
    private static readonly NetworkCommandTracker CycleRequests = new();
    private static float _nextLoadoutCheckTime;
    private static int _nextCycleRequestId;
    private static readonly ModeSyncState Sync = new();
    private static float _nextClientSettingsPollTime;
    internal static void Apply(bool enabled, string allowedWeapons, int spareMagazines,
        bool cycleWeapons, bool defaultKnife)
    {
        spareMagazines = Mathf.Clamp(spareMagazines, 2, 10);
        allowedWeapons ??= string.Empty;
        List<string> nextAllowed = WeaponService.ParseWeaponList(allowedWeapons);
        bool settingsChanged = Enabled != enabled || Cycle != cycleWeapons
            || SpareMagazines != spareMagazines || DefaultKnife != defaultKnife;
        bool allowedChanged = Allowed.Count != nextAllowed.Count
            || !Allowed.SequenceEqual(nextAllowed, StringComparer.Ordinal);

        Enabled = enabled;
        Cycle = cycleWeapons;
        DefaultKnife = defaultKnife;
        SpareMagazines = spareMagazines;
        Allowed = nextAllowed;

        if (settingsChanged || allowedChanged)
        {
            WeaponService.ResetPendingRequests();
            PendingLoadouts.Clear();
            DefaultKnifeObjects.Clear();
            DefaultKnifeNextAttemptTimes.Clear();
            DefaultKnifeResolvedObjects.Clear();
            _nextLoadoutCheckTime = 0f;
        }
        if (allowedChanged)
        {
            SelectedWeapons.Clear();
        }
    }
    private static void ApplyFromConfig() => Apply(Plugin.WeaponTweaksEnabled.Value,
        Plugin.AllowedWeapons.Value, Plugin.SpareMagazines.Value, Plugin.CycleWeapons.Value,
        Plugin.DefaultKnife.Value);
    internal static void PushIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost) return;
        ApplyFromConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision,
            Plugin.WeaponTweaksEnabled.Value ? "1" : "0", Plugin.AllowedWeapons.Value,
            Plugin.SpareMagazines.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.CycleWeapons.Value ? "1" : "0", Plugin.DefaultKnife.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.GlobalWeaponsModId, nameof(Plugin.SyncWeaponSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.WeaponTweaksEnabled.Value, Plugin.AllowedWeapons.Value, Plugin.SpareMagazines.Value,
            Plugin.CycleWeapons.Value, Plugin.DefaultKnife.Value);
    }
    internal static void PeriodicPushIfHost() { if (Sync.IsSettingsPushDue()) PushIfHost(); }
    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        SelectedWeapons.Clear();
        PendingLoadouts.Clear();
        DefaultKnifeObjects.Clear();
        DefaultKnifeNextAttemptTimes.Clear();
        DefaultKnifeResolvedObjects.Clear();
        CycleRequests.Clear();
        _nextCycleRequestId = 0;
        _nextClientSettingsPollTime = 0f;
        if (MyceliumNetwork.IsHost)
        {
            PushIfHost();
        }
        else
        {
            ApplyLobbySettingsSnapshot();
        }
    }

    internal static void ResetForLobbyLeft()
    {
        SelectedWeapons.Clear();
        PendingLoadouts.Clear();
        DefaultKnifeObjects.Clear();
        DefaultKnifeNextAttemptTimes.Clear();
        CycleRequests.Clear();
        _nextCycleRequestId = 0;
        Sync.ResetForLobby();
        Apply(false, string.Empty, 5, false, false);
    }
    internal static void OnPlayerEntered(CSteamID player)
    {
        CycleRequests.Remove(player);
        if (MyceliumNetwork.IsHost) MyceliumNetwork.RPCTarget(Plugin.GlobalWeaponsModId, nameof(Plugin.SyncWeaponSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.WeaponTweaksEnabled.Value, Plugin.AllowedWeapons.Value, Plugin.SpareMagazines.Value,
            Plugin.CycleWeapons.Value, Plugin.DefaultKnife.Value);
    }

    internal static void PollSettingsIfClient()
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || Time.unscaledTime < _nextClientSettingsPollTime)
        {
            return;
        }

        _nextClientSettingsPollTime = Time.unscaledTime
            + HostSettingsSync.SettingsHeartbeatIntervalSeconds;
        ApplyLobbySettingsSnapshot();
    }

    internal static void OnLobbyDataUpdated(List<string> keys)
    {
        if (MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || !ModeLobbyDataSync.ContainsKey(keys, SettingsLobbyDataKey))
        {
            return;
        }

        ApplyLobbySettingsSnapshot();
    }

    internal static void OnPlayerLeft(CSteamID player)
    {
        CycleRequests.Remove(player);
        int playerId = PlayerLookup.FindPlayerId(player);
        if (playerId >= 0)
        {
            SelectedWeapons.Remove(playerId);
            PendingLoadouts.Remove(playerId);
            DefaultKnifeObjects.Remove(playerId);
            DefaultKnifeNextAttemptTimes.Remove(playerId);
            DefaultKnifeResolvedObjects.Remove(playerId);
        }
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision,
        string source = "rpc")
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision, source);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 5, out CSteamID hostId,
                out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !int.TryParse(fields[2], out int spareMagazines)
            || !LobbySnapshotCodec.TryParseBool(fields[3], out bool cycleWeapons)
            || !LobbySnapshotCodec.TryParseBool(fields[4], out bool defaultKnife)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("global-weapons", "settings")))
        {
            return;
        }

        Apply(enabled, fields[1], spareMagazines, cycleWeapons, defaultKnife);
    }

    internal static bool TryAcceptCycleRequest(CSteamID sender, int requestId)
    {
        return CycleRequests.TryAccept(sender, requestId);
    }

    internal static void UpdateLocalCycle()
    {
        if (GameModeManager.IsVanillaScene || GameModeManager.ShouldIgnoreGlobalWeaponSettings
            || !Enabled || !Cycle || !Input.GetKeyDown(KeyCode.F8) || Allowed.Count == 0
            || ClientInstance.Instance == null
            || JuggernautState.IsCurrentJuggernaut(ClientInstance.Instance.PlayerSpawner?.player)
            || GameModeManager.IsActive(GameMode.Infected))
        {
            return;
        }
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            GiveCycledWeapon(ClientInstance.Instance.PlayerId);
        }
        else if (MyceliumNetwork.InLobby)
        {
            MyceliumNetwork.RPC(Plugin.GlobalWeaponsModId, nameof(Plugin.RequestWeaponCycle), ReliableType.Reliable,
                ClientInstance.Instance.PlayerId, ++_nextCycleRequestId);
        }
    }

    internal static void GiveCycledWeapon(int playerId)
    {
        if (GameModeManager.IsVanillaScene
            || (GameModeManager.IsActive(GameMode.Juggernaut)
                && playerId == JuggernautState.CurrentJuggernautPlayerId)
            || GameModeManager.IsActive(GameMode.Infected))
        {
            return;
        }
        string? currentWeapon = GetSelectedWeapon(playerId);
        if (currentWeapon == null) return;

        int currentIndex = Allowed.IndexOf(currentWeapon);
        string nextWeapon = Allowed[(currentIndex + 1) % Allowed.Count];
        SelectedWeapons[playerId] = nextWeapon;
        RequestLoadout(playerId, nextWeapon);
    }

    internal static void RequestLoadout(int playerId, string weaponName)
    {
        if (GameModeManager.IsVanillaScene || WeaponService.IsFinalGameScreen)
        {
            return;
        }
        PendingLoadouts[playerId] = Time.unscaledTime + 5f;
        WeaponService.GiveWeapon(playerId, weaponName, SpareMagazines);
    }

    internal static void EnsureCycleLoadouts()
    {
        if (GameModeManager.IsVanillaScene || GameModeManager.ShouldIgnoreGlobalWeaponSettings
            || !Enabled || !Cycle || WeaponService.IsFinalGameScreen || !MyceliumNetwork.InLobby
            || !MyceliumNetwork.IsHost || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 1f;
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner)
            {
                continue;
            }

            FirstPersonController? player = client.PlayerSpawner.player;
            if (player == null || !player || player.playerPickupScript == null || !player.playerPickupScript)
            {
                continue;
            }

            string? selectedWeapon = GetSelectedWeapon(client.PlayerId);
            if (selectedWeapon == null)
            {
                continue;
            }

            if (GameModeManager.IsActive(GameMode.Juggernaut) && client.PlayerId == JuggernautState.CurrentJuggernautPlayerId)
            {
                continue;
            }
            if (GameModeManager.IsActive(GameMode.Infected))
            {
                continue;
            }

            PlayerPickup? pickup = player.playerPickupScript;
            GameObject? heldObject = pickup.objInHand;
            Weapon? heldWeapon = heldObject == null || !heldObject
                ? null
                : heldObject.GetComponent<Weapon>();
            if (heldWeapon != null && heldWeapon.name.StartsWith(selectedWeapon, StringComparison.Ordinal))
            {
                WeaponAmmoTuning.Initialize(heldWeapon, SpareMagazines);
                PendingLoadouts.Remove(client.PlayerId);
                continue;
            }

            if (!PendingLoadouts.TryGetValue(client.PlayerId, out float retryTime) || Time.unscaledTime >= retryTime)
            {
                RequestLoadout(client.PlayerId, selectedWeapon);
            }
        }
    }

    internal static void EnsureDefaultKnifeLoadouts()
    {
        if (!DefaultKnife || !MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby
            || GameModeManager.IsVanillaScene || !GameModeManager.IsCustomMode
            || GameModeManager.IsActive(GameMode.Infected)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WeaponService.IsFinalGameScreen)
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

            FirstPersonController player = client.PlayerSpawner.player;
            int playerObjectId = player.GetInstanceID();
            if (!DefaultKnifeObjects.TryGetValue(client.PlayerId, out int knownObjectId)
                || knownObjectId != playerObjectId)
            {
                DefaultKnifeObjects[client.PlayerId] = playerObjectId;
                DefaultKnifeNextAttemptTimes[client.PlayerId] = Time.unscaledTime + 1.5f;
                DefaultKnifeResolvedObjects.Remove(client.PlayerId);
                continue;
            }

            if (DefaultKnifeResolvedObjects.TryGetValue(client.PlayerId,
                    out int resolvedObjectId)
                && resolvedObjectId == playerObjectId)
            {
                continue;
            }

            PlayerPickup? pickup = player.playerPickupScript;
            if (pickup == null || !pickup)
            {
                continue;
            }

            GameObject? heldObject = pickup.objInHand;
            if (heldObject != null && heldObject)
            {
                DefaultKnifeResolvedObjects[client.PlayerId] = playerObjectId;
                continue;
            }

            if (DefaultKnifeNextAttemptTimes.TryGetValue(client.PlayerId,
                    out float nextAttemptTime)
                && Time.unscaledTime < nextAttemptTime)
            {
                continue;
            }

            DefaultKnifeNextAttemptTimes[client.PlayerId] = Time.unscaledTime + 1f;
            WeaponService.GiveWeapon(client.PlayerId, "Couperet", clearBothHands: false);
        }
    }

    internal static string? GetSelectedWeapon(int playerId)
    {
        if (Allowed.Count == 0)
        {
            return null;
        }

        if (SelectedWeapons.TryGetValue(playerId, out string selectedWeapon) && Allowed.Contains(selectedWeapon))
        {
            return selectedWeapon;
        }

        string initialWeapon = Allowed[0];
        SelectedWeapons[playerId] = initialWeapon;
        return initialWeapon;
    }
}