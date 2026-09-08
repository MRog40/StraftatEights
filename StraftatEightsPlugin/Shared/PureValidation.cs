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

    internal static Dictionary<int, int> ParseSigned(string data, int maximumScore)
    {
        Dictionary<int, int> scores = new();
        foreach (string entry in (data ?? string.Empty).Split(';'))
        {
            int separator = entry.IndexOf(':');
            if (separator > 0 && int.TryParse(entry.Substring(0, separator), out int id)
                && int.TryParse(entry.Substring(separator + 1), out int score)
                && id >= 0 && score <= maximumScore)
            {
                scores[id] = score;
            }
        }
        return scores;
    }
}

internal static class OneInTheChamberRules
{
    internal const int PlayerHealth = 10;

    internal static bool IsAllowedWeapon(string weaponName)
    {
        return weaponName.StartsWith("Pistol", StringComparison.Ordinal)
            || weaponName.StartsWith("Couperet", StringComparison.Ordinal);
    }

    internal static bool ApplyDeath(HashSet<int> alivePlayers, Dictionary<int, int> reserveBullets,
        int deadPlayerId, int killerId)
    {
        if (!alivePlayers.Remove(deadPlayerId))
        {
            return false;
        }

        if (killerId >= 0 && killerId != deadPlayerId && alivePlayers.Contains(killerId))
        {
            reserveBullets.TryGetValue(killerId, out int bullets);
            reserveBullets[killerId] = bullets + 1;
        }

        return true;
    }
}

internal static class HotPotatoRules
{
    internal static bool IsAllowedWeapon(string weaponName, bool hasPotato)
    {
        string expected = hasPotato ? "BaseballBat" : "Shotgun";
        return weaponName.StartsWith(expected, StringComparison.Ordinal);
    }

    internal static int ResolvePotato(int potatoPlayerId, int killerId, int deadPlayerId)
    {
        return killerId == potatoPlayerId && killerId != deadPlayerId
            ? deadPlayerId
            : potatoPlayerId;
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

    internal static bool TrySelectRandom<T>(IReadOnlyList<T> modes, T current, int selectionIndex, out T next)
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

        int candidateCount = currentIndex < 0 ? modes.Count : modes.Count - 1;
        if (candidateCount == 0)
        {
            next = modes[0];
            return true;
        }

        int candidateIndex = (int)((uint)selectionIndex % (uint)candidateCount);
        if (currentIndex >= 0 && candidateIndex >= currentIndex)
        {
            candidateIndex++;
        }
        next = modes[candidateIndex];
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
