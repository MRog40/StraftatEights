using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint DefaultGameModeModId = 3141592653u;
    internal static ConfigEntry<bool> DefaultGameModeEnabled = null!;

    private void InitializeDefaultGameMode()
    {
        const string section = "Game Mode Settings";
        DefaultGameModeEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Default Game Mode",
            "Use the map's normal weapon spawners, movement, and health. Winning a take awards 50 points, and the first player or team to reach the configured point limit wins.");

        DefaultGameModeEnabled.SettingChanged += (_, _) => GameModeManager.OnSettingsChanged();

        MyceliumNetwork.RegisterNetworkObject(this, DefaultGameModeModId);
        ModeLobbyDataSync.RegisterKeys(DefaultGameModeState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += DefaultGameModeState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += DefaultGameModeState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += DefaultGameModeState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += DefaultGameModeState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += DefaultGameModeState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncDefaultGameModeLiveState(CSteamID hostId, string scoresData,
        string aliveData, int takeId, int winnerId, float timeRemaining,
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        DefaultGameModeState.ApplyLiveState(hostId, scoresData, aliveData,
            takeId, winnerId, timeRemaining, roundId, revision);
    }

    [CustomRPC]
    public void DefaultGameModeAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}