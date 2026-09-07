using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint GunGameModId = 1618033990u;
    internal static ConfigEntry<bool> GunGameEnabled = null!;
    internal static ConfigEntry<string> GunGameWeaponOrder = null!;

    private void InitializeGunGame()
    {
        const string section = "Game Mode Settings";
        GunGameEnabled = Config.Bind(section, "Gun Game Enabled", false,
            "Host-controlled: players advance through the weapon list with each kill.");
        GunGameWeaponOrder = Config.Bind(section, "Gun Game Weapon Order",
            "Glock, Webley, SMG, Bukanee, SawedOff, Shotgun, Yangtse, Kusma, AR15, AK-K, QCW05, FG42, HK_G11, SmithCarbine, M2000, BaseballBat",
            "Host-controlled: exact prefab IDs in progression order.");

        GunGameEnabled.SettingChanged += (_, _) => { GunGameState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };
        GunGameWeaponOrder.SettingChanged += (_, _) => GunGameState.PushSettingsIfHost();
        MyceliumNetwork.RegisterNetworkObject(this, GunGameModId);
        MyceliumNetwork.LobbyCreated += GunGameState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += GunGameState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += GunGameState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncGunGameSettings(CSteamID hostId, int roundId, int revision, bool enabled, string weaponOrder)
    {
        if (!GunGameState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        GunGameState.ApplySettings(enabled, weaponOrder);
    }

    [CustomRPC]
    public void SyncGunGameLiveState(CSteamID hostId, string progressData, int roundId, int revision) =>
        GunGameState.ApplyLiveState(hostId, progressData, roundId, revision);
}