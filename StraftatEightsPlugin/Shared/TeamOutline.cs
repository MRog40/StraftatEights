using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class TeamOutline
{
    private const float RefreshIntervalSeconds = 0.25f;
    private static readonly Dictionary<int, PlayerHealth> AppliedPlayers = new();
    private static float _nextRefreshTime;
    private static GameMode _lastMode = GameMode.None;

    internal static void ResetState()
    {
        ClearApplied();
        _nextRefreshTime = 0f;
        _lastMode = GameMode.None;
    }

    internal static void Enforce()
    {
        GameMode activeMode = GameModeManager.IsVanillaScene
            ? GameMode.None
            : GameModeManager.ActiveMode;
        if (activeMode != _lastMode)
        {
            ClearApplied();
            _nextRefreshTime = 0f;
            _lastMode = activeMode;
        }

        IReadOnlyDictionary<int, int>? assignments = activeMode switch
        {
            GameMode.CaptureTheFlag => CaptureTheFlagState.Assignments,
            GameMode.TeamDeathmatch => TeamDeathmatchState.Assignments,
            GameMode.SearchAndDestroy => SearchAndDestroyState.Assignments,
            _ => null
        };
        if (assignments == null || GameModeManager.Phase != GameModePhase.ActiveRound)
        {
            ClearApplied();
            _nextRefreshTime = 0f;
            return;
        }

        bool teammatesOnly = activeMode == GameMode.SearchAndDestroy;
        int localTeamId = -1;
        if (teammatesOnly)
        {
            int localPlayerId = ClientInstance.Instance == null ? -1 : ClientInstance.Instance.PlayerId;
            if (!TeamAssignment.TryGetTeamId(localPlayerId, out localTeamId))
            {
                ClearApplied();
                _nextRefreshTime = 0f;
                return;
            }
        }

        float now = Time.unscaledTime;
        bool refreshMaterials = now >= _nextRefreshTime;
        HashSet<int> currentPlayerIds = new();
        foreach (KeyValuePair<int, int> assignment in assignments)
        {
            if (assignment.Value < 0 || assignment.Value > 1
                || (teammatesOnly && assignment.Value != localTeamId))
            {
                continue;
            }

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
                TeamColorData teamColor = teammatesOnly
                    ? TeamRules.GetColor(TeamRules.BlueTeamId)
                    : TeamRules.GetColor(assignment.Value);
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
