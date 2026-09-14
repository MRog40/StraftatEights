using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.SceneManagement;

namespace StraftatEightsPlugin;

internal static class ModeMapCatalog
{
    private static readonly IReadOnlyList<string> Barren01AltOnly =
        new[] { MapDefinitions.Barren01AltName };

    private static readonly IReadOnlyDictionary<GameMode, IReadOnlyList<string>> MapsByMode =
        new Dictionary<GameMode, IReadOnlyList<string>>
        {
            [GameMode.FreeForAll] = Barren01AltOnly,
            [GameMode.Juggernaut] = Barren01AltOnly,
            [GameMode.GunGame] = Barren01AltOnly,
            [GameMode.SniperBattle] = Barren01AltOnly,
            [GameMode.MichaelMeyers] = Barren01AltOnly,
            [GameMode.KillTheRat] = Barren01AltOnly,
            [GameMode.OneInTheChamber] = Barren01AltOnly,
            [GameMode.HotPotato] = Barren01AltOnly,
            [GameMode.Infidel] = Barren01AltOnly,
            [GameMode.HVT] = Barren01AltOnly,
            [GameMode.Assassin] = Barren01AltOnly
            ,[GameMode.Hardpoint] = Barren01AltOnly
        };

    internal static IReadOnlyList<string> GetMapNames(GameMode mode)
    {
        if (mode == GameMode.Default)
        {
            return GetDefaultMapNames();
        }

        return MapsByMode.TryGetValue(mode, out IReadOnlyList<string>? mapNames)
            ? mapNames
            : Array.Empty<string>();
    }

        private static IReadOnlyList<string> GetDefaultMapNames()
        {
            HashSet<string> mapsUsedByOtherModes = new(StringComparer.Ordinal);
            foreach (IReadOnlyList<string> mapNames in MapsByMode.Values)
            {
                foreach (string mapName in mapNames)
                {
                    mapsUsedByOtherModes.Add(mapName);
                }
            }

            List<string> defaultMapNames = new();
            foreach (string mapName in GetAllGameMapNames())
            {
                if (!mapsUsedByOtherModes.Contains(mapName)
                    && !defaultMapNames.Contains(mapName, StringComparer.Ordinal))
                {
                    defaultMapNames.Add(mapName);
                }
            }

            return defaultMapNames;
        }

        private static IReadOnlyList<string> GetAllGameMapNames()
        {
            List<string> mapNames = new();
            if (MapsManager.Instance != null && MapsManager.Instance.allMaps != null)
            {
                foreach (Map map in MapsManager.Instance.allMaps)
                {
                    if (map != null && !string.IsNullOrWhiteSpace(map.mapName))
                    {
                        mapNames.Add(map.mapName);
                    }
                }

                return mapNames;
            }

            const int firstMapBuildIndex = 6;
            for (int buildIndex = firstMapBuildIndex;
                buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                string mapName = Path.GetFileNameWithoutExtension(scenePath);
                if (!string.IsNullOrWhiteSpace(mapName))
                {
                    mapNames.Add(mapName);
                }
            }

            return mapNames;
        }

        internal static bool IsSupported(GameMode mode, string mapName)
        {
            return !string.IsNullOrWhiteSpace(mapName)
                && GetMapNames(mode).Contains(mapName, StringComparer.Ordinal);
        }

    internal static bool TryGetDefinition(GameMode mode, string mapName,
        out MapDefinition definition)
    {
            if (!IsSupported(mode, mapName))
        {
            definition = null!;
            return false;
        }

        return MapDefinitions.TryGet(mapName, out definition!);
    }
}