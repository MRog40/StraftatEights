using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;
using System.Linq;

namespace Eights;

public partial class Plugin
{
    internal const uint GlobalWeaponsModId = 1618033991u;
    internal static ConfigEntry<bool> WeaponTweaksEnabled = null!;
    internal static ConfigEntry<string> AllowedWeapons = null!;
    internal static ConfigEntry<int> SpareMagazines = null!;
    internal static ConfigEntry<bool> DefaultKnife = null!;
    internal static ConfigEntry<string> GunGameWeaponOrder = null!;

    private void InitializeGlobalWeapons()
    {
        const string section = "Weapon Settings";
        WeaponTweaksEnabled = Config.Bind(section, "Weapon Tweaks Enabled", true, "Host-controlled: enables weapon override rules.");
        AllowedWeapons = Config.Bind(section, "Allowed Weapons", "AK-K, AR15, Dispenser, HK_G11, Yangtse, Kusma, M2000, QCW05, SMG, Warden", "Host-controlled: exact weapon IDs allowed on spawners and team-mode loadouts.");
        SpareMagazines = Config.Bind(section, "Spare Magazines", 6, new ConfigDescription("Host-controlled: spare magazines granted with a weapon pickup.", new AcceptableValueRange<int>(2, 10)));
        DefaultKnife = Config.Bind(section, "Default Knife", false,
            "Host-controlled: gives players a Couperet after spawn or respawn when the right hand is empty.");
        const string defaultGunGameWeaponOrder =
            "Glock, Webley, SMG, Bukanee, Shotgun, AR15, QCW05, HK_G11, M2000, Couperet";
        ConfigDefinition legacyGunGameDefinition = new("Game Mode Settings", "Gun Game Weapon Order");
        bool hasLegacyGunGameOrder = Config.Keys.Contains(legacyGunGameDefinition);
        ConfigEntry<string>? legacyGunGameOrder = hasLegacyGunGameOrder
            ? Config.Bind(legacyGunGameDefinition, defaultGunGameWeaponOrder,
                new ConfigDescription("Host-controlled: exact prefab IDs in progression order."))
            : null;
        GunGameWeaponOrder = Config.Bind(section, "Gun Game Weapons",
            legacyGunGameOrder?.Value ?? defaultGunGameWeaponOrder,
            "Host-controlled: exact prefab IDs in progression order.");
        Config.Remove(legacyGunGameDefinition);
        if (hasLegacyGunGameOrder)
        {
            Config.Save();
        }
        ConfigDefinition legacyCycleDefinition = new(section, "F8 Cycle Weapons");
        if (Config.Keys.Contains(legacyCycleDefinition))
        {
            Config.Remove(legacyCycleDefinition);
            Config.Save();
        }
        WeaponTweaksEnabled.SettingChanged += (_, _) => WeaponSettingsState.PushIfHost();
        AllowedWeapons.SettingChanged += (_, _) => WeaponSettingsState.PushIfHost();
        SpareMagazines.SettingChanged += (_, _) => WeaponSettingsState.PushIfHost();
        DefaultKnife.SettingChanged += (_, _) => WeaponSettingsState.PushIfHost();
        GunGameWeaponOrder.SettingChanged += (_, _) => GunGameState.PushSettingsIfHost();
        MyceliumNetwork.RegisterNetworkObject(this, GlobalWeaponsModId);
        ModeLobbyDataSync.RegisterKeys(WeaponSettingsState.SettingsLobbyDataKey);
        MyceliumNetwork.LobbyCreated += WeaponSettingsState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += WeaponSettingsState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += WeaponSettingsState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += WeaponSettingsState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += WeaponSettingsState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncWeaponSettings(CSteamID hostId, int roundId, int revision, bool enabled,
        string allowedWeapons, int spareMagazines, bool defaultKnife,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!WeaponSettingsState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        WeaponSettingsState.Apply(enabled, allowedWeapons, spareMagazines, defaultKnife);
    }

    [CustomRPC]
    public void RequestWeaponAmmoReload(int playerId, int requestId, int roundId,
        int weaponObjectId, bool rightHand, RPCInfo info)
    {
        if (MyceliumNetwork.IsHost && roundId == GameModeManager.RoundId
            && NetworkAuthority.IsPlayerSender(info, playerId)
            && WeaponAmmoTuning.TryAcceptReloadRequest(info.SenderSteamID, requestId))
        {
            WeaponAmmoTuning.ApplyServerReload(playerId, requestId, weaponObjectId, rightHand);
        }
    }

    [CustomRPC]
    public void SyncWeaponAmmoReload(int playerId, int requestId, int roundId,
        int weaponObjectId, int currentAmmo, int spareRounds, bool rightHand, RPCInfo info)
    {
        if (NetworkAuthority.IsHostSender(info))
        {
            WeaponAmmoTuning.ApplyOwnerReloadSnapshot(playerId, requestId, roundId,
                weaponObjectId, currentAmmo, spareRounds, rightHand);
        }
    }

    [CustomRPC]
    public void AttachServerGrantedWeapon(int playerId, bool rightHand, RPCInfo info)
    {
        if (NetworkAuthority.IsHostSender(info) && NetworkAuthority.IsLocalPlayer(playerId))
        {
            WeaponService.AttachGrantedWeaponForOwner(playerId, rightHand);
        }
    }
}