using System;
using System.Collections.Generic;

namespace Eights;

internal static class DistributionRandom
{
    private const int MissingSelectionWeight = 3;
    private static readonly System.Random Random = new();
    private static readonly Dictionary<string, Dictionary<int, int>> SelectionCounts = new(
        StringComparer.Ordinal);
    private static readonly Dictionary<string, Dictionary<GameMode, int>> ModeSelectionCounts =
        new(StringComparer.Ordinal);

    internal static int SelectPlayer(string category, IReadOnlyList<int> candidates)
    {
        List<int> distinctCandidates = new();
        HashSet<int> seenCandidates = new();
        foreach (int playerId in candidates)
        {
            if (playerId >= 0 && seenCandidates.Add(playerId))
            {
                distinctCandidates.Add(playerId);
            }
        }

        if (distinctCandidates.Count == 0)
        {
            return -1;
        }
        if (distinctCandidates.Count == 1)
        {
            RecordSelection(category, distinctCandidates[0]);
            return distinctCandidates[0];
        }

        if (!SelectionCounts.TryGetValue(category, out Dictionary<int, int>? counts))
        {
            counts = new Dictionary<int, int>();
            SelectionCounts[category] = counts;
        }

        int highestCount = 0;
        foreach (int playerId in distinctCandidates)
        {
            if (counts.TryGetValue(playerId, out int count) && count > highestCount)
            {
                highestCount = count;
            }
        }

        int totalWeight = 0;
        foreach (int playerId in distinctCandidates)
        {
            counts.TryGetValue(playerId, out int count);
            totalWeight += 1 + (highestCount - count) * MissingSelectionWeight;
        }

        int selectedWeight = Random.Next(totalWeight);
        foreach (int playerId in distinctCandidates)
        {
            counts.TryGetValue(playerId, out int count);
            int weight = 1 + (highestCount - count) * MissingSelectionWeight;
            if (selectedWeight < weight)
            {
                counts[playerId] = count + 1;
                return playerId;
            }

            selectedWeight -= weight;
        }

        int fallbackPlayerId = distinctCandidates[distinctCandidates.Count - 1];
        counts.TryGetValue(fallbackPlayerId, out int fallbackCount);
        counts[fallbackPlayerId] = fallbackCount + 1;
        return fallbackPlayerId;
    }

    internal static void ResetForLobby()
    {
        SelectionCounts.Clear();
        ModeSelectionCounts.Clear();
    }

    internal static GameMode SelectMode(string category, IReadOnlyList<GameMode> candidates)
    {
        if (candidates.Count == 0)
        {
            return GameMode.None;
        }
        if (candidates.Count == 1)
        {
            RecordModeSelection(category, candidates[0]);
            return candidates[0];
        }

        if (!ModeSelectionCounts.TryGetValue(category, out Dictionary<GameMode, int>? counts))
        {
            counts = new Dictionary<GameMode, int>();
            ModeSelectionCounts[category] = counts;
        }

        int highestCount = 0;
        foreach (GameMode mode in candidates)
        {
            if (counts.TryGetValue(mode, out int count) && count > highestCount)
            {
                highestCount = count;
            }
        }

        int totalWeight = 0;
        foreach (GameMode mode in candidates)
        {
            counts.TryGetValue(mode, out int count);
            totalWeight += 1 + (highestCount - count) * MissingSelectionWeight;
        }

        int selectedWeight = Random.Next(totalWeight);
        foreach (GameMode mode in candidates)
        {
            counts.TryGetValue(mode, out int count);
            int weight = 1 + (highestCount - count) * MissingSelectionWeight;
            if (selectedWeight < weight)
            {
                counts[mode] = count + 1;
                return mode;
            }

            selectedWeight -= weight;
        }

        GameMode fallbackMode = candidates[candidates.Count - 1];
        RecordModeSelection(category, fallbackMode);
        return fallbackMode;
    }

    private static void RecordSelection(string category, int playerId)
    {
        if (!SelectionCounts.TryGetValue(category, out Dictionary<int, int>? counts))
        {
            counts = new Dictionary<int, int>();
            SelectionCounts[category] = counts;
        }

        counts.TryGetValue(playerId, out int count);
        counts[playerId] = count + 1;
    }

    private static void RecordModeSelection(string category, GameMode mode)
    {
        if (!ModeSelectionCounts.TryGetValue(category, out Dictionary<GameMode, int>? counts))
        {
            counts = new Dictionary<GameMode, int>();
            ModeSelectionCounts[category] = counts;
        }

        counts.TryGetValue(mode, out int count);
        counts[mode] = count + 1;
    }
}
