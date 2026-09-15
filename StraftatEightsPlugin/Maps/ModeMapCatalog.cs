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
            ,[GameMode.Hardpoint] = Barren01AltOnly,
            [GameMode.CaptureTheFlag] = Barren01AltOnly,
            [GameMode.SearchAndDestroy] = Barren01AltOnly
        };

    internal static IReadOnlyList<string> GetMapNames(GameMode mode)
    {
        return GetMapNames(mode, true);
    }

    internal static IReadOnlyList<string> GetMapNames(GameMode mode, bool mapOverridesEnabled)
    {
        if (!mapOverridesEnabled)
        {
            IReadOnlyList<string> mapNames = GetNormalLobbyMapNames();
            return RequiresMapDefinition(mode)
                ? GetMapsWithRequiredDefinition(mode, mapNames)
                : mapNames;
        }

        return GetOverrideMapNames(mode);
    }

    private static IReadOnlyList<string> GetOverrideMapNames(GameMode mode)
    {
        if (mode == GameMode.Default)
        {
            return GetDefaultMapNames();
        }

        return MapsByMode.TryGetValue(mode, out IReadOnlyList<string>? mapNames)
            ? mapNames
            : Array.Empty<string>();
    }

    private static IReadOnlyList<string> GetNormalLobbyMapNames()
    {
        if (SceneMotor.Instance != null && SceneMotor.Instance.PlayListMaps.Count > 0)
        {
            return GetDistinctMapNames(SceneMotor.Instance.PlayListMaps);
        }

        if (MapsManager.Instance == null || MapsManager.Instance.allMaps == null
            || MapsManager.Instance.unlockedMaps == null)
        {
            return Array.Empty<string>();
        }

        List<string> mapNames = new();
        foreach (int mapIndex in MapsManager.Instance.unlockedMaps)
        {
            if (mapIndex < 0 || mapIndex >= MapsManager.Instance.allMaps.Length)
            {
                continue;
            }

            Map map = MapsManager.Instance.allMaps[mapIndex];
            if (map != null && !string.IsNullOrWhiteSpace(map.mapName)
                && !mapNames.Contains(map.mapName, StringComparer.Ordinal))
            {
                mapNames.Add(map.mapName);
            }
        }

        return mapNames;
    }

    private static IReadOnlyList<string> GetMapsWithRequiredDefinition(GameMode mode,
        IReadOnlyList<string> mapNames)
    {
        List<string> supportedMaps = new();
        foreach (string mapName in mapNames)
        {
            if (HasRequiredDefinition(mode, mapName))
            {
                supportedMaps.Add(mapName);
            }
        }

        return supportedMaps;
    }

    private static List<string> GetDistinctMapNames(IReadOnlyList<string> mapNames)
    {
        List<string> distinctMapNames = new();
        foreach (string mapName in mapNames)
        {
            if (!string.IsNullOrWhiteSpace(mapName)
                && !distinctMapNames.Contains(mapName, StringComparer.Ordinal))
            {
                distinctMapNames.Add(mapName);
            }
        }

        return distinctMapNames;
    }

    private static bool RequiresMapDefinition(GameMode mode)
    {
        return mode == GameMode.Hardpoint || mode == GameMode.CaptureTheFlag
            || mode == GameMode.SearchAndDestroy;
    }

    private static bool HasRequiredDefinition(GameMode mode, string mapName)
    {
        if (!RequiresMapDefinition(mode)
            || !MapDefinitions.TryGet(mapName, out MapDefinition definition))
        {
            return !RequiresMapDefinition(mode);
        }

        return mode switch
        {
            GameMode.Hardpoint => definition.HardpointObjectives.Count > 0,
            GameMode.CaptureTheFlag => definition.CaptureTheFlagObjectives.Count == 2,
            GameMode.SearchAndDestroy => definition.SndObjectives.Count == 2,
            _ => true
        };
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
            return IsSupported(mode, mapName, true);
        }

        internal static bool IsSupported(GameMode mode, string mapName, bool mapOverridesEnabled)
        {
            if (string.IsNullOrWhiteSpace(mapName))
            {
                return false;
            }

            return mapOverridesEnabled
                ? GetOverrideMapNames(mode).Contains(mapName, StringComparer.Ordinal)
                : !RequiresMapDefinition(mode) || HasRequiredDefinition(mode, mapName);
        }

    internal static bool TryGetDefinition(GameMode mode, string mapName,
        out MapDefinition definition)
        {
            return TryGetDefinition(mode, mapName, true, out definition);
        }

        internal static bool TryGetDefinition(GameMode mode, string mapName,
            bool mapOverridesEnabled, out MapDefinition definition)
    {
            if (!IsSupported(mode, mapName, mapOverridesEnabled))
        {
            definition = null!;
            return false;
        }

        return MapDefinitions.TryGet(mapName, out definition!);
    }
}