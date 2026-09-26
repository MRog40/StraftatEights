using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint PotatotatModId = 2718281830u;
    internal static ConfigEntry<bool> PotatotatEnabled = null!;

    private void InitializePotatotat()
    {
        const string modeSection = "Game Modes Enabled";
        const string weaponSection = "Weapon Settings";
        PotatotatEnabled = ModeConfigMigration.BindModeEnabled(Config, modeSection, "Potatotat",
            "The grenade holder passes the potato to their victim on a kill. Other players score 20 points per kill and progress through "
            + "Elephant, Smith Carbine, Shotgun, Gust, and Tromblonj. A grenade death resets the victim to 0 points. Reach 100 points "
            + "to win with the Tromblonj.");
        ConfigDefinition previousWeaponOrderDefinition = new(weaponSection, "Hot Potato Weapons");
        ConfigDefinition weaponOrderDefinition = new(weaponSection, "Potatotat Weapons");
        bool hasOldWeaponConfig = Config.Keys.Contains(previousWeaponOrderDefinition)
            || Config.Keys.Contains(weaponOrderDefinition);
        Config.Remove(previousWeaponOrderDefinition);
        Config.Remove(weaponOrderDefinition);
        if (hasOldWeaponConfig)
        {
            Config.Save();
        }

        PotatotatEnabled.SettingChanged += (_, _) =>
        {
            PotatotatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, PotatotatModId);
        ModeLobbyDataSync.RegisterKeys(PotatotatState.SettingsLobbyDataKey, PotatotatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += PotatotatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += PotatotatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += PotatotatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += PotatotatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += PotatotatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncPotatotatSettings(CSteamID hostId, int roundId, int revision, bool enabled,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!PotatotatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        PotatotatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncPotatotatLiveState(CSteamID hostId, string killsData, int potatoPlayerId,
        int winnerId, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        PotatotatState.ApplyLiveState(hostId, killsData, potatoPlayerId, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void PotatotatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}