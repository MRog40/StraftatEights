using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HardpointOutline
{
    private const float RefreshIntervalSeconds = 0.25f;
    private static readonly Dictionary<int, PlayerHealth> AppliedPlayers = new();
    private static float _nextRefreshTime;

    internal static void ResetState()
    {
        ClearApplied();
        _nextRefreshTime = 0f;
    }

    internal static void Enforce()
    {
        if (!GameModeManager.IsActive(GameMode.Hardpoint))
        {
            ClearApplied();
            _nextRefreshTime = 0f;
            return;
        }

        float now = Time.unscaledTime;
        bool refreshMaterials = now >= _nextRefreshTime;
        HashSet<int> currentPlayerIds = new();
        foreach (KeyValuePair<int, int> assignment in HardpointState.Assignments)
        {
            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(assignment.Key);
            if (health == null || !health || !health.gameObject.activeInHierarchy)
            {
                continue;
            }

            currentPlayerIds.Add(assignment.Key);
            bool playerChanged = !AppliedPlayers.TryGetValue(assignment.Key,
                out PlayerHealth? previous) || previous != health;
            if (playerChanged && previous != null && previous)
            {
                PlayerOutline.Clear(previous);
            }

            AppliedPlayers[assignment.Key] = health;
            if (playerChanged || refreshMaterials)
            {
                TeamColorData teamColor = TeamRules.GetColor(assignment.Value);
                PlayerOutline.Apply(health, new Color32(teamColor.Red, teamColor.Green,
                    teamColor.Blue, 255));
            }
        }

        List<int> removedPlayerIds = new();
        foreach (KeyValuePair<int, PlayerHealth> applied in AppliedPlayers)
        {
            if (!currentPlayerIds.Contains(applied.Key))
            {
                PlayerOutline.Clear(applied.Value);
                removedPlayerIds.Add(applied.Key);
            }
        }

        foreach (int playerId in removedPlayerIds)
        {
            AppliedPlayers.Remove(playerId);
        }

        _nextRefreshTime = now + RefreshIntervalSeconds;
    }

    private static void ClearApplied()
    {
        foreach (PlayerHealth player in AppliedPlayers.Values)
        {
            if (player != null && player)
            {
                PlayerOutline.Clear(player);
            }
        }

        AppliedPlayers.Clear();
    }
}