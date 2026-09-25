using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint StraftatModId = 3141592653u;
    internal static ConfigEntry<bool> StraftatEnabled = null!;

    private void InitializeStraftat()
    {
        const string section = "Game Mode Settings";
        StraftatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Straftat",
            "Use the map's normal weapon spawners, movement, and health. Winning a take awards 50 points, and the first player or team to reach the configured point limit wins.");

        StraftatEnabled.SettingChanged += (_, _) => GameModeManager.OnSettingsChanged();

        MyceliumNetwork.RegisterNetworkObject(this, StraftatModId);
        ModeLobbyDataSync.RegisterKeys(StraftatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += StraftatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += StraftatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += StraftatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += StraftatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += StraftatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncStraftatLiveState(CSteamID hostId, string scoresData,
        string aliveData, int takeId, int winnerId, float timeRemaining,
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        StraftatState.ApplyLiveState(hostId, scoresData, aliveData,
            takeId, winnerId, timeRemaining, roundId, revision);
    }

    [CustomRPC]
    public void StraftatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}