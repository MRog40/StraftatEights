using System;
using System.Collections;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class MichaelMeyersState
{
    internal const string WeaponName = "Couperet";
    internal const float MovementMultiplier = 1.05f;
    internal static bool Enabled;
    internal static int CurrentMichaelPlayerId = -1;
    internal static bool OneVsOne;

    private static int _winnerId = -1;
    private static int _roundToken;
    private static float _nextSettingsPushTime;
    private static float _nextLiveStatePushTime;
    private static float _nextLoadoutCheckTime;
    private static int _settingsRevision;
    private static int _lastSettingsRoundId = -1;
    private static int _lastSettingsRevision = -1;
    private static int _liveStateRevision;
    private static int _lastLiveStateRoundId = -1;
    private static int _lastLiveStateRevision = -1;
    private static readonly HashSet<int> RoundPlayers = new();
    private static readonly HashSet<int> AlivePlayers = new();
    private static readonly Dictionary<int, float> PendingLoadouts = new();

    internal static void ApplySettings(bool enabled)
    {
        if (Enabled != enabled)
        {
            ResetMatchState();
        }
        Enabled = enabled;
    }

    private static void ApplyFromConfig()
    {
        ApplySettings(Plugin.MichaelMeyersEnabled.Value);
    }

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplyFromConfig();
        MyceliumNetwork.RPC(Plugin.MichaelMeyersModId, nameof(Plugin.SyncMichaelMeyersSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, ++_settingsRevision,
            Plugin.MichaelMeyersEnabled.Value);
    }

    internal static void PeriodicPushSettingsIfHost()
    {
        if (HostSettingsSync.IsDue(ref _nextSettingsPushTime))
        {
            PushSettingsIfHost();
        }
    }

    internal static void PeriodicPushLiveStateIfHost()
    {
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

        MyceliumNetwork.RPCTarget(Plugin.MichaelMeyersModId, nameof(Plugin.SyncMichaelMeyersSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, _settingsRevision,
            Plugin.MichaelMeyersEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.MichaelMeyersModId, nameof(Plugin.SyncMichaelMeyersLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, CurrentMichaelPlayerId, OneVsOne,
            GameModeManager.RoundId, _liveStateRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastSettingsRoundId, ref _lastSettingsRevision);
    }

    internal static void ResetMatchState()
    {
        _roundToken++;
        _liveStateRevision++;
        _lastLiveStateRoundId = -1;
        _lastLiveStateRevision = -1;
        _winnerId = -1;
        CurrentMichaelPlayerId = -1;
        OneVsOne = false;
        RoundPlayers.Clear();
        AlivePlayers.Clear();
        PendingLoadouts.Clear();
        _nextLoadoutCheckTime = 0f;
    }

    internal static void ApplyLiveState(CSteamID hostId, int michaelPlayerId, bool oneVsOne,
        int roundId, int revision)
    {
        if (michaelPlayerId < -1)
        {
            return;
        }
        if (!SessionState.TryAcceptSettingsSnapshot(hostId, roundId, revision,
            ref _lastLiveStateRoundId, ref _lastLiveStateRevision))
        {
            return;
        }
        CurrentMichaelPlayerId = michaelPlayerId;
        OneVsOne = oneVsOne;
    }

    internal static void OnRoundStarted()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.MichaelMeyers) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ResetMatchState();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                RoundPlayers.Add(client.PlayerId);
                AlivePlayers.Add(client.PlayerId);
            }
        }

        if (RoundPlayers.Count < 2 || Plugin.Instance == null)
        {
            return;
        }

        int token = _roundToken;
        Plugin.Instance.StartCoroutine(SelectMichaelAfterDelay(token, SessionState.Generation, GameModeManager.RoundId));
        BroadcastLiveState();
    }

    private static IEnumerator SelectMichaelAfterDelay(int token, int sessionGeneration, int roundId)
    {
        yield return new WaitForSeconds(5f);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || token != _roundToken || !Enabled || !GameModeManager.IsActive(GameMode.MichaelMeyers)
            || _winnerId >= 0)
        {
            yield break;
        }

        List<int> candidates = new();
        foreach (int playerId in AlivePlayers)
        {
            if (ClientInstance.playerInstances.ContainsKey(playerId))
            {
                candidates.Add(playerId);
            }
        }

        if (candidates.Count < 2)
        {
            yield break;
        }

        CurrentMichaelPlayerId = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        Announce(PlayerLookup.GetPlayerNameTag(CurrentMichaelPlayerId) + " is <color=#CC2222><b>MICHAEL MEYERS</b></color>!");
        BroadcastLiveState();
        GiveStartingWeapon(CurrentMichaelPlayerId);
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.MichaelMeyers)
            || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost || WeaponService.IsFinalGameScreen
            || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 0.5f;
        foreach (int playerId in AlivePlayers)
        {
            PlayerPickup? pickup = FindPickup(playerId);
            if (pickup == null)
            {
                continue;
            }

            if (CanHoldCouperet(playerId))
            {
                if (IsCouperetHeld(pickup))
                {
                    PendingLoadouts.Remove(playerId);
                }
                else if (!PendingLoadouts.TryGetValue(playerId, out float retryTime)
                    || Time.unscaledTime >= retryTime)
                {
                    PendingLoadouts[playerId] = Time.unscaledTime + 2f;
                    WeaponService.GiveWeapon(playerId, WeaponName);
                }
            }
            else if (HasHeldObject(pickup))
            {
                WeaponService.ClearHeldWeapons(pickup);
            }
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.MichaelMeyers) || _winnerId >= 0)
        {
            return;
        }

        EnsureTrackedPlayers();
        if (!AlivePlayers.Remove(deadPlayerId))
        {
            return;
        }

        if (AlivePlayers.Count <= 1)
        {
            int winnerId = -1;
            foreach (int playerId in AlivePlayers)
            {
                winnerId = playerId;
                break;
            }
            FinishRound(winnerId);
            return;
        }

        if (CurrentMichaelPlayerId >= 0 && deadPlayerId != CurrentMichaelPlayerId
            && AlivePlayers.Count == 2 && AlivePlayers.Contains(CurrentMichaelPlayerId))
        {
            OneVsOne = true;
            int survivorId = -1;
            foreach (int playerId in AlivePlayers)
            {
                if (playerId != CurrentMichaelPlayerId)
                {
                    survivorId = playerId;
                    break;
                }
            }

            if (survivorId >= 0)
            {
                Announce(PlayerLookup.GetPlayerNameTag(survivorId) + " received a <b>COUPERET</b>. Fight for the final kill!");
                GiveStartingWeapon(survivorId);
            }
        }

        BroadcastLiveState();
    }

    internal static bool IsMichael(PlayerHealth health)
    {
        return GameModeManager.IsActive(GameMode.MichaelMeyers)
            && health.playerValues?.playerClient?.PlayerId == CurrentMichaelPlayerId;
    }

    internal static bool CanHoldCouperet(PlayerHealth health)
    {
        int playerId = health.playerValues?.playerClient?.PlayerId ?? -1;
        return CanHoldCouperet(playerId);
    }

    private static bool CanHoldCouperet(int playerId)
    {
        return playerId >= 0 && GameModeManager.IsActive(GameMode.MichaelMeyers)
            && (playerId == CurrentMichaelPlayerId || (OneVsOne && playerId != CurrentMichaelPlayerId));
    }

    internal static bool IsMichael(FirstPersonController controller)
    {
        PlayerHealth? health = controller == null ? null : controller.GetComponent<PlayerHealth>();
        return health != null && IsMichael(health);
    }

    internal static bool IsCouperet(Weapon weapon)
    {
        return weapon != null && weapon.name.StartsWith(WeaponName, StringComparison.Ordinal);
    }

    private static void EnsureTrackedPlayers()
    {
        if (RoundPlayers.Count != 0)
        {
            return;
        }

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client != null && client)
            {
                RoundPlayers.Add(client.PlayerId);
                AlivePlayers.Add(client.PlayerId);
            }
        }
    }

    private static PlayerPickup? FindPickup(int playerId)
    {
        if (!ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client)
            || client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner)
        {
            return null;
        }

        return client.PlayerSpawner.player?.playerPickupScript;
    }

    private static bool IsCouperetHeld(PlayerPickup pickup)
    {
        Weapon? rightWeapon = GetWeapon(pickup.objInHand);
        Weapon? leftWeapon = GetWeapon(pickup.objInLeftHand);
        return (rightWeapon != null && IsCouperet(rightWeapon))
            || (leftWeapon != null && IsCouperet(leftWeapon));
    }

    private static bool HasHeldObject(PlayerPickup pickup)
    {
        return (pickup.objInHand != null && pickup.objInHand)
            || (pickup.objInLeftHand != null && pickup.objInLeftHand);
    }

    private static Weapon? GetWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static void FinishRound(int winnerId)
    {
        if (winnerId < 0 || ScoreManager.Instance == null)
        {
            return;
        }

        _winnerId = winnerId;
        Announce(PlayerLookup.GetPlayerNameTag(winnerId) + " won the <b>MICHAEL MEYERS</b> round!");
        BroadcastLiveState();
        GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(winnerId));
    }

    internal static void GiveStartingWeapon(int playerId)
    {
        bool canHoldCouperet = playerId == CurrentMichaelPlayerId
            || (OneVsOne && AlivePlayers.Contains(playerId));
        if (Enabled && GameModeManager.IsActive(GameMode.MichaelMeyers) && canHoldCouperet)
        {
            PendingLoadouts[playerId] = Time.unscaledTime + 2f;
            WeaponService.GiveWeapon(playerId, WeaponName);
        }
    }

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.MichaelMeyersModId, nameof(Plugin.SyncMichaelMeyersLiveState),
                ReliableType.Reliable, MyceliumNetwork.LobbyHost, CurrentMichaelPlayerId, OneVsOne,
                GameModeManager.RoundId, ++_liveStateRevision);
        }
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.MichaelMeyersModId, nameof(Plugin.MichaelMeyersAnnounce), ReliableType.Reliable, text);
        }
    }
}
