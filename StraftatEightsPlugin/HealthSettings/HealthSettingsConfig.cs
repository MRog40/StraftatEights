using BepInEx.Configuration;
using MyceliumNetworking;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint HealthSettingsModId = 1892737466u;

    internal static ConfigEntry<int> MaxHealthPercent = null!;
    internal static ConfigEntry<bool> HealthTweaksEnabled = null!;
    internal static ConfigEntry<bool> HealthRegenEnabled = null!;
    internal static ConfigEntry<int> HealthRegenDelaySeconds = null!;
    internal static ConfigEntry<int> HealthRegenRate = null!;

    private void InitializeHealthSettings()
    {
        const string section = "Health Settings";
        HealthTweaksEnabled = Config.Bind(section, "Health Tweaks Enabled", true,
            "Host-controlled: master switch. Turn off to restore normal health and disable regeneration.");
        MaxHealthPercent = Config.Bind(section, "Max Health %", 100,
            new ConfigDescription("Host-controlled: max health as a percent of normal.", new AcceptableValueRange<int>(10, 400)));
        HealthRegenEnabled = Config.Bind(section, "Enable Health Regen", true,
            "Host-controlled: enables health regeneration after taking damage.");
        HealthRegenDelaySeconds = Config.Bind(section, "Regen Delay (seconds)", 5,
            new ConfigDescription("Host-controlled: delay after taking damage before health regeneration starts.", new AcceptableValueRange<int>(2, 15)));
        HealthRegenRate = Config.Bind(section, "Regen Rate (health per second)", 25,
            new ConfigDescription("Host-controlled: displayed HUD health restored per second after the regen delay.", new AcceptableValueList<int>(25, 50, 75, 100, 150, 200)));
        if (HealthRegenRate.Value != 25 && HealthRegenRate.Value != 50 && HealthRegenRate.Value != 75 && HealthRegenRate.Value != 100 && HealthRegenRate.Value != 150 && HealthRegenRate.Value != 200)
        {
            HealthRegenRate.Value = 25;
        }

        HealthTweaksEnabled.SettingChanged += (_, _) => HealthSettingsState.PushIfHost();
        MaxHealthPercent.SettingChanged += (_, _) => HealthSettingsState.PushIfHost();
        HealthRegenEnabled.SettingChanged += (_, _) => HealthSettingsState.PushIfHost();
        HealthRegenDelaySeconds.SettingChanged += (_, _) => HealthSettingsState.PushIfHost();
        HealthRegenRate.SettingChanged += (_, _) => HealthSettingsState.PushIfHost();

        MyceliumNetwork.RegisterNetworkObject(this, HealthSettingsModId);
        MyceliumNetwork.LobbyCreated += HealthSettingsState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HealthSettingsState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += HealthSettingsState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncHealthSettings(bool enabled, int maxHealthPercent, bool regenEnabled, int regenDelaySeconds, int regenRate)
    {
        Logger.LogInfo($"[HealthSettings] Received sync: tweaks={enabled} maxHealth%={maxHealthPercent} regen={regenEnabled} delay={regenDelaySeconds:0.##} rate={regenRate}");
        HealthSettingsState.Apply(enabled, maxHealthPercent, regenEnabled, regenDelaySeconds, regenRate);
    }
}