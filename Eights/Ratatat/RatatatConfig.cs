using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint RatatatModId = 3141592655u;
    internal static ConfigEntry<bool> RatatatEnabled = null!;

    private void InitializeRatatat()
    {
        const string section = "Game Mode Settings";
        RatatatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Ratatat",
            "One player becomes the Rat and earns 3 points each second while alive, while the other players hunt them. "
            + "Killing the Rat awards 10 points, and the first player to reach the configured point limit wins.");

        RatatatEnabled.SettingChanged += (_, _) =>
        {
            RatatatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, RatatatModId);
        ModeLobbyDataSync.RegisterKeys(RatatatState.SettingsLobbyDataKey, RatatatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += RatatatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += RatatatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += RatatatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += RatatatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += RatatatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncRatatatSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!RatatatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        RatatatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncRatatatLiveState(CSteamID hostId, int ratPlayerId, string pointsData,
        int winnerId, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        RatatatState.ApplyLiveState(hostId, ratPlayerId, pointsData, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void RatatatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}