using BepInEx.Configuration;
using MyceliumNetworking;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint HealthSettingsModId = 1892737466u;

    internal static ConfigEntry<int> MaxHealthPercent = null!;
    internal static ConfigEntry<float> HealthRegenDelaySeconds = null!;
    internal static ConfigEntry<int> HealthRegenRate = null!;

    private void InitializeHealthSettings()
    {
        const string section = "Health Settings";
        MaxHealthPercent = Config.Bind(section, "Max Health %", 100,
            new ConfigDescription("Host-controlled: max health as a percent of normal.", new AcceptableValueRange<int>(10, 400)));
        HealthRegenDelaySeconds = Config.Bind(section, "Regen Delay (seconds)", 5f,
            new ConfigDescription("Host-controlled: delay after taking damage before health regeneration starts.", new AcceptableValueRange<float>(0.1f, 60f)));
        HealthRegenRate = Config.Bind(section, "Regen Rate (health per second)", 10,
            new ConfigDescription("Host-controlled: health restored per second after the regen delay.", new AcceptableValueRange<int>(10, 100)));

        MaxHealthPercent.SettingChanged += (_, _) => HealthSettingsState.PushIfHost();
        HealthRegenDelaySeconds.SettingChanged += (_, _) => HealthSettingsState.PushIfHost();
        HealthRegenRate.SettingChanged += (_, _) => HealthSettingsState.PushIfHost();

        MyceliumNetwork.RegisterNetworkObject(this, HealthSettingsModId);
        MyceliumNetwork.LobbyCreated += HealthSettingsState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HealthSettingsState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += HealthSettingsState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncHealthSettings(int maxHealthPercent, float regenDelaySeconds, int regenRate)
    {
        Logger.LogInfo($"[HealthSettings] Received sync: maxHealth%={maxHealthPercent} delay={regenDelaySeconds:0.##} rate={regenRate}");
        HealthSettingsState.Apply(maxHealthPercent, regenDelaySeconds, regenRate);
    }
}