using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint TeamDeathmatchModId = 1618033997u;
    internal static ConfigEntry<bool> TeamDeathmatchEnabled = null!;
    internal static ConfigEntry<bool> TeamDeathmatchUseWeaponSpawners = null!;

    private void InitializeTeamDeathmatch()
    {
        const string section = "Game Mode Settings";
        TeamDeathmatchEnabled = Config.Bind(section, "Team Deathmatch Enabled", false,
            "Host-controlled: teams score ten points per kill and respawn after death.");
        TeamDeathmatchUseWeaponSpawners = Config.Bind(section, "Team Deathmatch Use Weapon Spawners", true,
            "Host-controlled: use map weapon spawners instead of assigning a weapon directly on respawn.");
        TeamDeathmatchEnabled.SettingChanged += (_, _) =>
        {
            TeamDeathmatchState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };
        TeamDeathmatchUseWeaponSpawners.SettingChanged += (_, _) => TeamDeathmatchState.PushSettingsIfHost();

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
        bool enabled, bool useWeaponSpawners, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !TeamDeathmatchState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        TeamDeathmatchState.ApplySettings(enabled, useWeaponSpawners);
    }

    [CustomRPC]
    public void SyncTeamDeathmatchLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !GameModeManager.IsActive(GameMode.TeamDeathmatch))
        {
            return;
        }

        TeamDeathmatchState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            roundId, revision);
    }
}