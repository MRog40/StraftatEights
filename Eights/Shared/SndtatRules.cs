using System.Collections.Generic;
using System.Linq;

namespace Eights;

internal enum SndtatBombStatus
{
    Home,
    Carried,
    Dropped,
    Planted
}

internal static class SndtatRules
{
    internal const int PointsPerRoundWin = 40;
    internal const float PlantDurationSeconds = 5f;
    internal const float DefuseDurationSeconds = 7.5f;
    internal const float FuseDurationSeconds = 30f;
    internal const float InteractionRadius = 2f;
    internal const float PlantSiteRadius = 3f;
    internal const float PlantSiteMinVerticalOffset = -1f;
    internal const float PlantSiteMaxVerticalOffset = 2f;
    internal const float BetweenTakeDelaySeconds = 3f;
    internal const float TakeTimeLimitSeconds = ModeTimeoutRules.DefaultRoundSeconds;

    internal static Dictionary<int, int> AssignStrictTwoTeams(IReadOnlyList<int> playerIds)
    {
        Dictionary<int, int> assignments = new();
        foreach (int playerId in playerIds.Where(id => id >= 0).Distinct().OrderBy(id => id))
        {
            assignments[playerId] = assignments.Count % 2;
        }

        return assignments;
    }

    internal static int GetOffensiveTeamId(int takeId)
    {
        return takeId % 2 == 1 ? 0 : 1;
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

    internal static bool CanResolveTeamWipe(SndtatBombStatus status,
        int wipedTeamId, int defensiveTeamId)
    {
        return status != SndtatBombStatus.Planted
            || wipedTeamId == defensiveTeamId;
    }

    internal static bool TryRecoverBomb(SndtatBombStatus status, bool isOffense,
        bool inRange, out SndtatBombStatus nextStatus)
    {
        if (status != SndtatBombStatus.Dropped || !isOffense || !inRange)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = SndtatBombStatus.Carried;
        return true;
    }

    internal static bool TryStartPlant(SndtatBombStatus status, bool isCarrier,
        bool inRange, out SndtatBombStatus nextStatus)
    {
        if (status != SndtatBombStatus.Carried || !isCarrier || !inRange)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = status;
        return true;
    }

    internal static bool TryCompletePlant(SndtatBombStatus status, float progress,
        out SndtatBombStatus nextStatus)
    {
        if (status != SndtatBombStatus.Carried || progress < PlantDurationSeconds)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = SndtatBombStatus.Planted;
        return true;
    }

    internal static bool TryStartDefuse(SndtatBombStatus status, bool isDefense,
        bool inRange, out SndtatBombStatus nextStatus)
    {
        if (status != SndtatBombStatus.Planted || !isDefense || !inRange)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = status;
        return true;
    }

    internal static bool TryCompleteDefuse(SndtatBombStatus status, float progress,
        out SndtatBombStatus nextStatus)
    {
        if (status != SndtatBombStatus.Planted || progress < DefuseDurationSeconds)
        {
            nextStatus = status;
            return false;
        }

        nextStatus = SndtatBombStatus.Home;
        return true;
    }
}
