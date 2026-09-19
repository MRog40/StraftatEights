using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint TeamDeathmatchModId = 1618033997u;
    internal const float TeamDeathmatchSpawnRandomness = 0.35f;
    internal static ConfigEntry<bool> TeamDeathmatchEnabled = null!;

    private void InitializeTeamDeathmatch()
    {
        const string section = "Game Mode Settings";
        TeamDeathmatchEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Team Deathmatch",
            "Teams respawn and fight for kills throughout the match. Each kill awards 10 points to the team, and the first team to reach the configured point limit wins.");
        TeamDeathmatchEnabled.SettingChanged += (_, _) =>
        {
            TeamDeathmatchState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, TeamDeathmatchModId);
        ModeLobbyDataSync.RegisterKeys(TeamDeathmatchState.SettingsLobbyDataKey,
            TeamDeathmatchState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += TeamDeathmatchState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += TeamDeathmatchState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += TeamDeathmatchState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += TeamDeathmatchState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += TeamDeathmatchState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += TeamDeathmatchState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncTeamDeathmatchSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, bool ignoredLegacySpawnerFlag, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !TeamDeathmatchState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        TeamDeathmatchState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncTeamDeathmatchLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        TeamDeathmatchState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            roundId, revision);
    }
}