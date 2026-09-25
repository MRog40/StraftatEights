using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;

namespace Eights;

internal static class ModeConfigMigration
{
    private static readonly IReadOnlyDictionary<string, string[]> PreviousModeKeys =
        new Dictionary<string, string[]>
        {
            ["Straftat"] = new[] { "Default Game Mode", "Default" },
            ["Ffatat"] = new[] { "Free For All", "Free" },
            ["Nifetat"] = new[] { "Nife" },
            ["Juggertat"] = new[] { "Juggernaut" },
            ["Guntat"] = new[] { "Gun Game", "Gun" },
            ["Snipertat"] = new[] { "Sniper Battle", "Sniper" },
            ["Michaeltat"] = new[] { "Michael Meyers", "Michael" },
            ["Ratatat"] = new[] { "Kill The Rat", "Kill" },
            ["Chambertat"] = new[] { "One in the Chamber", "One" },
            ["Potatotat"] = new[] { "Hot Potato", "Hot" },
            ["Infideltat"] = new[] { "Infidel" },
            ["Infectedtat"] = new[] { "Infected" },
            ["PotatoInftat"] = new[] { "Hot Pot: Infected" },
            ["Hvtat"] = new[] { "HVT" },
            ["Assassintat"] = new[] { "Assassin" },
            ["Hardtat"] = new[] { "Hardpoint" },
            ["Capturetat"] = new[] { "Capture The Flag" },
            ["Sndtat"] = new[] { "Search and Destroy" },
            ["Tdmtat"] = new[] { "Team Deathmatch" },
            ["Ninjatat"] = new[] { "Ninja Hunters" },
            ["Hunttat"] = new[] { "Rabbit Hunt" },
            ["Tanktat"] = new[] { "Tank Battle" }
        };

    internal static ConfigEntry<bool> BindModeEnabled(ConfigFile config, string section,
        string key, string description)
    {
        List<string> legacyKeys = new() { key + " Enabled" };
        if (PreviousModeKeys.TryGetValue(key, out string[]? previousKeys))
        {
            foreach (string previousKey in previousKeys)
            {
                legacyKeys.Add(previousKey);
                legacyKeys.Add(previousKey + " Enabled");
            }
        }

        bool hasLegacyValue = false;
        bool legacyValue = true;
        foreach (string legacyKey in legacyKeys)
        {
            ConfigDefinition legacyDefinition = new(section, legacyKey);
            if (!config.Keys.Contains(legacyDefinition))
            {
                continue;
            }

            ConfigEntry<bool> legacyEntry = config.Bind(section, legacyKey, true, description);
            if (!hasLegacyValue)
            {
                hasLegacyValue = true;
                legacyValue = legacyEntry.Value;
            }
            config.Remove(legacyDefinition);
        }

        ConfigEntry<bool> entry = config.Bind(section, key,
            hasLegacyValue ? legacyValue : true, description);

        return entry;
    }
}