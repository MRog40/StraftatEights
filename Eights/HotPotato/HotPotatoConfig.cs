using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint HotPotatoModId = 2718281830u;
    internal static ConfigEntry<bool> HotPotatoEnabled = null!;

    private void InitializeHotPotato()
    {
        const string section = "Game Mode Settings";
        HotPotatoEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Hot Potato",
            "One player carries a renewable grenade while everyone else uses shotguns, but all players can fight and kill each other. "
            + "Each kill awards 10 points, the potato passes after its carrier dies, and the first player to reach the configured "
            + "point limit wins.");

        HotPotatoEnabled.SettingChanged += (_, _) =>
        {
            HotPotatoState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, HotPotatoModId);
        ModeLobbyDataSync.RegisterKeys(HotPotatoState.SettingsLobbyDataKey, HotPotatoState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += HotPotatoState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HotPotatoState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += HotPotatoState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += HotPotatoState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += HotPotatoState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncHotPotatoSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!HotPotatoState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        HotPotatoState.ApplySettings(enabled);
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