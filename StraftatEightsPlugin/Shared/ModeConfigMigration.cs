using BepInEx.Configuration;

namespace StraftatEightsPlugin;

internal static class ModeConfigMigration
{
    internal static ConfigEntry<bool> BindModeEnabled(ConfigFile config, string section,
        string key, string description)
    {
        string legacyKey = key + " Enabled";
        ConfigDefinition legacyDefinition = new(section, legacyKey);
        ConfigEntry<bool> legacyEntry = config.Bind(section, legacyKey, false, description);
        ConfigEntry<bool> entry = config.Bind(section, key, legacyEntry.Value, description);

        if (legacyEntry.Value && !entry.Value)
        {
            entry.Value = true;
        }

        config.Remove(legacyDefinition);
        return entry;
    }
}