using System;
using System.Collections.Generic;

namespace Eights;

internal static class ModeTimeoutRules
{
    internal const float DefaultRoundSeconds = 90f;
    internal const float LongRoundSeconds = 250f;

    internal static bool TryGetUniqueLeader(IReadOnlyDictionary<int, int> scores,
        out int leaderId)
    {
        leaderId = -1;
        int highestScore = int.MinValue;
        bool tied = false;
        foreach (KeyValuePair<int, int> entry in scores)
        {
            if (entry.Value > highestScore)
            {
                highestScore = entry.Value;
                leaderId = entry.Key;
                tied = false;
            }
            else if (entry.Value == highestScore)
            {
                tied = true;
            }
        }

        return leaderId >= 0 && !tied;
    }

    internal static bool TryGetUniqueLeadingTeam(IReadOnlyDictionary<int, int> scores,
        Func<int, int> getTeamId, out int leadingTeamId)
    {
        leadingTeamId = -1;
        int highestScore = int.MinValue;
        HashSet<int> leadingTeams = new();
        foreach (KeyValuePair<int, int> entry in scores)
        {
            int teamId = getTeamId(entry.Key);
            if (entry.Value > highestScore)
            {
                highestScore = entry.Value;
                leadingTeams.Clear();
                leadingTeams.Add(teamId);
            }
            else if (entry.Value == highestScore)
            {
                leadingTeams.Add(teamId);
            }
        }

        if (leadingTeams.Count != 1)
        {
            return false;
        }

        foreach (int teamId in leadingTeams)
        {
            leadingTeamId = teamId;
            break;
        }
        return true;
    }
}