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

    internal static Dictionary<int, int> AssignBalanced(IReadOnlyList<int> playerIds,
        Random random, IReadOnlyDictionary<int, int>? previousAssignments)
    {
        return AssignDistributedBalanced(playerIds, GetTeamCount(playerIds.Count), random,
            previousAssignments);
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

    internal static Dictionary<int, int> AssignTwoTeams(IReadOnlyList<int> playerIds,
        Random random, IReadOnlyDictionary<int, int>? previousAssignments)
    {
        return AssignDistributedBalanced(playerIds, 2, random, previousAssignments);
    }

    internal static int ResolveTeamId(IReadOnlyDictionary<int, int> assignments, int playerId)
    {
        return assignments.TryGetValue(playerId, out int teamId) ? teamId : playerId;
    }

    internal static Dictionary<int, int> AssignHardtatBalanced(IReadOnlyList<int> playerIds)
    {
        return AssignHardtatBalanced(playerIds, null);
    }

    internal static Dictionary<int, int> AssignHardtatBalanced(IReadOnlyList<int> playerIds,
        Random? random)
    {
        int teamCount = GetHardtatTeamCount(playerIds.Count);
        if (teamCount == 0)
        {
            return new Dictionary<int, int>();
        }

        if (random == null)
        {
            Dictionary<int, int> assignments = new();
            List<int> orderedPlayerIds = playerIds.Where(id => id >= 0).Distinct().OrderBy(id => id)
                .ToList();
            foreach (int playerId in orderedPlayerIds)
            {
                assignments[playerId] = assignments.Count % teamCount;
            }

            return assignments;
        }

        return AssignDistributedBalanced(playerIds, teamCount, random, null);
    }

    internal static Dictionary<int, int> AssignHardtatBalanced(IReadOnlyList<int> playerIds,
        Random random, IReadOnlyDictionary<int, int>? previousAssignments)
    {
        return AssignDistributedBalanced(playerIds, GetHardtatTeamCount(playerIds.Count), random,
            previousAssignments);
    }

    private static Dictionary<int, int> AssignDistributedBalanced(
        IReadOnlyList<int> playerIds, int teamCount, Random random,
        IReadOnlyDictionary<int, int>? previousAssignments)
    {
        Dictionary<int, int> emptyAssignments = new();
        if (teamCount == 0)
        {
            return emptyAssignments;
        }

        List<int> validPlayerIds = playerIds.Where(id => id >= 0).Distinct().ToList();
        if (validPlayerIds.Count == 0)
        {
            return emptyAssignments;
        }

        const int candidateCount = 24;
        List<Dictionary<int, int>> candidates = new(candidateCount);
        List<int> candidateWeights = new(candidateCount);
        int totalWeight = 0;
        for (int attempt = 0; attempt < candidateCount; attempt++)
        {
            Shuffle(validPlayerIds, random);
            Dictionary<int, int> candidate = new();
            for (int index = 0; index < validPlayerIds.Count; index++)
            {
                candidate[validPlayerIds[index]] = index % teamCount;
            }

            int changedPlayers = CountChangedPlayers(candidate, previousAssignments);
            int weight = 1 + changedPlayers * 3;
            candidates.Add(new Dictionary<int, int>(candidate));
            candidateWeights.Add(weight);
            totalWeight += weight;
        }

        int selectedWeight = random.Next(totalWeight);
        for (int index = 0; index < candidates.Count; index++)
        {
            if (selectedWeight < candidateWeights[index])
            {
                return candidates[index];
            }

            selectedWeight -= candidateWeights[index];
        }

        return candidates[candidates.Count - 1];
    }

    private static int CountChangedPlayers(IReadOnlyDictionary<int, int> assignments,
        IReadOnlyDictionary<int, int>? previousAssignments)
    {
        if (previousAssignments == null)
        {
            return 0;
        }

        int changedPlayers = 0;
        foreach (KeyValuePair<int, int> assignment in assignments)
        {
            if (!previousAssignments.TryGetValue(assignment.Key, out int previousTeamId)
                || previousTeamId != assignment.Value)
            {
                changedPlayers++;
            }
        }

        return changedPlayers;
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (int index = values.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }

    internal static int GetHardtatTeamCount(int playerCount)
    {
        if (playerCount <= 0)
        {
            return 0;
        }

        return playerCount >= 9 || (playerCount >= 3 && playerCount % 2 == 1) ? 3 : 2;
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
            VermillionTeamId => new TeamColorData(220, 38, 38),
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

    internal static TeamPoint SelectRandomizedFarthestFromEnemiesAndObjective(
        IReadOnlyList<TeamPoint> candidates, IReadOnlyList<TeamPoint> enemies,
        TeamPoint objective, float randomness, int randomIndex)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one spawn candidate is required.", nameof(candidates));
        }

        List<RankedCandidate> rankedCandidates = new(candidates.Count);
        for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++)
        {
            float nearestEnemyDistance = enemies.Count == 0
                ? 0f
                : (float)Math.Sqrt(GetNearestDistanceSquared(candidates[candidateIndex], enemies));
            float objectiveDistance = (float)Math.Sqrt(
                candidates[candidateIndex].HorizontalDistanceSquared(objective));
            float score = nearestEnemyDistance * 2f + objectiveDistance;
            int insertionIndex = rankedCandidates.Count;
            while (insertionIndex > 0
                && score > rankedCandidates[insertionIndex - 1].Distance)
            {
                insertionIndex--;
            }

            rankedCandidates.Insert(insertionIndex,
                new RankedCandidate(candidateIndex, score));
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