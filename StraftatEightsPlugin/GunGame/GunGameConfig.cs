using BepInEx.Configuration;
using MyceliumNetworking;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint GunGameModId = 1618033990u;
    internal static ConfigEntry<bool> GunGameEnabled = null!;
    internal static ConfigEntry<float> GunGameRespawnDelaySeconds = null!;
    internal static ConfigEntry<int> GunGameKillsToWin = null!;
    internal static ConfigEntry<string> GunGameWeaponOrder = null!;

    private void InitializeGunGame()
    {
        const string section = "Game Mode - Gun Game - 3";
        GunGameEnabled = Config.Bind(section, "Enabled", false, "Host-controlled: enables Gun Game.");
        GunGameRespawnDelaySeconds = GameModeConfig.BindRespawnDelay(Config, section);
        GunGameKillsToWin = Config.Bind(section, "Kills To Win", 30,
            new ConfigDescription("Host-controlled: kills required to win.", new AcceptableValueRange<int>(1, 100)));
        GunGameWeaponOrder = Config.Bind(section, "Weapon Order",
            "Glock, Revolver, Silenzzio, Webley, Mac10, SMG, Bukanee, Yangtse, Hill_H15, Crisis, DF_Torrent, SawedOff, Shotgun, Havoc, AAA12, Kusma, AR15, AK-K, QCW05, FG42, HK_G11, SmithCarbine, Warden, M2000, Bayshore, HandCanon, Minigun, RocketLauncher, Phoenix, BaseballBat",
            "Host-controlled: exact RandomWeapons prefab IDs in progression order. The final weapon should be BaseballBat.");

        GunGameEnabled.SettingChanged += (_, _) => { GunGameState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };
        GunGameRespawnDelaySeconds.SettingChanged += (_, _) => GunGameState.PushSettingsIfHost();
        GunGameKillsToWin.SettingChanged += (_, _) => GunGameState.PushSettingsIfHost();
        GunGameWeaponOrder.SettingChanged += (_, _) => GunGameState.PushSettingsIfHost();
        MyceliumNetwork.RegisterNetworkObject(this, GunGameModId);
        MyceliumNetwork.LobbyCreated += GunGameState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += GunGameState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += GunGameState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncGunGameSettings(bool enabled, float respawnDelay, int killsToWin, string weaponOrder)
    {
        GunGameState.ApplySettings(enabled, respawnDelay, killsToWin, weaponOrder);
    }

    [CustomRPC]
    public void SyncGunGameLiveState(string progressData) => GunGameState.ApplyLiveState(progressData);
}