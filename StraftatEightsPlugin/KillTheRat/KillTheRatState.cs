using System;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class KillTheRatState
{
    internal const string SettingsLobbyDataKey = "StraftatEights_KillTheRat_Settings";
    internal const string LiveLobbyDataKey = "StraftatEights_KillTheRat_Live";
    internal const string HumanWeaponName = "Glock";
    internal const string RatWeaponName = "Taser";
    internal const float RatMovementMultiplier = 1.3f;
    internal const float VoidDeathY = -300f;
    internal static bool Enabled;
    internal static int CurrentRatPlayerId = -1;
    internal static int PointsToWin => GameModeManager.EffectivePointsToWin;
    internal static int WinnerId = -1;
    internal static readonly Dictionary<int, int> Points = new();

    private static float _nextLoadoutCheckTime;
    private static float _survivalAccumulator;
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

    private static void ApplySettingsFromHostConfig() => ApplySettings(Plugin.KillTheRatEnabled.Value);

    internal static void PushSettingsIfHost()
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        ApplySettingsFromHostConfig();
        int revision = Sync.NextSettingsRevision();
        ModeLobbyDataSync.Publish(SettingsLobbyDataKey, MyceliumNetwork.LobbyHost,
            GameModeManager.RoundId, revision, Plugin.KillTheRatEnabled.Value ? "1" : "0");
        MyceliumNetwork.RPC(Plugin.KillTheRatModId, nameof(Plugin.SyncKillTheRatSettings), ReliableType.Reliable,
            MyceliumNetwork.LobbyHost, GameModeManager.RoundId, revision, Plugin.KillTheRatEnabled.Value);
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

        MyceliumNetwork.RPCTarget(Plugin.KillTheRatModId, nameof(Plugin.SyncKillTheRatSettings), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, GameModeManager.RoundId, Sync.SettingsRevision,
            Plugin.KillTheRatEnabled.Value);
        MyceliumNetwork.RPCTarget(Plugin.KillTheRatModId, nameof(Plugin.SyncKillTheRatLiveState), player,
            ReliableType.Reliable, MyceliumNetwork.LobbyHost, CurrentRatPlayerId, SerializePoints(),
            WinnerId, GameModeManager.RoundId, Sync.LiveRevision);
    }

    internal static bool TryAcceptSettingsSnapshot(CSteamID hostId, int roundId, int revision)
    {
        return Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision);
    }

    internal static void ResetMatchState()
    {
        Sync.ResetLiveState();
        _survivalAccumulator = 0f;
        _nextLoadoutCheckTime = 0f;
        CurrentRatPlayerId = -1;
        WinnerId = -1;
        Points.Clear();
        PendingLoadouts.Clear();
    }

    internal static void ApplyLiveState(CSteamID hostId, int ratPlayerId, string pointsData,
        int winnerId, int roundId, int revision, string source = "rpc")
    {
        if (ratPlayerId < -1 || winnerId < -1)
        {
            return;
        }
        if (!Sync.TryAcceptLiveSnapshot(hostId, roundId, revision, source))
        {
            return;
        }

        DebugLog.Info($"KillTheRat live state accepted source={source} host={hostId.m_SteamID} "
            + $"round={roundId} revision={revision} rat={ratPlayerId} points={Points.Count}");

        CurrentRatPlayerId = ratPlayerId;
        WinnerId = winnerId;
        Points.Clear();
        foreach (KeyValuePair<int, int> entry in ScoreCodec.Parse(pointsData, PointsToWin))
        {
            Points[entry.Key] = entry.Value;
        }
    }

    internal static void ServerTick(float deltaTime)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.KillTheRat)
            || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost
            || CurrentRatPlayerId < 0 || WinnerId >= 0)
        {
            return;
        }

        _survivalAccumulator += Mathf.Max(0f, deltaTime);
        int seconds = Mathf.FloorToInt(_survivalAccumulator);
        if (seconds <= 0)
        {
            return;
        }

        _survivalAccumulator -= seconds;
        for (int second = 0; second < seconds && WinnerId < 0; second++)
        {
            AwardPoints(CurrentRatPlayerId, ScoreRules.PointsPerRatSurvivalSecond);
        }
    }

    internal static void OnServerKill(int deadPlayerId, int killerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.KillTheRat) || WinnerId >= 0)
        {
            return;
        }

        bool legitKill = killerId >= 0 && killerId != deadPlayerId;
        if (deadPlayerId == CurrentRatPlayerId)
        {
            if (legitKill && killerId != CurrentRatPlayerId && AwardPoints(killerId, ScoreRules.PointsPerKill))
            {
                BecomeRat(killerId, PlayerLookup.GetPlayerNameTag(killerId)
                    + " killed the RAT and is now the RAT! Kill them!");
            }
            else if (WinnerId < 0)
            {
                ClearRat();
            }
            return;
        }

        if (CurrentRatPlayerId < 0 && legitKill)
        {
            BecomeRat(killerId, PlayerLookup.GetPlayerNameTag(killerId)
                + " got the first kill and is now the RAT! Kill them!");
        }
    }

    internal static bool IsRat(PlayerHealth health)
    {
        return GameModeManager.IsActive(GameMode.KillTheRat)
            && GetPlayerId(health) == CurrentRatPlayerId;
    }

    internal static bool IsRat(FirstPersonController controller)
    {
        if (controller == null)
        {
            return false;
        }

        PlayerHealth? health = controller.GetComponent<PlayerHealth>();
        return health != null && IsRat(health);
    }

    internal static bool HandleHumanVoidFall(FirstPersonController controller)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.KillTheRat)
            || controller == null || controller.transform.position.y >= VoidDeathY
            || IsRat(controller))
        {
            return false;
        }

        PlayerHealth? health = controller.GetComponent<PlayerHealth>();
        if (health == null || health.sync___get_value_health() <= 0f)
        {
            return false;
        }

        if (!health.IsServer && !controller.IsOwner)
        {
            return false;
        }

        Vector3 safePosition = controller.transform.position;
        safePosition.y = VoidDeathY + 1f;
        controller.transform.position = safePosition;
        float lethalDamage = health.sync___get_value_health() + 1f;
        if (health.IsServer)
        {
            return FishNetCompatibility.TryRemoveHealth(health, lethalDamage);
        }

        health.RemoveHealth(lethalDamage);
        return true;
    }

    internal static bool IsRatWeapon(Weapon weapon)
    {
        return weapon != null && weapon.name.StartsWith(RatWeaponName, StringComparison.Ordinal);
    }

    internal static bool IsHumanWeapon(Weapon weapon)
    {
        return weapon != null && weapon.name.StartsWith(HumanWeaponName, StringComparison.Ordinal);
    }

    internal static void EnsureLoadouts()
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.KillTheRat)
            || !MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost || WeaponService.IsFinalGameScreen
            || Time.unscaledTime < _nextLoadoutCheckTime)
        {
            return;
        }

        _nextLoadoutCheckTime = Time.unscaledTime + 0.5f;
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerSpawner == null || !client.PlayerSpawner)
            {
                continue;
            }

            PlayerPickup? pickup = client.PlayerSpawner.player?.playerPickupScript;
            if (pickup == null)
            {
                continue;
            }

            bool isRat = client.PlayerId == CurrentRatPlayerId;
            Weapon? rightWeapon = GetWeapon(pickup.objInHand);
            bool rightLoadoutReady = rightWeapon != null
                && rightWeapon.name.StartsWith(isRat ? RatWeaponName : HumanWeaponName,
                    StringComparison.Ordinal);
            if (rightLoadoutReady)
            {
                if (!isRat)
                {
                    WeaponAmmoTuning.InitializeUnlimited(rightWeapon!);
                }
                PendingLoadouts.Remove(client.PlayerId);
                continue;
            }

            if (!PendingLoadouts.TryGetValue(client.PlayerId, out float retryTime)
                || Time.unscaledTime >= retryTime)
            {
                PendingLoadouts[client.PlayerId] = Time.unscaledTime + 2f;
                if (!rightLoadoutReady)
                {
                    WeaponService.GiveWeapon(client.PlayerId, isRat ? RatWeaponName : HumanWeaponName,
                        unlimitedAmmo: !isRat);
                }
            }
        }
    }

    internal static void RequestLoadout(int playerId)
    {
        if (!Enabled || !GameModeManager.IsActive(GameMode.KillTheRat) || !MyceliumNetwork.IsHost)
        {
            return;
        }

        bool isRat = playerId == CurrentRatPlayerId;
        PendingLoadouts[playerId] = Time.unscaledTime + 2f;
        WeaponService.GiveWeapon(playerId, isRat ? RatWeaponName : HumanWeaponName,
            unlimitedAmmo: !isRat);
    }

    private static bool AwardPoints(int playerId, int amount)
    {
        Points.TryGetValue(playerId, out int current);
        int total = current + amount;
        Points[playerId] = total;
        GameModeHud.ShowScorePopupForPlayer(playerId, amount);
        if (total >= PointsToWin)
        {
            WinnerId = playerId;
            Announce(PlayerLookup.GetPlayerNameTag(playerId) + " reached " + PointsToWin
                + " points and won the EXTERMINATORS round!");
            BroadcastLiveState();
            GameModeManager.CompleteCustomRound(ScoreManager.Instance.GetTeamId(playerId));
            return false;
        }

        BroadcastLiveState();
        return true;
    }

    private static void BecomeRat(int playerId, string announcement)
    {
        CurrentRatPlayerId = playerId;
        _survivalAccumulator = 0f;
        Announce(announcement);
        PendingLoadouts.Remove(playerId);
        RequestLoadout(playerId);
        BroadcastLiveState();
    }

    private static void ClearRat()
    {
        CurrentRatPlayerId = -1;
        _survivalAccumulator = 0f;
        PendingLoadouts.Clear();
        Announce("The RAT died. Humans, kill each other to choose a new RAT!");
        BroadcastLiveState();
    }

    private static Weapon? GetWeapon(GameObject? heldObject)
    {
        return heldObject == null || !heldObject ? null : heldObject.GetComponent<Weapon>();
    }

    private static int GetPlayerId(PlayerHealth health)
    {
        return health.playerValues?.playerClient?.PlayerId ?? -1;
    }

    private static string SerializePoints() => ScoreCodec.Serialize(Points);

    private static void BroadcastLiveState()
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            int revision = Sync.NextLiveRevision();
            ModeLobbyDataSync.Publish(LiveLobbyDataKey, MyceliumNetwork.LobbyHost,
                GameModeManager.RoundId, revision, CurrentRatPlayerId.ToString(),
                WinnerId.ToString(), SerializePoints());
            MyceliumNetwork.RPC(Plugin.KillTheRatModId, nameof(Plugin.SyncKillTheRatLiveState), ReliableType.Reliable,
                MyceliumNetwork.LobbyHost, CurrentRatPlayerId, SerializePoints(), WinnerId,
                GameModeManager.RoundId, revision);
        }
    }

    private static void ApplyLobbySettingsSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(SettingsLobbyDataKey, 1, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !LobbySnapshotCodec.TryParseBool(fields[0], out bool enabled)
            || !Sync.TryAcceptSettingsSnapshot(hostId, roundId, revision,
                ModeLobbyDataSync.Source("kill-the-rat", "settings")))
        {
            return;
        }

        ApplySettings(enabled);
    }

    private static void ApplyLobbyLiveSnapshot()
    {
        if (!ModeLobbyDataSync.TryRead(LiveLobbyDataKey, 3, out CSteamID hostId,
            out int roundId, out int revision, out string[] fields)
            || !int.TryParse(fields[0], out int ratPlayerId)
            || !int.TryParse(fields[1], out int winnerId))
        {
            return;
        }

        ApplyLiveState(hostId, ratPlayerId, fields[2], winnerId, roundId, revision,
            ModeLobbyDataSync.Source("kill-the-rat", "live"));
    }

    private static void Announce(string text)
    {
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(Plugin.KillTheRatModId, nameof(Plugin.KillTheRatAnnounce), ReliableType.Reliable, text);
        }
    }
}