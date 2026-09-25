using System.Collections.Generic;
using System.Reflection;
using FishNet.Object;
using HarmonyLib;
using MyceliumNetworking;
using UnityEngine;

namespace Eights;

internal static class RespawnProtection
{
    private const float DurationSeconds = 1f;
    private const float ProtectedHealth = 999f / 25f;
    private const float ActiveProtectionSyncIntervalSeconds = 0.25f;
    private static readonly List<Protection> ActiveProtections = new();
    private static readonly Dictionary<int, int> PendingRespawnPlayers = new();
    private static readonly Dictionary<int, int> PendingActiveProtections = new();
    private static readonly List<int> PendingRespawnPlayerIds = new();

    private sealed class Protection
    {
        internal Protection(PlayerHealth player, float expiresAt)
        {
            Player = player;
            ExpiresAt = expiresAt;
            NextSyncTime = 0f;
        }

        internal PlayerHealth Player { get; }
        internal float ExpiresAt { get; set; }
        internal float NextSyncTime { get; set; }
    }

    internal static void Begin(PlayerHealth player)
    {
        Begin(player, true);
    }

    private static void Begin(PlayerHealth player, bool broadcast)
    {
        if (GameModeManager.IsVanillaScene || player == null || !player)
        {
            return;
        }

        foreach (Protection protection in ActiveProtections)
        {
            if (protection.Player == player)
            {
                protection.ExpiresAt = Time.unscaledTime + DurationSeconds;
                Apply(player);
                if (broadcast)
                {
                    BroadcastActiveProtection(player, true, true);
                }
                return;
            }
        }

        HealthSettingsTuning.CaptureBaseline(player);
        ActiveProtections.Add(new Protection(player, Time.unscaledTime + DurationSeconds));
        Apply(player);
        if (broadcast)
        {
            BroadcastActiveProtection(player, true, true);
        }
    }

    internal static void ArmForRespawn(int playerId)
    {
        if (playerId < 0)
        {
            return;
        }

        SetPendingRespawn(playerId, true);
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(GameModeManager.ModId,
                nameof(Plugin.SyncRespawnProtection), ReliableType.Reliable, playerId, true);
        }
    }

    internal static void CancelRespawn(int playerId)
    {
        if (playerId < 0)
        {
            return;
        }

        PendingRespawnPlayers.Remove(playerId);
        if (MyceliumNetwork.InLobby && MyceliumNetwork.IsHost)
        {
            MyceliumNetwork.RPC(GameModeManager.ModId,
                nameof(Plugin.SyncRespawnProtection), ReliableType.Reliable, playerId, false);
        }
    }

    internal static void SetPendingRespawn(int playerId, bool pending)
    {
        if (playerId < 0)
        {
            return;
        }

        if (pending)
        {
            PlayerHealth? currentPlayer = PlayerLookup.FindPlayerHealthById(playerId);
            PendingRespawnPlayers[playerId] = currentPlayer == null || !currentPlayer
                ? -1
                : currentPlayer.GetInstanceID();
        }
        else
        {
            PendingRespawnPlayers.Remove(playerId);
        }
    }

    internal static bool ConsumePendingRespawn(PlayerHealth player)
    {
        if (player == null || !player)
        {
            return false;
        }

        int playerId = player.playerValues?.playerClient?.PlayerId ?? -1;
        if (playerId < 0 || !PendingRespawnPlayers.TryGetValue(playerId, out int oldObjectId)
            || (oldObjectId >= 0 && oldObjectId == player.GetInstanceID()))
        {
            return false;
        }

        PendingRespawnPlayers.Remove(playerId);
        return true;
    }

    internal static void SetActiveRespawn(int playerId, int objectId, bool active)
    {
        if (playerId < 0 || objectId < 0)
        {
            return;
        }

        if (!active)
        {
            PendingActiveProtections.Remove(playerId);
            PendingRespawnPlayers.Remove(playerId);
            return;
        }

        PendingActiveProtections[playerId] = objectId;
        TryBeginActiveRespawn(playerId, objectId);
    }

    internal static void Update()
    {
        PendingRespawnPlayerIds.Clear();
        foreach (int playerId in PendingRespawnPlayers.Keys)
        {
            PendingRespawnPlayerIds.Add(playerId);
        }

        foreach (int playerId in PendingRespawnPlayerIds)
        {
            PlayerHealth? player = PlayerLookup.FindPlayerHealthById(playerId);
            if (player != null && player && player.gameObject.activeInHierarchy
                && ConsumePendingRespawn(player))
            {
                Begin(player);
            }
        }

        PendingRespawnPlayerIds.Clear();
        foreach (int playerId in PendingActiveProtections.Keys)
        {
            PendingRespawnPlayerIds.Add(playerId);
        }

        foreach (int playerId in PendingRespawnPlayerIds)
        {
            if (PendingActiveProtections.TryGetValue(playerId, out int objectId))
            {
                TryBeginActiveRespawn(playerId, objectId);
            }
        }

        for (int index = ActiveProtections.Count - 1; index >= 0; index--)
        {
            Protection protection = ActiveProtections[index];
            if (protection.Player == null || !protection.Player)
            {
                ActiveProtections.RemoveAt(index);
                continue;
            }

            if (Time.unscaledTime < protection.ExpiresAt)
            {
                Apply(protection.Player);
                BroadcastActiveProtection(protection.Player, true, false);
                continue;
            }

            End(protection.Player);
            ActiveProtections.RemoveAt(index);
        }
    }

    internal static bool IsProtected(PlayerHealth player)
    {
        if (player == null || !player)
        {
            return false;
        }

        foreach (Protection protection in ActiveProtections)
        {
            if (protection.Player == player)
            {
                return Time.unscaledTime < protection.ExpiresAt;
            }
        }

        return false;
    }

    internal static bool IsLocalPlayerProtected()
    {
        foreach (Protection protection in ActiveProtections)
        {
            if (protection.Player != null && protection.Player.IsOwner)
            {
                return Time.unscaledTime < protection.ExpiresAt;
            }
        }

        return false;
    }

    private static void TryBeginActiveRespawn(int playerId, int objectId)
    {
        PlayerHealth? player = PlayerLookup.FindPlayerHealthById(playerId);
        if (player == null || !player || !player.gameObject.activeInHierarchy
            || GetNetworkObjectId(player) != objectId)
        {
            return;
        }

        PendingActiveProtections.Remove(playerId);
    PendingRespawnPlayers.Remove(playerId);
        foreach (Protection protection in ActiveProtections)
        {
            if (protection.Player == player)
            {
                return;
            }
        }

        Begin(player, false);
    }

    internal static void ResetState()
    {
        ActiveProtections.Clear();
        PendingRespawnPlayers.Clear();
        PendingActiveProtections.Clear();
    }

    internal static void ApplyHealth(PlayerHealth player)
    {
        player.fullHealth = ProtectedHealth;
        if (player.IsServer)
        {
            float healthDelta = ProtectedHealth - player.sync___get_value_health();
            if (!Mathf.Approximately(healthDelta, 0f))
            {
                HealthSettingsTuning.ApplyingPassiveHealth = true;
                try
                {
                    FishNetCompatibility.TryRemoveHealth(player, -healthDelta);
                }
                finally
                {
                    HealthSettingsTuning.ApplyingPassiveHealth = false;
                }
            }
        }

    }

    private static void Apply(PlayerHealth player)
    {
        ApplyHealth(player);
    }

    private static void End(PlayerHealth player)
    {
        BroadcastActiveProtection(player, false, true);
        HealthSettingsTuning.ApplyIfChanged(player, HealthSettingsState.MaxHealthMultiplier,
            HealthSettingsState.TuningVersion);

        if (player.IsServer)
        {
            float normalHealth = Mathf.Max(0f, player.fullHealth);
            float excessHealth = player.sync___get_value_health() - normalHealth;
            if (excessHealth > 0f)
            {
                HealthSettingsTuning.ApplyingPassiveHealth = true;
                try
                {
                    FishNetCompatibility.TryRemoveHealth(player, excessHealth);
                }
                finally
                {
                    HealthSettingsTuning.ApplyingPassiveHealth = false;
                }
            }
        }
    }

    private static int GetNetworkObjectId(PlayerHealth player)
    {
        NetworkObject? networkObject = player.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            networkObject = player.GetComponentInParent<NetworkObject>();
        }

        return networkObject == null ? -1 : networkObject.ObjectId;
    }

    private static void BroadcastActiveProtection(PlayerHealth player, bool active, bool force)
    {
        if (!player.IsServer || !MyceliumNetwork.InLobby || player.playerValues == null
            || player.playerValues.playerClient == null)
        {
            return;
        }

        int objectId = GetNetworkObjectId(player);
        if (objectId < 0)
        {
            return;
        }

        Protection? protection = ActiveProtections.Find(candidate => candidate.Player == player);
        if (!force && protection != null && Time.unscaledTime < protection.NextSyncTime)
        {
            return;
        }

        if (protection != null)
        {
            protection.NextSyncTime = Time.unscaledTime + ActiveProtectionSyncIntervalSeconds;
        }

        MyceliumNetwork.RPC(GameModeManager.ModId,
            nameof(Plugin.SyncRespawnProtectionActive), ReliableType.Reliable,
            player.playerValues.playerClient.PlayerId, objectId, active);
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_RespawnProtection_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        PlayerHealth? player = __instance.player?.GetComponent<PlayerHealth>();
        if (player != null && RespawnProtection.ConsumePendingRespawn(player))
        {
            RespawnProtection.Begin(player);
        }
    }
}

[HarmonyPatch]
internal static class PlayerSetup_RespawnProtection_Patch
{
    private static MethodBase? TargetMethod()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return typeof(PlayerSetup).GetMethod("OnStartClient___UserLogic", flags)
            ?? typeof(PlayerSetup).GetMethod("OnStartClient", flags);
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(PlayerSetup __instance)
    {
        if (__instance == null)
        {
            return;
        }

        PlayerHealth? player = __instance.GetComponent<PlayerHealth>();
        if (player != null && RespawnProtection.ConsumePendingRespawn(player))
        {
            RespawnProtection.Begin(player);
        }
    }
}