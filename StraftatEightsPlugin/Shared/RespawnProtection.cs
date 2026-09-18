using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MyceliumNetworking;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class RespawnProtection
{
    private const float DurationSeconds = 2f;
    private const float ProtectedHealth = 999f / 25f;
    private const float OutlineWidth = 0.10f;
    private static readonly Color OutlineColor = new(0.5f, 0.5f, 0.5f);
    private static readonly List<Protection> ActiveProtections = new();
    private static readonly Dictionary<int, int> PendingRespawnPlayers = new();
    private static readonly List<int> PendingRespawnPlayerIds = new();

    private sealed class Protection
    {
        internal Protection(PlayerHealth player, float expiresAt)
        {
            Player = player;
            ExpiresAt = expiresAt;
        }

        internal PlayerHealth Player { get; }
        internal float ExpiresAt { get; set; }
    }

    internal static void Begin(PlayerHealth player)
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
                return;
            }
        }

        HealthSettingsTuning.CaptureBaseline(player);
        ActiveProtections.Add(new Protection(player, Time.unscaledTime + DurationSeconds));
        Apply(player);
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

    internal static bool IsProtected(Weapon weapon)
    {
        if (weapon == null)
        {
            return false;
        }

        PlayerHealth? player = weapon.playerController == null
            ? null
            : weapon.playerController.GetComponent<PlayerHealth>();
        player ??= weapon.GetComponentInParent<PlayerHealth>();
        if (player == null && weapon.rootObject != null)
        {
            player = weapon.rootObject.GetComponent<PlayerHealth>();
        }

        return player != null && IsProtected(player);
    }

    internal static void ResetState()
    {
        foreach (Protection protection in ActiveProtections)
        {
            if (protection.Player != null && protection.Player)
            {
                PlayerOutline.ClearTemporary(protection.Player, OutlineColor, OutlineWidth);
            }
        }
        ActiveProtections.Clear();
        PendingRespawnPlayers.Clear();
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

        if (IsLocalPlayer(player))
        {
            PlayerOutline.ClearTemporary(player, OutlineColor, OutlineWidth);
        }
        else if (!player.IsOwner)
        {
            PlayerOutline.ApplyTemporary(player, OutlineColor, OutlineWidth);
        }
    }

    private static bool IsLocalPlayer(PlayerHealth player)
    {
        int localPlayerId = ClientInstance.Instance == null
            ? -1
            : ClientInstance.Instance.PlayerId;
        return localPlayerId >= 0
            && player.playerValues?.playerClient?.PlayerId == localPlayerId;
    }

    private static void Apply(PlayerHealth player)
    {
        ApplyHealth(player);
    }

    private static void End(PlayerHealth player)
    {
        PlayerOutline.ClearTemporary(player, OutlineColor, OutlineWidth);
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