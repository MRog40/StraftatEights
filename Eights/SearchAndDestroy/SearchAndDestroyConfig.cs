using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint SearchAndDestroyModId = 1618033996u;
    internal static ConfigEntry<bool> SearchAndDestroyEnabled = null!;

    private void InitializeSearchAndDestroy()
    {
        const string section = "Game Mode Settings";
        SearchAndDestroyEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Search and Destroy",
            "Two teams alternate between attacking and defending, with attackers planting a bomb at one of two sites and defenders defusing it or eliminating the attackers. "
            + "A take win awards 40 points with no respawns, and the first team to reach the configured point limit wins.");
        SearchAndDestroyEnabled.SettingChanged += (_, _) =>
        {
            SearchAndDestroyState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, SearchAndDestroyModId);
        ModeLobbyDataSync.RegisterKeys(SearchAndDestroyState.SettingsLobbyDataKey,
            SearchAndDestroyState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += SearchAndDestroyState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += SearchAndDestroyState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += SearchAndDestroyState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += SearchAndDestroyState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += SearchAndDestroyState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += SearchAndDestroyState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncSearchAndDestroySettings(CSteamID hostId, int roundId, int revision,
        bool enabled, bool ignoredLegacySpawnerFlag, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !SearchAndDestroyState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        SearchAndDestroyState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncSearchAndDestroyLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, string stateData, int takeId, int winnerId,
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        SearchAndDestroyState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            stateData, takeId, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void RequestSearchAndDestroyInteraction(int playerId, int commandId, bool pressed,
        bool lookingAtBomb, RPCInfo info)
    {
        SearchAndDestroyState.HandleInteractionRequest(playerId, commandId, pressed,
            lookingAtBomb, info);
    }
}
