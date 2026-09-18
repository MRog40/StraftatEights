using System;
using System.Collections.Generic;
using System.Linq;

namespace StraftatEightsPlugin;

internal static class HardpointRules
{
    internal const float NextObjectiveWarningSeconds = 10f;

    internal static int GetContestTimeLimit(int scoreLimit)
    {
        return Math.Max(1, (int)(scoreLimit * 1.2f));
    }

    internal static int GetNextObjectiveIndex(int currentIndex, int objectiveCount)
    {
        if (objectiveCount <= 0)
        {
            return 0;
        }

        return (currentIndex + 1) % objectiveCount;
    }

    internal static bool IsWarningActive(float elapsedSeconds, float objectiveDuration,
        float warningDuration)
    {
        return elapsedSeconds >= Math.Max(0f, objectiveDuration - warningDuration);
    }

    internal static bool TryAwardPoint(Dictionary<int, int> scores, int teamId, int scoreLimit,
        bool suddenDeath, out int winningTeamId)
    {
        winningTeamId = -1;
        if (teamId < 0 || scoreLimit <= 0)
        {
            return false;
        }

        if (suddenDeath)
        {
            winningTeamId = teamId;
            return true;
        }

        scores.TryGetValue(teamId, out int score);
        scores[teamId] = Math.Min(scoreLimit, score + 1);
        if (scores[teamId] >= scoreLimit)
        {
            winningTeamId = teamId;
            return true;
        }

        return false;
    }

    internal static bool TryResolveTimerWinner(IReadOnlyDictionary<int, int> scores,
        out int winningTeamId)
    {
        winningTeamId = -1;
        if (scores.Count == 0)
        {
            return false;
        }

        int highestScore = scores.Values.Max();
        List<int> leaders = scores.Where(entry => entry.Value == highestScore)
            .Select(entry => entry.Key).ToList();
        if (leaders.Count != 1)
        {
            return false;
        }

        winningTeamId = leaders[0];
        return true;
    }
}