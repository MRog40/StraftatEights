using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class RespawnProtection
{
    private const float DurationSeconds = 2f;
    private const float ProtectedHealth = 1000f;
    private const float OutlineWidth = 0.15f;
    private static readonly Color OutlineColor = new(0.5f, 0.5f, 0.5f);
    private static readonly List<Protection> ActiveProtections = new();

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
        if (player == null || !player)
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

    internal static void Update()
    {
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
                PlayerOutline.ClearTemporary(protection.Player);
            }
        }
        ActiveProtections.Clear();
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

        PlayerOutline.ApplyTemporary(player, OutlineColor, OutlineWidth);
    }

    private static void Apply(PlayerHealth player)
    {
        ApplyHealth(player);
    }

    private static void End(PlayerHealth player)
    {
        PlayerOutline.ClearTemporary(player);
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
        if (player != null)
        {
            RespawnProtection.Begin(player);
        }
    }
}