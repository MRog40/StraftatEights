using System.Collections.Generic;

namespace Eights;

internal static class HuntModesRules
{
    internal const float TieBreakDurationSeconds = 5f;
    internal const int PointsPerTakeWin = 40;

    internal static int GetOriginIndex(int teamId, int takeId)
    {
        int takeOffset = takeId <= 1 ? 0 : (takeId - 1) % 2;
        return (teamId + takeOffset) % 2;
    }

    internal static bool TryGetTeamWipeWinner(IReadOnlyCollection<int> alivePlayers,
        IReadOnlyDictionary<int, int> assignments, out int winningTeamId)
    {
        bool teamZeroAlive = HasAliveMember(alivePlayers, assignments, 0);
        bool teamOneAlive = HasAliveMember(alivePlayers, assignments, 1);
        if (teamZeroAlive == teamOneAlive)
        {
            winningTeamId = -1;
            return false;
        }

        winningTeamId = teamZeroAlive ? 0 : 1;
        return true;
    }

    internal static float AdvanceTieBreakHold(float elapsed, int previousController,
        int controller, ref float progress)
    {
        if (controller < 0 || controller != previousController)
        {
            progress = 0f;
        }

        progress += elapsed;
        return progress;
    }

    internal static int AddTakePoints(int currentScore, int pointsToWin)
    {
        return ScoreRules.AddPoints(currentScore, PointsPerTakeWin, pointsToWin);
    }

    private static bool HasAliveMember(IReadOnlyCollection<int> alivePlayers,
        IReadOnlyDictionary<int, int> assignments, int teamId)
    {
        foreach (int playerId in alivePlayers)
        {
            if (assignments.TryGetValue(playerId, out int assignedTeam)
                && assignedTeam == teamId)
            {
                return true;
            }
        }

        return false;
    }
}
