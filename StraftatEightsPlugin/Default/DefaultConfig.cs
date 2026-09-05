using BepInEx.Configuration;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal static ConfigEntry<bool> DefaultGameModeEnabled = null!;

    private void InitializeDefaultGameMode()
    {
        const string section = "Game Mode Settings";
        DefaultGameModeEnabled = Config.Bind(section, "Default Game Mode Enabled", false,
            "Host-controlled: uses normal Straftat one-life rules, map weapon spawners, and default health while keeping global movement settings active.");

        DefaultGameModeEnabled.SettingChanged += (_, _) => GameModeManager.OnSettingsChanged();
    }
}