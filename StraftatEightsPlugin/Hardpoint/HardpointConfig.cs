using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint HardpointModId = 1618033994u;

    internal static ConfigEntry<bool> HardpointEnabled = null!;
    internal static ConfigEntry<bool> HardpointUseWeaponSpawners = null!;

    private void InitializeHardpoint()
    {
        const string section = "Game Mode Settings";
        HardpointEnabled = Config.Bind(section, "Hardpoint Enabled", false,
            "Host-controlled: teams fight for rotating hardpoints with one point per uncontested second.");
        HardpointUseWeaponSpawners = Config.Bind(section, "Hardpoint Use Weapon Spawners", true,
            "Host-controlled: use map weapon spawners instead of assigning a weapon directly on respawn.");

        HardpointEnabled.SettingChanged += (_, _) =>
        {
            HardpointState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };
        HardpointUseWeaponSpawners.SettingChanged += (_, _) => HardpointState.PushSettingsIfHost();

        MyceliumNetwork.RegisterNetworkObject(this, HardpointModId);
        ModeLobbyDataSync.RegisterKeys(HardpointState.SettingsLobbyDataKey,
            HardpointState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += HardpointState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HardpointState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += HardpointState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += HardpointState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncHardpointSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, bool useWeaponSpawners, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !HardpointState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        HardpointState.ApplySettings(enabled, useWeaponSpawners);
    }

    [CustomRPC]
    public void SyncHardpointLiveState(CSteamID hostId, string assignmentsData, int teamCount,
        string scoresData, int objectiveIndex, float objectiveElapsed, float contestTimeRemaining,
        int controller, bool suddenDeath, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !GameModeManager.IsActive(GameMode.Hardpoint))
        {
            return;
        }

        HardpointState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            objectiveIndex, objectiveElapsed, contestTimeRemaining, controller, suddenDeath,
            roundId, revision);
    }
}