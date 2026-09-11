using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint HotPotatoModId = 2718281830u;
    internal static ConfigEntry<bool> HotPotatoEnabled = null!;

    private void InitializeHotPotato()
    {
        const string section = "Game Mode Settings";
        HotPotatoEnabled = Config.Bind(section, "Hot Potato Enabled", false,
            "Host-controlled: one player has a renewable HandGrenade, all other players have Shotgun, and kills score points.");

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
        if (!GameModeManager.IsActive(GameMode.HotPotato))
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
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.WriteLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}