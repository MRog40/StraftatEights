using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint PotatotatModId = 2718281830u;
    internal static ConfigEntry<bool> PotatotatEnabled = null!;
    internal static ConfigEntry<string> PotatotatWeaponOrder = null!;

    private void InitializePotatotat()
    {
        const string modeSection = "Game Mode Settings";
        const string weaponSection = "Weapon Settings";
        const string defaultWeaponOrder = "Shotgun, Tromblonj, Gust, Crisis";
        ConfigDefinition previousWeaponOrderDefinition = new(weaponSection, "Hot Potato Weapons");
        bool hasPreviousWeaponOrder = Config.Keys.Contains(previousWeaponOrderDefinition);
        ConfigEntry<string>? previousWeaponOrder = hasPreviousWeaponOrder
            ? Config.Bind(previousWeaponOrderDefinition, defaultWeaponOrder,
                new ConfigDescription("Host-controlled: exact prefab IDs rotated through by non-potato players."))
            : null;
        PotatotatEnabled = ModeConfigMigration.BindModeEnabled(Config, modeSection, "Potatotat",
            "One player carries a renewable grenade while everyone else rotates through the configured weapons. All players can fight "
            + "and kill each other. Each kill awards 10 points, the potato passes after its carrier kills, and the first player to "
            + "reach the configured point limit wins.");
        PotatotatWeaponOrder = Config.Bind(weaponSection, "Potatotat Weapons",
            previousWeaponOrder?.Value ?? defaultWeaponOrder,
            "Host-controlled: exact prefab IDs rotated through by non-potato players.");
        Config.Remove(previousWeaponOrderDefinition);
        if (hasPreviousWeaponOrder)
        {
            Config.Save();
        }

        PotatotatEnabled.SettingChanged += (_, _) =>
        {
            PotatotatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };
        PotatotatWeaponOrder.SettingChanged += (_, _) => PotatotatState.PushSettingsIfHost();

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
        string? weaponOrder, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!PotatotatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        PotatotatState.ApplySettings(enabled, weaponOrder ?? string.Empty);
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