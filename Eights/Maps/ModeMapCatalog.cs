using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.SceneManagement;

namespace Eights;

internal static class ModeMapCatalog
{
    private static readonly IReadOnlyList<string> HardtatMaps =
        new[]
        {
            MapDefinitions.Barren01AltName,
            MapDefinitions.DragonflyNeighbourhoodName,
            MapDefinitions.Bazaar03Name,
            MapDefinitions.Drain01Name,
            MapDefinitions.TheSamePlace11Name,
            MapDefinitions.Bazaar02Name,
            MapDefinitions.TheSamePlace14Name,
            // MapDefinitions.DragonflyVestigeName,
            // MapDefinitions.TheSamePlace04AltName,
            // MapDefinitions.Corridor11Name,
            MapDefinitions.Garden02Name,
            // MapDefinitions.Chateaux06NoGrassName,
            // MapDefinitions.BasketSwirlyName,
            // MapDefinitions.BasketSwirlyAltName,
            MapDefinitions.Republic08Name
        };
    private static readonly IReadOnlyList<string> CapturetatMaps =
        new[]
        {
            // MapDefinitions.Barren01AltName,
            MapDefinitions.DragonflyNeighbourhoodName,
            // MapDefinitions.Bazaar03Name,
            // MapDefinitions.Drain01Name,
            // MapDefinitions.TheSamePlace11Name,
            MapDefinitions.Bazaar02Name,
            // MapDefinitions.TheSamePlace14Name,
            MapDefinitions.DragonflyVestigeName,
            // MapDefinitions.TheSamePlace04AltName,
            MapDefinitions.Corridor11Name,
            // MapDefinitions.Garden02Name,
            // MapDefinitions.Chateaux06NoGrassName,
            MapDefinitions.BasketSwirlyName,
            MapDefinitions.BasketSwirlyAltName,
            MapDefinitions.Republic08Name
        };
    private static readonly IReadOnlyList<string> SndtatMaps =
        new[]
        {
            // MapDefinitions.Barren01AltName,
            MapDefinitions.DragonflyNeighbourhoodName,
            MapDefinitions.Bazaar03Name,
            MapDefinitions.Drain01Name,
            // MapDefinitions.TheSamePlace11Name,
            MapDefinitions.Bazaar02Name,
            MapDefinitions.TheSamePlace14Name,
            MapDefinitions.DragonflyVestigeName,
            // MapDefinitions.TheSamePlace04AltName,
            // MapDefinitions.Corridor11Name,
            // MapDefinitions.Garden02Name,
            // MapDefinitions.Chateaux06NoGrassName,
            // MapDefinitions.BasketSwirlyName,
            // MapDefinitions.BasketSwirlyAltName,
            // MapDefinitions.Republic08Name
        };
    private static readonly IReadOnlyList<string> CountertatMaps = SndtatMaps;
    private static readonly IReadOnlyList<string> TdmtatMaps =
        new[]
        {
            MapDefinitions.Barren01AltName,
            MapDefinitions.DragonflyNeighbourhoodName,
            MapDefinitions.Bazaar03Name,
            // MapDefinitions.Drain01Name,
            MapDefinitions.TheSamePlace11Name,
            // MapDefinitions.Bazaar02Name,
            MapDefinitions.TheSamePlace14Name,
            MapDefinitions.DragonflyVestigeName,
            MapDefinitions.TheSamePlace04AltName,
            // MapDefinitions.Corridor11Name,
            MapDefinitions.Garden02Name,
            // MapDefinitions.Chateaux06NoGrassName,
            MapDefinitions.BasketSwirlyName,
            MapDefinitions.BasketSwirlyAltName,
            // MapDefinitions.Republic08Name
        };
    private static readonly IReadOnlyList<string> FfatatMaps =
        new[]
        {
            MapDefinitions.DragonflyNeighbourhoodName,
            MapDefinitions.TheSamePlace14Name,
            MapDefinitions.DragonflyVestigeName,
            MapDefinitions.TheSamePlace04AltName,
            MapDefinitions.Chateaux06NoGrassName,
            MapDefinitions.BasketSwirlyName,
            MapDefinitions.BasketSwirlyAltName,
            MapDefinitions.Republic08Name,
            MapDefinitions.Arena13Name,
            MapDefinitions.Arena14Name
        };
    private static readonly IReadOnlyList<string> NifetatMaps = FfatatMaps;
    private static readonly IReadOnlyList<string> JuggertatMaps =
        new[] { "Arena_11_Alt", "Arena_10", "Arena_10_Alt", "Basket_Junglegym" };
    private static readonly IReadOnlyList<string> GuntatMaps = FfatatMaps;
    private static readonly IReadOnlyList<string> SnipertatMaps =
        new[]
        {
            MapDefinitions.Adobe02Name,
            MapDefinitions.Adobe02AltName,
            MapDefinitions.ArenaShadow08Name,
            MapDefinitions.Garden01Name,
            MapDefinitions.Garden01AltName,
            MapDefinitions.Chateaux01Name
        };
    private static readonly IReadOnlyList<string> MichaeltatMaps =
        new[]
        {
            "Bazaar_Alleyway_Alt", "WestVillage_04_Alt", "Adobe_01_Alt",
            "Arena_Shadow_04_Alt", "Basket_Junglegym_Alt", "Neo_Arena_03",
            "Republic_05", "Republic_01", "Dragonfly_Shrine_Alt", "Basket_00_Alt",
            "Arena_Shadow_03", "Backrooms_00", "Backrooms_02", "Basket_Borular_Alt",
            "Bazaar_Charshi_Alt", "Corridor_00_Alt", "Dragonfly_Basalt", "HK_02_Alt",
            "Parking_alt"
        };
    private static readonly IReadOnlyList<string> RatatatMaps =
        new[] { "WestVillage_04", "Toilets_00", "Arena_13", "JF_Poolrooms_01" };
    private static readonly IReadOnlyList<string> ChambertatMaps =
        new[] { "TheSamePlace_02", "StLucia_01", "Chateaux_04" };
    private static readonly IReadOnlyList<string> PotatotatMaps = FfatatMaps;
    private static readonly IReadOnlyList<string> InfideltatMaps =
        new[] { "Bazaar_01", "TheSamePlace_04", "TheSamePlace_12" };
    private static readonly IReadOnlyList<string> InfectedtatMaps = FfatatMaps;
    private static readonly IReadOnlyList<string> PotatoInftatMaps = InfectedtatMaps;
    private static readonly IReadOnlyList<string> NinjatatMaps = HardtatMaps;
    private static readonly IReadOnlyList<string> HunttatMaps = HardtatMaps;
    private static readonly IReadOnlyList<string> TanktatMaps = HardtatMaps;

    private static readonly IReadOnlyDictionary<GameMode, IReadOnlyList<string>> MapsByMode =
        new Dictionary<GameMode, IReadOnlyList<string>>
        {
            [GameMode.Ffatat] = FfatatMaps,
            [GameMode.Nifetat] = NifetatMaps,
            [GameMode.Juggertat] = JuggertatMaps,
            [GameMode.Guntat] = GuntatMaps,
            [GameMode.Snipertat] = SnipertatMaps,
            [GameMode.Michaeltat] = MichaeltatMaps,
            [GameMode.Ratatat] = RatatatMaps,
            [GameMode.Chambertat] = ChambertatMaps,
            [GameMode.Potatotat] = PotatotatMaps,
            [GameMode.Infideltat] = InfideltatMaps,
            [GameMode.Hvtat] = InfideltatMaps,
            [GameMode.Infectedtat] = InfectedtatMaps,
            [GameMode.PotatoInftat] = PotatoInftatMaps,
            [GameMode.Assassintat] = InfideltatMaps,
            [GameMode.Hardtat] = HardtatMaps,
            [GameMode.Capturetat] = CapturetatMaps,
            [GameMode.Sndtat] = SndtatMaps,
            [GameMode.Countertat] = CountertatMaps,
            [GameMode.Tdmtat] = TdmtatMaps,
            [GameMode.Ninjatat] = NinjatatMaps,
            [GameMode.Hunttat] = HunttatMaps,
            [GameMode.Tanktat] = TanktatMaps
        };

    internal static IReadOnlyList<string> GetMapNames(GameMode mode)
    {
        return GetMapNames(mode, true);
    }

    internal static IReadOnlyList<string> GetMapNames(GameMode mode, bool mapOverridesEnabled)
    {
        if (!mapOverridesEnabled && !RequiresMapOverride(mode))
        {
            IReadOnlyList<string> mapNames = GetNormalLobbyMapNames();
            return RequiresMapDefinition(mode)
                ? GetMapsWithRequiredDefinition(mode, mapNames)
                : mapNames;
        }

        return GetOverrideMapNames(mode);
    }

    internal static bool RequiresMapOverride(GameMode mode)
    {
        return mode == GameMode.Capturetat || mode == GameMode.Hardtat
            || GameModeManager.IsBombMode(mode) || GameModeManager.IsHuntersMode(mode);
    }

    private static IReadOnlyList<string> GetOverrideMapNames(GameMode mode)
    {
        if (mode == GameMode.Straftat)
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
        return mode == GameMode.Hardtat || mode == GameMode.Capturetat
            || GameModeManager.IsBombMode(mode) || GameModeManager.IsHuntersMode(mode);
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
            GameMode.Hardtat => definition.HardtatObjectives.Count > 0,
            GameMode.Capturetat => definition.CapturetatObjectives.Count == 2,
            GameMode.Sndtat => definition.SndObjectives.Count == 2,
            GameMode.Countertat => definition.SndObjectives.Count == 2,
            GameMode.Ninjatat => definition.HardtatObjectives.Count > 0,
            GameMode.Hunttat => definition.HardtatObjectives.Count > 0,
            GameMode.Tanktat => definition.HardtatObjectives.Count > 0,
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

            return mapOverridesEnabled || RequiresMapOverride(mode)
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