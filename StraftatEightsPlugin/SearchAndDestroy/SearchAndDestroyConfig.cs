using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint SearchAndDestroyModId = 1618033996u;
    internal static ConfigEntry<bool> SearchAndDestroyEnabled = null!;
    internal static ConfigEntry<bool> SearchAndDestroyUseWeaponSpawners = null!;

    private void InitializeSearchAndDestroy()
    {
        const string section = "Game Mode Settings";
        SearchAndDestroyEnabled = Config.Bind(section, "Search and Destroy Enabled", false,
            "Host-controlled: two teams attack and defend bomb sites with one life per sub-round.");
        SearchAndDestroyUseWeaponSpawners = Config.Bind(section, "Search and Destroy Use Weapon Spawners", false,
            "Host-controlled: use map weapon spawners instead of assigning a weapon directly on respawn.");
        SearchAndDestroyEnabled.SettingChanged += (_, _) =>
        {
            SearchAndDestroyState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };
        SearchAndDestroyUseWeaponSpawners.SettingChanged += (_, _) => SearchAndDestroyState.PushSettingsIfHost();

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
        bool enabled, bool useWeaponSpawners, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !SearchAndDestroyState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        SearchAndDestroyState.ApplySettings(enabled, useWeaponSpawners);
    }

    [CustomRPC]
    public void SyncSearchAndDestroyLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, string stateData, int subRoundId, int winnerId,
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !GameModeManager.IsActive(GameMode.SearchAndDestroy))
        {
            return;
        }

        SearchAndDestroyState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            stateData, subRoundId, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void RequestSearchAndDestroyInteraction(int playerId, int commandId, bool pressed,
        bool lookingAtBomb, RPCInfo info)
    {
        SearchAndDestroyState.HandleInteractionRequest(playerId, commandId, pressed,
            lookingAtBomb, info);
    }
}
