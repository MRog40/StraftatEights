using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint HotPotatoModId = 2718281830u;
    internal static ConfigEntry<bool> HotPotatoEnabled = null!;
    internal static ConfigEntry<string> HotPotatoWeaponOrder = null!;

    private void InitializeHotPotato()
    {
        const string modeSection = "Game Mode Settings";
        const string weaponSection = "Weapon Settings";
        const string defaultWeaponOrder = "Shotgun, Tromblonj, Gust, Crisis";
        HotPotatoEnabled = ModeConfigMigration.BindModeEnabled(Config, modeSection, "Hot Potato",
            "One player carries a renewable grenade while everyone else rotates through the configured weapons. All players can fight "
            + "and kill each other. Each kill awards 10 points, the potato passes after its carrier kills, and the first player to "
            + "reach the configured point limit wins.");
        HotPotatoWeaponOrder = Config.Bind(weaponSection, "Hot Potato Weapons", defaultWeaponOrder,
            "Host-controlled: exact prefab IDs rotated through by non-potato players.");

        HotPotatoEnabled.SettingChanged += (_, _) =>
        {
            HotPotatoState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };
        HotPotatoWeaponOrder.SettingChanged += (_, _) => HotPotatoState.PushSettingsIfHost();

        MyceliumNetwork.RegisterNetworkObject(this, HotPotatoModId);
        ModeLobbyDataSync.RegisterKeys(HotPotatoState.SettingsLobbyDataKey, HotPotatoState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += HotPotatoState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HotPotatoState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += HotPotatoState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += HotPotatoState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += HotPotatoState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncHotPotatoSettings(CSteamID hostId, int roundId, int revision, bool enabled,
        string? weaponOrder, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!HotPotatoState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        HotPotatoState.ApplySettings(enabled, weaponOrder ?? string.Empty);
    }

    [CustomRPC]
    public void SyncHotPotatoLiveState(CSteamID hostId, string killsData, int potatoPlayerId,
        int winnerId, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        HotPotatoState.ApplyLiveState(hostId, killsData, potatoPlayerId, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void HotPotatoAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}