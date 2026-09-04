using BepInEx.Configuration;

namespace StraftatEightsPlugin;

internal static class GameModeConfig
{
    internal static ConfigEntry<float> BindRespawnDelay(ConfigFile config, string section)
    {
        return config.Bind(section, "Respawn Delay (seconds)", 3f,
            new ConfigDescription("Host-controlled: how long a killed player waits before respawning.",
                new AcceptableValueRange<float>(0f, 10f)));
    }
}