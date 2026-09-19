using System;
using System.Collections.Generic;
using System.Linq;

namespace Eights;

internal readonly struct TeamColorData : IEquatable<TeamColorData>
{
    internal TeamColorData(byte red, byte green, byte blue)
    {
        Red = red;
        Green = green;
        Blue = blue;
    }

    internal byte Red { get; }
    internal byte Green { get; }
    internal byte Blue { get; }

    public bool Equals(TeamColorData other)
    {
        return Red == other.Red && Green == other.Green && Blue == other.Blue;
    }

    public override bool Equals(object? obj)
    {
        return obj is TeamColorData other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Red, Green, Blue);
    }
}

internal readonly struct TeamPoint
{
    internal TeamPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    internal float X { get; }
    internal float Y { get; }
    internal float Z { get; }

    internal float HorizontalDistanceSquared(TeamPoint other)
    {
        float x = X - other.X;
        float z = Z - other.Z;
        return x * x + z * z;
    }
}

internal static class TeamRules
{
    internal const int BlueTeamId = 0;
    internal const int VermillionTeamId = 1;
    internal const int GreenTeamId = 2;

    private readonly struct RankedCandidate
    {
        internal RankedCandidate(int index, float distance)
        {
            Index = index;
            Distance = distance;
        }

        internal int Index { get; }
        internal float Distance { get; }
    }

    internal static int GetTeamCount(int playerCount)
    {
        if (playerCount <= 0)
        {
            return 0;
        }

        return playerCount >= 3 && playerCount % 3 == 0 ? 3 : 2;
    }

    internal static Dictionary<int, int> AssignBalanced(IReadOnlyList<int> playerIds)
    {
        Dictionary<int, int> assignments = new();
        int teamCount = GetTeamCount(playerIds.Count);
        if (teamCount == 0)
        {
            return assignments;
        }

        foreach (int playerId in playerIds.OrderBy(id => id))
        {
            if (playerId >= 0 && !assignments.ContainsKey(playerId))
            {
                assignments[playerId] = assignments.Count % teamCount;
            }
        }

        return assignments;
    }

    internal static Dictionary<int, int> AssignTwoTeams(IReadOnlyList<int> playerIds)
    {
        Dictionary<int, int> assignments = new();
        foreach (int playerId in playerIds.Where(id => id >= 0).Distinct().OrderBy(id => id))
        {
            assignments[playerId] = assignments.Count % 2;
        }

        return assignments;
    }

    internal static Dictionary<int, int> AssignHardpointBalanced(IReadOnlyList<int> playerIds)
    {
        Dictionary<int, int> assignments = new();
        int teamCount = GetHardpointTeamCount(playerIds.Count);
        if (teamCount == 0)
        {
            return assignments;
        }

        foreach (int playerId in playerIds.OrderBy(id => id))
        {
            if (playerId >= 0 && !assignments.ContainsKey(playerId))
            {
                assignments[playerId] = assignments.Count % teamCount;
            }
        }

        return assignments;
    }

    internal static int GetHardpointTeamCount(int playerCount)
    {
        if (playerCount <= 0)
        {
            return 0;
        }

        return playerCount >= 9 ? 3 : 2;
    }

    internal static float GetTeamHealthMultiplier(IReadOnlyDictionary<int, int> assignments,
        int playerId)
    {
        if (!assignments.TryGetValue(playerId, out int playerTeamId))
        {
            return 1f;
        }

        Dictionary<int, int> teamSizes = new();
        foreach (int teamId in assignments.Values)
        {
            teamSizes.TryGetValue(teamId, out int teamSize);
            teamSizes[teamId] = teamSize + 1;
        }

        int playerTeamSize = teamSizes[playerTeamId];
        int largestTeamSize = teamSizes.Values.Max();
        return (float)largestTeamSize / playerTeamSize;
    }

    internal static TeamColorData GetColor(int teamId)
    {
        return teamId switch
        {
            BlueTeamId => new TeamColorData(0, 114, 178),
            VermillionTeamId => new TeamColorData(213, 94, 0),
            GreenTeamId => new TeamColorData(0, 158, 115),
            _ => new TeamColorData(220, 220, 220)
        };
    }

    internal static int ResolveController(IEnumerable<int> teamsOnPoint)
    {
        int controller = -1;
        foreach (int teamId in teamsOnPoint.Distinct())
        {
            if (teamId < 0)
            {
                continue;
            }

            if (controller >= 0)
            {
                return -2;
            }

            controller = teamId;
        }

        return controller;
    }

    internal static TeamPoint SelectFarthestFromOrigins(IReadOnlyList<TeamPoint> candidates,
        IReadOnlyList<TeamPoint> origins)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one spawn candidate is required.", nameof(candidates));
        }

        TeamPoint best = candidates[0];
        float bestDistance = float.MinValue;
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            float nearestDistance = GetNearestDistanceSquared(candidates[candidateIndex], origins);
            if (nearestDistance > bestDistance)
            {
                best = candidates[candidateIndex];
                bestDistance = nearestDistance;
            }
        }

        return best;
    }

    internal static TeamPoint SelectFarthestFromEnemies(IReadOnlyList<TeamPoint> candidates,
        IReadOnlyList<TeamPoint> enemies)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one spawn candidate is required.", nameof(candidates));
        }

        if (enemies.Count == 0)
        {
            return candidates[0];
        }

        return SelectFarthestFromOrigins(candidates, enemies);
    }

    internal static TeamPoint SelectRandomizedFarthestFromEnemies(
        IReadOnlyList<TeamPoint> candidates, IReadOnlyList<TeamPoint> enemies,
        float randomness, int randomIndex)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one spawn candidate is required.", nameof(candidates));
        }

        if (enemies.Count == 0)
        {
            return candidates[0];
        }

        List<RankedCandidate> rankedCandidates = new(candidates.Count);
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            float distance = GetNearestDistanceSquared(candidates[candidateIndex], enemies);
            int insertionIndex = rankedCandidates.Count;
            while (insertionIndex > 0
                && distance > rankedCandidates[insertionIndex - 1].Distance)
            {
                insertionIndex--;
            }

            rankedCandidates.Insert(insertionIndex,
                new RankedCandidate(candidateIndex, distance));
        }

        float clampedRandomness = Math.Max(0f, Math.Min(1f, randomness));
        int poolSize = 1 + (int)Math.Floor((candidates.Count - 1) * clampedRandomness);
        int selectedRank = GetRandomIndex(randomIndex, poolSize);
        return candidates[rankedCandidates[selectedRank].Index];
    }

    private static int GetRandomIndex(int randomIndex, int count)
    {
        int normalizedIndex = randomIndex % count;
        return normalizedIndex < 0 ? normalizedIndex + count : normalizedIndex;
    }

    internal static string SerializeAssignments(IReadOnlyDictionary<int, int> assignments)
    {
        return string.Join(";", assignments.OrderBy(entry => entry.Key)
            .Select(entry => $"{entry.Key}:{entry.Value}"));
    }

    internal static Dictionary<int, int> ParseAssignments(string data, int teamCount)
    {
        Dictionary<int, int> assignments = new();
        if (teamCount <= 0)
        {
            return assignments;
        }

        foreach (string entry in (data ?? string.Empty).Split(';'))
        {
            int separator = entry.IndexOf(':');
            if (separator <= 0
                || !int.TryParse(entry.Substring(0, separator), out int playerId)
                || !int.TryParse(entry.Substring(separator + 1), out int teamId)
                || playerId < 0 || teamId < 0 || teamId >= teamCount)
            {
                continue;
            }

            assignments[playerId] = teamId;
        }

        return assignments;
    }

    private static float GetNearestDistanceSquared(TeamPoint candidate,
        IReadOnlyList<TeamPoint> origins)
    {
        if (origins.Count == 0)
        {
            return float.MaxValue;
        }

        float nearestDistance = float.MaxValue;
        foreach (TeamPoint origin in origins)
        {
            nearestDistance = Math.Min(nearestDistance,
                candidate.HorizontalDistanceSquared(origin));
        }

        return nearestDistance;
    }
}