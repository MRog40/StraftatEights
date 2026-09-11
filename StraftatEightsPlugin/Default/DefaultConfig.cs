using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint DefaultGameModeModId = 3141592653u;
    internal static ConfigEntry<bool> DefaultGameModeEnabled = null!;

    private void InitializeDefaultGameMode()
    {
        const string section = "Game Mode Settings";
        DefaultGameModeEnabled = Config.Bind(section, "Default Game Mode Enabled", false,
            "Host-controlled: uses map weapon spawners and default movement and health while awarding 50 points to players for each sub-round win.");

        DefaultGameModeEnabled.SettingChanged += (_, _) => GameModeManager.OnSettingsChanged();

        MyceliumNetwork.RegisterNetworkObject(this, DefaultGameModeModId);
        ModeLobbyDataSync.RegisterKeys(DefaultGameModeState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += DefaultGameModeState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += DefaultGameModeState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += DefaultGameModeState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += DefaultGameModeState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncDefaultGameModeLiveState(CSteamID hostId, string scoresData,
        string aliveData, int subRoundId, int winnerId, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GameModeManager.IsActive(GameMode.Default))
        {
            return;
        }
        DefaultGameModeState.ApplyLiveState(hostId, scoresData, aliveData,
            subRoundId, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void DefaultGameModeAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.AnnounceTarget(ClientInstance.ReplaceAllPlayerNameTags(text));
    }
}