using System;
using System.Collections.Generic;
using System.Linq;

namespace Eights;

internal enum CapturetatFlagStatus
{
    Home,
    Carried,
    Dropped
}

internal static class CapturetatRules
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

    internal static bool TryPickup(CapturetatFlagStatus status, bool isEnemyFlag,
        out CapturetatFlagStatus nextStatus)
    {
        if (!isEnemyFlag || status == CapturetatFlagStatus.Carried)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = CapturetatFlagStatus.Carried;
        return true;
    }

    internal static bool TryReturn(CapturetatFlagStatus status, bool isOwnFlag,
        out CapturetatFlagStatus nextStatus)
    {
        if (!isOwnFlag || status != CapturetatFlagStatus.Dropped)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = CapturetatFlagStatus.Home;
        return true;
    }

    internal static bool TryDrop(CapturetatFlagStatus status,
        out CapturetatFlagStatus nextStatus)
    {
        if (status != CapturetatFlagStatus.Carried)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = CapturetatFlagStatus.Dropped;
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
        scores[teamId] = ScoreRules.AddPoints(score, CapturePoints, pointsToWin);
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
