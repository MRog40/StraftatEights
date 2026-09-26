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
    internal static bool DefaultKnife;
    internal static int SpareMagazines = 5;
    internal static List<string> Allowed = new();
    private const float DefaultKnifeSpawnGraceSeconds = 1.5f;
    private const float DefaultKnifeDropGraceSeconds = 0.35f;
    private const float DefaultKnifeRequestIntervalSeconds = 1f;
    private const float DefaultKnifeGrantRetrySeconds = 1.5f;
    private static readonly Dictionary<int, int> LastDefaultKnifeRequestIds = new();
    private static readonly Dictionary<int, float> NextDefaultKnifeGrantTimes = new();
    private static readonly ModeSyncState Sync = new();
    private static float _nextClientSettingsPollTime;
    private static int _localDefaultKnifePlayerId = -1;
    private static int _localDefaultKnifePickupId = -1;
    private static int _localDefaultKnifeRoundId = -1;
    private static bool _localDefaultKnifeHadGun;
    private static bool _localDefaultKnifeInitialGrace;
    private static float _localDefaultKnifeNoGunSince;
    private static float _nextLocalDefaultKnifeRequestTime;
    private static int _localDefaultKnifeRequestId;
    private static int _nextLocalDefaultKnifeRequestId;
    internal static void Apply(bool enabled, string allowedWeapons, int spareMagazines,
        bool defaultKnife)
    {
        spareMagazines = Mathf.Clamp(spareMagazines, 2, 10);
        allowedWeapons ??= string.Empty;
        List<string> nextAllowed = WeaponService.ParseWeaponList(allowedWeapons);
        bool settingsChanged = Enabled != enabled
            || SpareMagazines != spareMagazines || DefaultKnife != defaultKnife;
        bool allowedChanged = Allowed.Count != nextAllowed.Count
            || !Allowed.SequenceEqual(nextAllowed, StringComparer.Ordinal);

        Enabled = enabled;
        DefaultKnife = defaultKnife;
        SpareMagazines = spareMagazines;
        Allowed = nextAllowed;

        if (settingsChanged || allowedChanged)
        {
            WeaponService.ResetPendingRequests();
            ResetDefaultKnifeRequestState(false);
        }
    }
    private static void ApplyFromConfig() => Apply(Plugin.WeaponTweaksEnabled.Value,
        Plugin.AllowedWeapons.Value, Plugin.SpareMagazines.Value, Plugin.DefaultKnife.Value);
    internal static void PushIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost) return;
        ApplyFromConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision,
            Plugin.WeaponTweaksEnabled.Value ? "1" : "0", Plugin.AllowedWeapons.Value,
            Plugin.SpareMagazines.Value.ToString(CultureInfo.InvariantCulture),
            Plugin.DefaultKnife.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.GlobalWeaponsModId, nameof(Plugin.SyncWeaponSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision,
            Plugin.WeaponTweaksEnabled.Value, Plugin.AllowedWeapons.Value, Plugin.SpareMagazines.Value,
            Plugin.DefaultKnife.Value);
    }
    internal static void PeriodicPushIfHost() { if (Sync.IsSettingsPushDue()) PushIfHost(); }
    internal static void OnLobbyEntered()
    {
        Sync.ResetForLobby();
        ResetDefaultKnifeRequestState(true);
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
        ResetDefaultKnifeRequestState(true);
        Sync.ResetForLobby();
        Apply(false, string.Empty, 5, false);
    }
    internal static void OnPlayerEntered(CSteamID player)
    {
        if (MyceliumNetwork.IsHost) MyceliumNetwork.RPCTarget(Plugin.GlobalWeaponsModId, nameof(Plugin.SyncWeaponSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.WeaponTweaksEnabled.Value, Plugin.AllowedWeapons.Value, Plugin.SpareMagazines.Value,
            Plugin.DefaultKnife.Value);
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
        int playerId = PlayerLookup.FindPlayerId(player);
        if (playerId >= 0)
        {
            LastDefaultKnifeRequestIds.Remove(playerId);
            NextDefaultKnifeGrantTimes.Remove(playerId);
            if (_localDefaultKnifePlayerId == playerId)
            {
                ResetLocalDefaultKnifeTracking();
            }
        }
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision,
        string source = "rpc")
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision, source);
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 4, out CSteamID hostId,
                out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !int.TryParse(fields[2], out int spareMagazines)
            || !LobbySnapshotCodec.TryParseBool(fields[3], out bool defaultKnife)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("global-weapons", "settings")))
        {
            return;
        }

        Apply(enabled, fields[1], spareMagazines, defaultKnife);
    }

    internal static void TrackLocalDefaultKnifeFallback(PlayerPickup pickup)
    {
        if (pickup == null || !pickup || !pickup.IsOwner)
        {
            return;
        }

        if (!MyceliumNetwork.InLobby || !DefaultKnife
            || (MyceliumNetwork.IsHost && !GameModeManager.CanUseDefaultKnifeFallback)
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || WeaponService.IsFinalGameScreen)
        {
            ResetLocalDefaultKnifeTracking();
            return;
        }

        int playerId = pickup.playerValues?.playerClient?.PlayerId
            ?? pickup.GetComponent<PlayerHealth>()?.playerValues?.playerClient?.PlayerId ?? -1;
        if (playerId < 0)
        {
            ResetLocalDefaultKnifeTracking();
            return;
        }

        int pickupId = pickup.GetInstanceID();
        int roundId = GameModeManager.RoundId;
        float now = Time.unscaledTime;
        Weapon? rightWeapon = GetHeldWeapon(pickup, true);
        Weapon? leftWeapon = GetHeldWeapon(pickup, false);
        bool hasGun = IsGun(rightWeapon) || IsGun(leftWeapon);
        bool hasDefaultKnife = IsDefaultKnife(rightWeapon) || IsDefaultKnife(leftWeapon);

        if (_localDefaultKnifePlayerId != playerId || _localDefaultKnifePickupId != pickupId
            || _localDefaultKnifeRoundId != roundId)
        {
            _localDefaultKnifePlayerId = playerId;
            _localDefaultKnifePickupId = pickupId;
            _localDefaultKnifeRoundId = roundId;
            _localDefaultKnifeHadGun = hasGun;
            _localDefaultKnifeInitialGrace = !hasGun && !hasDefaultKnife;
            _localDefaultKnifeNoGunSince = now;
            _localDefaultKnifeRequestId = 0;
            _nextLocalDefaultKnifeRequestTime = now;
        }

        if (hasGun)
        {
            _localDefaultKnifeHadGun = true;
            _localDefaultKnifeInitialGrace = false;
            _localDefaultKnifeNoGunSince = now;
            _localDefaultKnifeRequestId = 0;
            return;
        }

        if (IsDefaultKnife(rightWeapon)
            && WeaponService.IsOwnerHandObjectUnattached(pickup, true))
        {
            WeaponService.AttachUnparentedWeapon(pickup);
        }
        if (IsDefaultKnife(leftWeapon)
            && WeaponService.IsOwnerHandObjectUnattached(pickup, false))
        {
            WeaponService.AttachUnparentedLeftWeapon(pickup);
        }

        if (hasDefaultKnife)
        {
            _localDefaultKnifeHadGun = false;
            _localDefaultKnifeInitialGrace = false;
            _localDefaultKnifeNoGunSince = now;
            _localDefaultKnifeRequestId = 0;
            return;
        }

        if (_localDefaultKnifeHadGun)
        {
            _localDefaultKnifeHadGun = false;
            _localDefaultKnifeInitialGrace = false;
            _localDefaultKnifeNoGunSince = now;
            _localDefaultKnifeRequestId = 0;
        }

        float graceSeconds = _localDefaultKnifeInitialGrace
            ? DefaultKnifeSpawnGraceSeconds
            : DefaultKnifeDropGraceSeconds;
        if (now - _localDefaultKnifeNoGunSince < graceSeconds)
        {
            return;
        }
        _localDefaultKnifeInitialGrace = false;

        if (!DefaultKnifeRules.ShouldProvideKnife(DefaultKnife, false))
        {
            return;
        }

        if (_localDefaultKnifeRequestId == 0)
        {
            _localDefaultKnifeRequestId = NextLocalDefaultKnifeRequestId();
        }
        if (now < _nextLocalDefaultKnifeRequestTime)
        {
            return;
        }

        _nextLocalDefaultKnifeRequestTime = now + DefaultKnifeRequestIntervalSeconds;
        if (MyceliumNetwork.IsHost)
        {
            TryGrantDefaultKnifeFallback(playerId, _localDefaultKnifeRequestId, roundId);
        }
        else
        {
            MyceliumNetwork.RPC(Plugin.GlobalWeaponsModId,
                nameof(Plugin.RequestDefaultKnifeFallback), ReliableType.Reliable,
                playerId, _localDefaultKnifeRequestId, roundId);
        }
    }

    internal static void TryGrantDefaultKnifeFallback(int playerId, int requestId, int roundId)
    {
        if (!MyceliumNetwork.IsHost || !MyceliumNetwork.InLobby || playerId < 0 || requestId <= 0
            || roundId != GameModeManager.RoundId || !DefaultKnife
            || !GameModeManager.CanUseDefaultKnifeFallback
            || GameModeManager.Phase != GameModePhase.ActiveRound || WeaponService.IsFinalGameScreen)
        {
            return;
        }

        if (LastDefaultKnifeRequestIds.TryGetValue(playerId, out int lastRequestId)
            && requestId < lastRequestId)
        {
            return;
        }

        PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(playerId);
        FirstPersonController? player = health != null && health
            ? health.GetComponent<FirstPersonController>()
            : null;
        if (player == null || !player || !player.gameObject.activeInHierarchy)
        {
            return;
        }

        PlayerPickup? pickup = player.playerPickupScript;
        if (pickup == null || !pickup || !pickup.IsServer)
        {
            return;
        }

        Weapon? rightWeapon = GetHeldWeapon(pickup, true);
        Weapon? leftWeapon = GetHeldWeapon(pickup, false);
        bool hasGun = IsGun(rightWeapon) || IsGun(leftWeapon);
        if (hasGun)
        {
            RemoveKnifeIfHeld(pickup, rightWeapon, true);
            RemoveKnifeIfHeld(pickup, leftWeapon, false);
            return;
        }

        if (IsDefaultKnife(rightWeapon) || IsDefaultKnife(leftWeapon))
        {
            LastDefaultKnifeRequestIds[playerId] = requestId;
            if (!NextDefaultKnifeGrantTimes.TryGetValue(playerId, out float attachmentRetryTime)
                || Time.unscaledTime >= attachmentRetryTime)
            {
                NextDefaultKnifeGrantTimes[playerId] = Time.unscaledTime
                    + DefaultKnifeGrantRetrySeconds;
                WeaponService.NotifyOwnerWeaponAttached(playerId, IsDefaultKnife(rightWeapon));
            }
            return;
        }

        int playerObjectId = player.GetInstanceID();
        if (GameModeManager.UsesTeamWeaponLoadouts
            && TeamWeaponLoadouts.IsWeaponGrantPending(playerId, playerObjectId))
        {
            return;
        }

        if (requestId == lastRequestId
            && NextDefaultKnifeGrantTimes.TryGetValue(playerId, out float retryTime)
            && Time.unscaledTime < retryTime)
        {
            return;
        }

        LastDefaultKnifeRequestIds[playerId] = requestId;
        NextDefaultKnifeGrantTimes[playerId] = Time.unscaledTime + DefaultKnifeGrantRetrySeconds;
        Plugin.Logger.LogDebug($"[GlobalWeapons] Granting default knife: playerId={playerId}, "
            + $"requestId={requestId}, round={roundId}.");
        WeaponService.GiveWeapon(playerId, DefaultKnifeRules.DefaultWeaponName,
            clearBothHands: false, onlyIfNoGun: true);
    }

    private static int NextLocalDefaultKnifeRequestId()
    {
        _nextLocalDefaultKnifeRequestId = unchecked(_nextLocalDefaultKnifeRequestId + 1);
        if (_nextLocalDefaultKnifeRequestId <= 0)
        {
            _nextLocalDefaultKnifeRequestId = 1;
        }
        return _nextLocalDefaultKnifeRequestId;
    }

    private static void ResetDefaultKnifeRequestState(bool resetSequence)
    {
        LastDefaultKnifeRequestIds.Clear();
        NextDefaultKnifeGrantTimes.Clear();
        ResetLocalDefaultKnifeTracking();
        if (resetSequence)
        {
            _nextLocalDefaultKnifeRequestId = 0;
        }
    }

    private static void ResetLocalDefaultKnifeTracking()
    {
        _localDefaultKnifePlayerId = -1;
        _localDefaultKnifePickupId = -1;
        _localDefaultKnifeRoundId = -1;
        _localDefaultKnifeHadGun = false;
        _localDefaultKnifeInitialGrace = false;
        _localDefaultKnifeNoGunSince = 0f;
        _nextLocalDefaultKnifeRequestTime = 0f;
        _localDefaultKnifeRequestId = 0;
    }

    private static Weapon? GetHeldWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static Weapon? GetHeldWeapon(PlayerPickup pickup, bool rightHand)
    {
        bool hasObject = rightHand ? pickup.hasObjectInHand : pickup.hasObjectInLeftHand;
        return hasObject
            ? GetHeldWeapon(rightHand ? pickup.objInHand : pickup.objInLeftHand)
            : null;
    }

    private static bool IsDefaultKnife(Weapon? weapon)
    {
        return weapon != null && weapon
            && DefaultKnifeRules.IsDefaultKnife(weapon.name);
    }

    private static bool IsGun(Weapon? weapon)
    {
        return weapon != null && weapon && !DefaultKnifeRules.IsKnife(weapon.name);
    }

    private static void RemoveKnifeIfHeld(PlayerPickup pickup, Weapon? weapon,
        bool rightHand)
    {
        if (IsDefaultKnife(weapon))
        {
            WeaponService.RemoveHeldWeapon(pickup, rightHand, weapon!);
        }
    }

}