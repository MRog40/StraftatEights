using BepInEx.Configuration;
using MyceliumNetworking;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint GlobalWeaponsModId = 1618033991u;
    internal static ConfigEntry<bool> WeaponTweaksEnabled = null!;
    internal static ConfigEntry<string> AllowedWeapons = null!;
    internal static ConfigEntry<int> SpareMagazines = null!;
    internal static ConfigEntry<bool> CycleWeapons = null!;

    private void InitializeGlobalWeapons()
    {
        const string section = "Weapon Settings";
        WeaponTweaksEnabled = Config.Bind(section, "Weapon Tweaks Enabled", false, "Host-controlled: enables weapon override rules.");
        AllowedWeapons = Config.Bind(section, "Allowed Weapons", "AK-K, AR15, Bukanee, Dispenser, HK_G11, Keso, Kusma, M2000, QCW05, Glock, Silenzzio, SMG, SmithCarbine, Warden, Yangtse", "Host-controlled: exact weapon IDs allowed on spawners and for cycling.");
        SpareMagazines = Config.Bind(section, "Spare Magazines", 5, new ConfigDescription("Host-controlled: spare magazines granted with a weapon pickup.", new AcceptableValueRange<int>(2, 10)));
        CycleWeapons = Config.Bind(section, "F8 Cycle Weapons", false, "Host-controlled: F8 cycles through allowed weapons and disables weapon droppers.");
        WeaponTweaksEnabled.SettingChanged += (_, _) => WeaponSettingsState.PushIfHost();
        AllowedWeapons.SettingChanged += (_, _) => WeaponSettingsState.PushIfHost();
        SpareMagazines.SettingChanged += (_, _) => WeaponSettingsState.PushIfHost();
        CycleWeapons.SettingChanged += (_, _) => WeaponSettingsState.PushIfHost();
        MyceliumNetwork.RegisterNetworkObject(this, GlobalWeaponsModId);
        MyceliumNetwork.LobbyCreated += WeaponSettingsState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += WeaponSettingsState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += WeaponSettingsState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncWeaponSettings(bool enabled, string allowedWeapons, int spareMagazines, bool cycleWeapons)
    {
        WeaponSettingsState.Apply(enabled, allowedWeapons, spareMagazines, cycleWeapons);
    }

    [CustomRPC]
    public void RequestWeaponCycle(int playerId)
    {
        if (MyceliumNetwork.IsHost && WeaponSettingsState.Enabled && WeaponSettingsState.Cycle)
        {
            WeaponSettingsState.GiveCycledWeapon(playerId);
        }
    }
}