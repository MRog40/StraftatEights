using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint HVTModId = 3141592657u;
    internal static ConfigEntry<bool> HVTEnabled = null!;

    private void InitializeHVT()
    {
        const string section = "Game Mode Settings";
        HVTEnabled = Config.Bind(section, "HVT Enabled", false,
            "Host-controlled: the first killer becomes the HVT, who gains three points per second alive.");

        HVTEnabled.SettingChanged += (_, _) =>
        {
            HVTState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, HVTModId);
        MyceliumNetwork.RegisterLobbyDataKey(HVTState.SettingsLobbyDataKey);
        MyceliumNetwork.RegisterLobbyDataKey(HVTState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += HVTState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HVTState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += HVTState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += HVTState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncHVTSettings(CSteamID hostId, int roundId, int revision, bool enabled)
    {
        if (!HVTState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        HVTState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncHVTLiveState(CSteamID hostId, int hvtPlayerId, string pointsData,
        int winnerId, int roundId, int revision)
    {
        HVTState.ApplyLiveState(hostId, hvtPlayerId, pointsData, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void HVTAnnounce(string text)
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.WriteLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}
