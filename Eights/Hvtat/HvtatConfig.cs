using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint HvtatModId = 3141592657u;
    internal static ConfigEntry<bool> HvtatEnabled = null!;

    private void InitializeHvtat()
    {
        const string section = "Game Mode Settings";
        HvtatEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Hvtat",
            "The first legitimate killer becomes the High-Value Target and earns 3 points each second while alive. "
            + "Other players hunt the Hvtat, and killing them transfers the role; the first player to reach the configured "
            + "point limit wins.");

        HvtatEnabled.SettingChanged += (_, _) =>
        {
            HvtatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, HvtatModId);
        ModeLobbyDataSync.RegisterKeys(HvtatState.SettingsLobbyDataKey, HvtatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += HvtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HvtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += HvtatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += HvtatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += HvtatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncHvtatSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!HvtatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        HvtatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncHvtatLiveState(CSteamID hostId, int hvtPlayerId, string pointsData,
        int winnerId, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        HvtatState.ApplyLiveState(hostId, hvtPlayerId, pointsData, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void HvtatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }

    [CustomRPC]
    public void HvtatAnnounceTarget(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        GameModeHud.AnnounceTarget(ClientInstance.ReplaceAllPlayerNameTags(text));
    }
}
