using System;
using System.Collections.Generic;
using System.Linq;

namespace StraftatEightsPlugin;

internal enum SearchAndDestroyBombStatus
{
    Home,
    Carried,
    Dropped,
    Planted
}

internal static class SearchAndDestroyRules
{
    internal const int PointsPerRoundWin = 40;
    internal const float PlantDurationSeconds = 5f;
    internal const float DefuseDurationSeconds = 7.5f;
    internal const float FuseDurationSeconds = 30f;
    internal const float InteractionRadius = 2f;
    internal const float PlantSiteRadius = 3f;
    internal const float BetweenSubRoundDelaySeconds = 3f;

    internal static float GetSubRoundTimeLimit(int scoreLimit)
    {
        return Math.Max(1, scoreLimit);
    }

    internal static Dictionary<int, int> AssignStrictTwoTeams(IReadOnlyList<int> playerIds)
    {
        Dictionary<int, int> assignments = new();
        foreach (int playerId in playerIds.Where(id => id >= 0).Distinct().OrderBy(id => id))
        {
            assignments[playerId] = assignments.Count % 2;
        }

        return assignments;
    }

    internal static int GetOffensiveTeamId(int subRoundId)
    {
        return subRoundId % 2 == 1 ? 0 : 1;
    }

    internal static int GetOtherTeamId(int teamId)
    {
        return teamId == 0 ? 1 : teamId == 1 ? 0 : -1;
    }

    internal static bool IsMatchWon(int score, int pointsToWin)
    {
        return score >= pointsToWin;
    }

    internal static bool IsTeamWiped(IReadOnlyCollection<int> alivePlayers,
        IReadOnlyDictionary<int, int> assignments, int teamId)
    {
        foreach (int playerId in alivePlayers)
        {
            if (assignments.TryGetValue(playerId, out int assignedTeamId)
                && assignedTeamId == teamId)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool CanResolveTeamWipe(SearchAndDestroyBombStatus status)
    {
        return status != SearchAndDestroyBombStatus.Planted;
    }

    internal static bool TryRecoverBomb(SearchAndDestroyBombStatus status, bool isOffense,
        bool inRange, out SearchAndDestroyBombStatus nextStatus)
    {
        if (status != SearchAndDestroyBombStatus.Dropped || !isOffense || !inRange)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = SearchAndDestroyBombStatus.Carried;
        return true;
    }

    internal static bool TryStartPlant(SearchAndDestroyBombStatus status, bool isCarrier,
        bool inRange, out SearchAndDestroyBombStatus nextStatus)
    {
        if (status != SearchAndDestroyBombStatus.Carried || !isCarrier || !inRange)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = status;
        return true;
    }

    internal static bool TryCompletePlant(SearchAndDestroyBombStatus status, float progress,
        out SearchAndDestroyBombStatus nextStatus)
    {
        if (status != SearchAndDestroyBombStatus.Carried || progress < PlantDurationSeconds)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = SearchAndDestroyBombStatus.Planted;
        return true;
    }

    internal static bool TryStartDefuse(SearchAndDestroyBombStatus status, bool isDefense,
        bool inRange, out SearchAndDestroyBombStatus nextStatus)
    {
        if (status != SearchAndDestroyBombStatus.Planted || !isDefense || !inRange)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = status;
        return true;
    }

    internal static bool TryCompleteDefuse(SearchAndDestroyBombStatus status, float progress,
        out SearchAndDestroyBombStatus nextStatus)
    {
        if (status != SearchAndDestroyBombStatus.Planted || progress < DefuseDurationSeconds)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = SearchAndDestroyBombStatus.Home;
        return true;
    }
}
