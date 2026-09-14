using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HardpointOutline
{
    private static readonly Dictionary<int, PlayerHealth> AppliedPlayers = new();

    internal static void ResetState()
    {
        ClearApplied();
    }

    internal static void Enforce()
    {
        if (!GameModeManager.IsActive(GameMode.Hardpoint))
        {
            ClearApplied();
            return;
        }

        HashSet<int> currentPlayerIds = new();
        foreach (KeyValuePair<int, int> assignment in HardpointState.Assignments)
        {
            PlayerHealth? health = PlayerLookup.FindPlayerHealthById(assignment.Key);
            if (health == null || !health.gameObject.activeInHierarchy)
            {
                continue;
            }

            currentPlayerIds.Add(assignment.Key);
            if (AppliedPlayers.TryGetValue(assignment.Key, out PlayerHealth? previous)
                && previous != health)
            {
                PlayerOutline.Clear(previous);
            }

            AppliedPlayers[assignment.Key] = health;
            TeamColorData teamColor = TeamRules.GetColor(assignment.Value);
            PlayerOutline.Apply(health, new Color32(teamColor.Red, teamColor.Green,
                teamColor.Blue, 255));
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