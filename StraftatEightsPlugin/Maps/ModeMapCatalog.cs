using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.SceneManagement;

namespace StraftatEightsPlugin;

internal static class ModeMapCatalog
{
    private static readonly IReadOnlyList<string> FreeForAllMaps =
        new[] { "Chateaux_06_NoGrass", "Basket_Swirly", "Basket_Swirly_Alt", "Republic_08" };
    private static readonly IReadOnlyList<string> JuggernautMaps =
        new[] { "Arena_11_Alt", "Arena_10", "Arena_10_Alt", "Basket_Junglegym" };
    private static readonly IReadOnlyList<string> GunGameMaps = FreeForAllMaps;
    private static readonly IReadOnlyList<string> SniperBattleMaps =
        new[] { "Adobe_02", "Arena_Shadow_08", "Garden_01", "Chateaux_01" };
    private static readonly IReadOnlyList<string> MichaelMeyersMaps =
        new[]
        {
            "Bazaar_Alleyway_Alt", "WestVillage_04_Alt", "Adobe_01_Alt",
            "Arena_Shadow_04_Alt", "Basket_Junglegym_Alt", "Neo_Arena_03"
        };
    private static readonly IReadOnlyList<string> KillTheRatMaps =
        new[] { "WestVillage_04", "Toilets_00", "Arena_13", "JF_Poolrooms_01" };
    private static readonly IReadOnlyList<string> OneInTheChamberMaps =
        new[] { "TheSamePlace_02", "StLucia_01", "Chateaux_04" };
    private static readonly IReadOnlyList<string> HotPotatoMaps = FreeForAllMaps;
    private static readonly IReadOnlyList<string> InfidelMaps =
        new[] { "Bazaar_01", "TheSamePlace_04", "TheSamePlace_12" };
    private static readonly IReadOnlyList<string> Barren01AltOnly =
        new[] { MapDefinitions.Barren01AltName };

    private static readonly IReadOnlyDictionary<GameMode, IReadOnlyList<string>> MapsByMode =
        new Dictionary<GameMode, IReadOnlyList<string>>
        {
            [GameMode.FreeForAll] = FreeForAllMaps,
            [GameMode.Juggernaut] = JuggernautMaps,
            [GameMode.GunGame] = GunGameMaps,
            [GameMode.SniperBattle] = SniperBattleMaps,
            [GameMode.MichaelMeyers] = MichaelMeyersMaps,
            [GameMode.KillTheRat] = KillTheRatMaps,
            [GameMode.OneInTheChamber] = OneInTheChamberMaps,
            [GameMode.HotPotato] = HotPotatoMaps,
            [GameMode.Infidel] = InfidelMaps,
            [GameMode.HVT] = Barren01AltOnly,
            [GameMode.Assassin] = Barren01AltOnly,
            [GameMode.Hardpoint] = Barren01AltOnly,
            [GameMode.CaptureTheFlag] = Barren01AltOnly,
            [GameMode.SearchAndDestroy] = Barren01AltOnly,
            [GameMode.TeamDeathmatch] = Barren01AltOnly
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
            || mode == GameMode.SearchAndDestroy || mode == GameMode.TeamDeathmatch;
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
            GameMode.TeamDeathmatch => definition.SpawnPoints.Count > 0
                && definition.TeamOrigins.Count >= 2,
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