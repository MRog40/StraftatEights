using System;
using System.Collections.Generic;
using System.Linq;

namespace StraftatEightsPlugin;

internal enum CaptureTheFlagFlagStatus
{
    Home,
    Carried,
    Dropped
}

internal static class CaptureTheFlagRules
{
    internal const int CapturePoints = 25;
    internal const float MatchDurationMultiplier = 2f;

    internal static Dictionary<int, int> AssignStrictTwoTeams(IReadOnlyList<int> playerIds)
    {
        Dictionary<int, int> assignments = new();
        foreach (int playerId in playerIds.Where(id => id >= 0).Distinct().OrderBy(id => id))
        {
            assignments[playerId] = assignments.Count % 2;
        }

        return assignments;
    }

    internal static int GetSpawnCandidateCount(int authoredSpawnCount)
    {
        return Math.Max(1, authoredSpawnCount / 3);
    }

    internal static float GetMatchDuration(int pointsToWin)
    {
        return Math.Max(1f, pointsToWin * MatchDurationMultiplier);
    }

    internal static int FindNearestTeam(TeamPoint position, IReadOnlyList<TeamPoint> origins)
    {
        int selectedTeam = 0;
        float selectedDistance = float.MaxValue;
        for (int teamId = 0; teamId < 2 && teamId < origins.Count; teamId++)
        {
            float distance = position.HorizontalDistanceSquared(origins[teamId]);
            if (distance < selectedDistance)
            {
                selectedDistance = distance;
                selectedTeam = teamId;
            }
        }

        return selectedTeam;
    }

    internal static bool TryPickup(CaptureTheFlagFlagStatus status, bool isEnemyFlag,
        out CaptureTheFlagFlagStatus nextStatus)
    {
        if (!isEnemyFlag || status == CaptureTheFlagFlagStatus.Carried)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = CaptureTheFlagFlagStatus.Carried;
        return true;
    }

    internal static bool TryReturn(CaptureTheFlagFlagStatus status, bool isOwnFlag,
        out CaptureTheFlagFlagStatus nextStatus)
    {
        if (!isOwnFlag || status != CaptureTheFlagFlagStatus.Dropped)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = CaptureTheFlagFlagStatus.Home;
        return true;
    }

    internal static bool TryDrop(CaptureTheFlagFlagStatus status,
        out CaptureTheFlagFlagStatus nextStatus)
    {
        if (status != CaptureTheFlagFlagStatus.Carried)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = CaptureTheFlagFlagStatus.Dropped;
        return true;
    }

    internal static bool TryAwardCapture(Dictionary<int, int> scores, int teamId,
        int pointsToWin, bool carriesEnemyFlag, bool ownFlagIsHome, out int winningTeamId)
    {
        winningTeamId = -1;
        if (!carriesEnemyFlag || !ownFlagIsHome || teamId < 0 || pointsToWin <= 0)
        {
            return false;
        }

        scores.TryGetValue(teamId, out int score);
        scores[teamId] = Math.Min(pointsToWin, score + CapturePoints);
        if (scores[teamId] >= pointsToWin)
        {
            winningTeamId = teamId;
        }

        return true;
    }

    internal static bool TryResolveTimeoutWinner(IReadOnlyDictionary<int, int> scores,
        out int winningTeamId)
    {
        scores.TryGetValue(0, out int teamZeroScore);
        scores.TryGetValue(1, out int teamOneScore);
        if (teamZeroScore == teamOneScore)
        {
            winningTeamId = -1;
            return false;
        }

        winningTeamId = teamZeroScore > teamOneScore ? 0 : 1;
        return true;
    }
}
