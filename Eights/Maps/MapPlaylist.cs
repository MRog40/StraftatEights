using System;
using System.Collections.Generic;

namespace Eights;

internal readonly struct MapPlaylistEntry<TMode>
{
    internal TMode Mode { get; }
    internal string MapName { get; }

    internal MapPlaylistEntry(TMode mode, string mapName)
    {
        Mode = mode;
        MapName = mapName;
    }
}

internal static class MapPlaylist
{
    internal static List<MapPlaylistEntry<TMode>> Build<TMode>(IReadOnlyList<TMode> modes,
        Func<TMode, IReadOnlyList<string>> mapNamesForMode, Random random)
    {
        List<MapPlaylistEntry<TMode>> playlist = new();
        HashSet<TMode> seenModes = new();

        foreach (TMode mode in modes)
        {
            if (!seenModes.Add(mode))
            {
                continue;
            }

            List<string> mapNames = GetDistinctMapNames(mapNamesForMode(mode));
            if (mapNames.Count == 0)
            {
                continue;
            }

            Shuffle(mapNames, random);
            playlist.Add(new MapPlaylistEntry<TMode>(mode, mapNames[0]));
        }

        Shuffle(playlist, random);
        return playlist;
    }

    internal static string SelectNextMap(IReadOnlyList<string> mapNames, string previousMap,
        Random random)
    {
        return SelectNextMap(mapNames, previousMap, random, null);
    }

    internal static string SelectNextMap(IReadOnlyList<string> mapNames, string previousMap,
        Random random, IReadOnlyCollection<string>? recentMaps)
    {
        List<string> distinctMapNames = GetDistinctMapNames(mapNames);
        if (distinctMapNames.Count == 0)
        {
            return string.Empty;
        }

        if (distinctMapNames.Count == 1)
        {
            return distinctMapNames[random.Next(distinctMapNames.Count)];
        }

        List<string> availableMapNames = new(distinctMapNames);
        if (recentMaps != null)
        {
            HashSet<string> recentMapSet = new(recentMaps, StringComparer.Ordinal);
            availableMapNames.RemoveAll(recentMapSet.Contains);
        }
        else if (!string.IsNullOrEmpty(previousMap))
        {
            availableMapNames.Remove(previousMap);
        }

        if (availableMapNames.Count == 0)
        {
            availableMapNames = distinctMapNames;
            if (!string.IsNullOrEmpty(previousMap))
            {
                availableMapNames.Remove(previousMap);
            }
        }

        return availableMapNames[random.Next(availableMapNames.Count)];
    }

    private static List<string> GetDistinctMapNames(IReadOnlyList<string> mapNames)
    {
        List<string> distinctMapNames = new();
        if (mapNames == null)
        {
            return distinctMapNames;
        }

        HashSet<string> seenNames = new(StringComparer.Ordinal);
        foreach (string mapName in mapNames)
        {
            if (!string.IsNullOrWhiteSpace(mapName) && seenNames.Add(mapName))
            {
                distinctMapNames.Add(mapName);
            }
        }

        return distinctMapNames;
    }

    private static void Shuffle<T>(IList<T> values, Random random)
    {
        for (int index = values.Count - 1; index > 0; index--)
        {
            int swapIndex = random.Next(index + 1);
            (values[index], values[swapIndex]) = (values[swapIndex], values[index]);
        }
    }
}