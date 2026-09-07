using System;
using System.Collections.Generic;
using System.Linq;

namespace StraftatEightsPlugin;

internal static class SnapshotValidation
{
    internal static bool TryAccept(int roundId, int revision, ref int lastRoundId, ref int lastRevision)
    {
        if (roundId < 0 || revision < 0
            || roundId < lastRoundId || (roundId == lastRoundId && revision < lastRevision))
        {
            return false;
        }

        lastRoundId = roundId;
        lastRevision = revision;
        return true;
    }
}

internal static class WeaponListParser
{
    internal static List<string> Parse(string value, IEnumerable<string> validNames)
    {
        HashSet<string> valid = new(validNames, StringComparer.Ordinal);
        return (value ?? string.Empty).Split(',', ';')
            .Select(item => item.Trim())
            .Where(item => item.Length > 0 && valid.Contains(item))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}

internal static class ScoreCodec
{
    internal static string Serialize(IReadOnlyDictionary<int, int> scores)
    {
        return string.Join(";", scores.Select(entry => $"{entry.Key}:{entry.Value}"));
    }

    internal static Dictionary<int, int> Parse(string data, int maximumScore)
    {
        Dictionary<int, int> scores = new();
        foreach (string entry in (data ?? string.Empty).Split(';'))
        {
            int separator = entry.IndexOf(':');
            if (separator > 0 && int.TryParse(entry.Substring(0, separator), out int id)
                && int.TryParse(entry.Substring(separator + 1), out int score)
                && id >= 0 && score >= 0 && score <= maximumScore)
            {
                scores[id] = score;
            }
        }
        return scores;
    }
}

internal static class ModeCycle
{
    internal static bool TrySelectNext<T>(IReadOnlyList<T> modes, T current, out T next)
    {
        if (modes.Count == 0)
        {
            next = default!;
            return false;
        }

        int currentIndex = -1;
        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int index = 0; index < modes.Count; index++)
        {
            if (comparer.Equals(modes[index], current))
            {
                currentIndex = index;
                break;
            }
        }

        next = modes[(currentIndex + 1) % modes.Count];
        return true;
    }
}

internal sealed class RequestVersionTracker
{
    private readonly Dictionary<int, int> versions = new();

    internal int Next(int playerId)
    {
        int nextVersion = versions.TryGetValue(playerId, out int currentVersion)
            ? currentVersion + 1
            : 1;
        versions[playerId] = nextVersion;
        return nextVersion;
    }

    internal bool IsCurrent(int playerId, int version)
    {
        return versions.TryGetValue(playerId, out int currentVersion)
            && currentVersion == version;
    }

    internal void Clear()
    {
        versions.Clear();
    }
}
