using System.Linq;
using BepInEx.Configuration;

namespace Eights;

internal static class ModeConfigMigration
{
    internal static ConfigEntry<bool> BindModeEnabled(ConfigFile config, string section,
        string key, string description)
    {
        string legacyKey = key + " Enabled";
        ConfigDefinition legacyDefinition = new(section, legacyKey);
        bool hasLegacyValue = config.Keys.Contains(legacyDefinition);
        ConfigEntry<bool> legacyEntry = config.Bind(section, legacyKey, true, description);
        ConfigEntry<bool> entry = config.Bind(section, key,
            hasLegacyValue ? legacyEntry.Value : true, description);

        if (hasLegacyValue && legacyEntry.Value && !entry.Value)
        {
            entry.Value = true;
        }

        config.Remove(legacyDefinition);
        return entry;
    }
}