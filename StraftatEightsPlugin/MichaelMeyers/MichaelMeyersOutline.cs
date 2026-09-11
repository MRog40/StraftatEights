using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

// Gives Michael a local predator-style view of every active enemy player.
internal static class MichaelMeyersOutline
{
    private static readonly Dictionary<int, PlayerHealth> OutlinedPlayers = new();

    internal static void ResetState()
    {
        OutlinedPlayers.Clear();
    }

    internal static void EnforceOutline()
    {
        GameMode activeMode = GameModeManager.ActiveMode;
        if (PlayerOutline.UpdateMode(activeMode))
        {
            OutlinedPlayers.Clear();
        }

        if (GameModeManager.ShouldClearPlayerOutlines
            || !GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            return;
        }

        int localPlayerId = ClientInstance.Instance == null ? -1 : ClientInstance.Instance.PlayerId;
        if (localPlayerId < 0 || MichaelMeyersState.CurrentMichaelPlayerId != localPlayerId)
        {
            ClearApplied();
            return;
        }

        Dictionary<int, PlayerHealth> currentTargets = new();
        foreach (int playerId in PlayerLookup.GetConnectedPlayerIds())
        {
            if (playerId == localPlayerId)
            {
                continue;
            }

            PlayerHealth? health = PlayerLookup.FindPlayerHealthById(playerId);
            if (health != null && health.gameObject.activeInHierarchy)
            {
                currentTargets[playerId] = health;
            }
        }

        if (!TargetsMatch(currentTargets))
        {
            PlayerOutline.ClearApplied();
            OutlinedPlayers.Clear();
            foreach (KeyValuePair<int, PlayerHealth> target in currentTargets)
            {
                OutlinedPlayers[target.Key] = target.Value;
            }
        }

        foreach (PlayerHealth health in OutlinedPlayers.Values)
        {
            if (health != null && health.gameObject.activeInHierarchy)
            {
                PlayerOutline.Apply(health, Color.red);
            }
        }
    }

    private static bool TargetsMatch(Dictionary<int, PlayerHealth> currentTargets)
    {
        if (currentTargets.Count != OutlinedPlayers.Count)
        {
            return false;
        }

        foreach (KeyValuePair<int, PlayerHealth> target in currentTargets)
        {
            if (!OutlinedPlayers.TryGetValue(target.Key, out PlayerHealth outlined)
                || !ReferenceEquals(outlined, target.Value))
            {
                return false;
            }
        }

        return true;
    }

    private static void ClearApplied()
    {
        PlayerOutline.ClearApplied();
        OutlinedPlayers.Clear();
    }
}