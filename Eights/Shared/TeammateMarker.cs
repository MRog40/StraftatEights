using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Eights;

internal static class TeammateMarker
{
    private const float RefreshIntervalSeconds = 0.25f;
    private const float MarkerHeight = 2.2f;
    private const float MarkerSize = 0.21f;
    private const float DeathFlashDurationSeconds = 0.18f;
    private static readonly Color MarkerColor = new(0.22f, 0.57f, 1f, 0.92f);
    private static readonly Color DeathMarkerColor = new(1f, 0.08f, 0.08f, 0.98f);
    private static readonly Dictionary<int, GameObject> Markers = new();
    private static readonly Dictionary<int, PlayerHealth> TrackedPlayers = new();
    private static readonly Dictionary<int, float> DeathFlashUntil = new();
    private static float _nextRefreshTime;
    private static GameMode _lastMode = GameMode.None;

    internal static void Enforce()
    {
        GameMode activeMode = GameModeManager.IsVanillaScene
            ? GameMode.None
            : GameModeManager.ActiveMode;
        if (activeMode != _lastMode)
        {
            ClearState();
            _lastMode = activeMode;
        }

        if (GameModeManager.Phase != GameModePhase.ActiveRound
            || !TryGetAssignments(activeMode, out IReadOnlyDictionary<int, int> assignments))
        {
            ClearState();
            return;
        }

        float now = Time.unscaledTime;
        ExpireDeathFlashes(now);
        if (now < _nextRefreshTime)
        {
            return;
        }

        int localPlayerId = ClientInstance.Instance == null ? -1 : ClientInstance.Instance.PlayerId;
        if (!TeamAssignment.TryGetTeamId(localPlayerId, out int localTeamId))
        {
            ClearState();
            return;
        }

        HashSet<int> currentPlayerIds = new();
        foreach (KeyValuePair<int, int> assignment in assignments)
        {
            if (assignment.Key == localPlayerId || assignment.Value != localTeamId)
            {
                continue;
            }

            if (DeathFlashUntil.ContainsKey(assignment.Key))
            {
                continue;
            }

            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(assignment.Key);
            if (health == null || !health || !health.gameObject.activeInHierarchy)
            {
                continue;
            }

            currentPlayerIds.Add(assignment.Key);
            if (!Markers.TryGetValue(assignment.Key, out GameObject? marker)
                || marker == null || !marker)
            {
                marker = FloatingObjectiveMarker.CreateOverheadCircle(
                    $"TeammateMarker_{assignment.Key}", MarkerColor);
                Markers[assignment.Key] = marker;
            }

            if (!TrackedPlayers.TryGetValue(assignment.Key, out PlayerHealth? previous)
                || previous != health || marker.transform.parent != health.transform)
            {
                marker.transform.SetParent(health.transform, false);
                marker.transform.localPosition = Vector3.up * MarkerHeight;
                marker.transform.localRotation = Quaternion.identity;
                marker.transform.localScale = Vector3.one * MarkerSize;
            }

            SetMarkerColor(marker, MarkerColor);
            marker.SetActive(true);
            TrackedPlayers[assignment.Key] = health;
        }

        List<int> removedPlayerIds = new();
        foreach (KeyValuePair<int, PlayerHealth> tracked in TrackedPlayers)
        {
            if (currentPlayerIds.Contains(tracked.Key))
            {
                continue;
            }

            if (DeathFlashUntil.ContainsKey(tracked.Key))
            {
                continue;
            }

            if (Markers.TryGetValue(tracked.Key, out GameObject? marker)
                && marker != null && marker)
            {
                marker.SetActive(false);
            }
            removedPlayerIds.Add(tracked.Key);
        }

        foreach (int playerId in removedPlayerIds)
        {
            TrackedPlayers.Remove(playerId);
        }

        _nextRefreshTime = now + RefreshIntervalSeconds;
    }

    internal static void ResetState()
    {
        ClearState();
        _lastMode = GameMode.None;
    }

    private static bool TryGetAssignments(GameMode mode,
        out IReadOnlyDictionary<int, int> assignments)
    {
        assignments = mode switch
        {
            GameMode.Hardpoint => HardpointState.Assignments,
            GameMode.CaptureTheFlag => CaptureTheFlagState.Assignments,
            GameMode.TeamDeathmatch => TeamDeathmatchState.Assignments,
            GameMode.SearchAndDestroy => SearchAndDestroyState.Assignments,
            GameMode.NinjaHunters => HuntersState.Assignments,
            GameMode.RabbitHunters => HuntersState.Assignments,
            GameMode.TankBattle => HuntersState.Assignments,
            _ => null!
        };
        return assignments != null;
    }

    private static void ClearState()
    {
        foreach (GameObject marker in Markers.Values)
        {
            if (marker != null && marker)
            {
                Object.Destroy(marker);
            }
        }

        Markers.Clear();
        TrackedPlayers.Clear();
        DeathFlashUntil.Clear();
        _nextRefreshTime = 0f;
    }

    internal static void OnPlayerDied(PlayerHealth player)
    {
        if (player == null || !player || !GameModeManager.IsActive(GameMode.Hardpoint)
            && !GameModeManager.IsActive(GameMode.CaptureTheFlag)
            && !GameModeManager.IsActive(GameMode.SearchAndDestroy)
            && !GameModeManager.IsActive(GameMode.TeamDeathmatch)
            && !GameModeManager.IsHuntersActive)
        {
            return;
        }

        int playerId = player.playerValues?.playerClient?.PlayerId ?? -1;
        if (playerId < 0 || !Markers.TryGetValue(playerId, out GameObject? marker)
            || marker == null || !marker)
        {
            return;
        }

        DeathFlashUntil[playerId] = Time.unscaledTime + DeathFlashDurationSeconds;
        TrackedPlayers.Remove(playerId);
        marker.transform.SetParent(null, true);
        marker.transform.position = player.transform.position + Vector3.up * MarkerHeight;
        marker.transform.localScale = Vector3.one * MarkerSize;
        SetMarkerColor(marker, DeathMarkerColor);
        marker.SetActive(true);
    }

    private static void ExpireDeathFlashes(float now)
    {
        List<int> expiredPlayerIds = new();
        foreach (KeyValuePair<int, float> flash in DeathFlashUntil)
        {
            if (now < flash.Value)
            {
                continue;
            }

            if (Markers.TryGetValue(flash.Key, out GameObject? marker)
                && marker != null && marker)
            {
                marker.SetActive(false);
            }
            expiredPlayerIds.Add(flash.Key);
        }

        foreach (int playerId in expiredPlayerIds)
        {
            DeathFlashUntil.Remove(playerId);
        }
    }

    private static void SetMarkerColor(GameObject marker, Color color)
    {
        Renderer? renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = color;
        }
    }
}

[HarmonyPatch]
internal static class PlayerHealth_TeammateMarkerDeath_Patch
{
    private static MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(PlayerHealth),
            "RpcLogic___DespawnObjectObservers_", method => method.ReturnType == typeof(void)
                && method.GetParameters().Length == 0);
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(PlayerHealth __instance)
    {
        TeammateMarker.OnPlayerDied(__instance);
    }
}
