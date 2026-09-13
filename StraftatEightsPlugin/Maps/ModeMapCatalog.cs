using System;
using System.Collections.Generic;
using System.Linq;

namespace StraftatEightsPlugin;

internal static class ModeMapCatalog
{
    private static readonly IReadOnlyList<string> Barren01AltOnly =
        new[] { MapDefinitions.Barren01AltName };

    private static readonly IReadOnlyDictionary<GameMode, IReadOnlyList<string>> MapsByMode =
        new Dictionary<GameMode, IReadOnlyList<string>>
        {
            [GameMode.Default] = Barren01AltOnly,
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
        };

    internal static IReadOnlyList<string> GetMapNames(GameMode mode)
    {
        return MapsByMode.TryGetValue(mode, out IReadOnlyList<string>? mapNames)
            ? mapNames
            : Array.Empty<string>();
    }

    internal static bool TryGetDefinition(GameMode mode, string mapName,
        out MapDefinition definition)
    {
        if (!MapsByMode.TryGetValue(mode, out IReadOnlyList<string>? mapNames)
            || !mapNames.Contains(mapName, StringComparer.Ordinal))
        {
            definition = null!;
            return false;
        }

        return MapDefinitions.TryGet(mapName, out definition!);
    }
}