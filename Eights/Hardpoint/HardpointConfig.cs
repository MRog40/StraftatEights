using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint HardpointModId = 1618033994u;
    internal const float HardpointSpawnRandomness = 0.35f;

    internal static ConfigEntry<bool> HardpointEnabled = null!;

    private void InitializeHardpoint()
    {
        const string section = "Game Mode Settings";
        HardpointEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Hardpoint",
            "Teams rotate between map hardpoints and earn 1 point for every second of uncontested control. "
            + "The first team to reach the configured point limit wins; if the match timer expires while tied, the game enters sudden death.");

        HardpointEnabled.SettingChanged += (_, _) =>
        {
            HardpointState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, HardpointModId);
        ModeLobbyDataSync.RegisterKeys(HardpointState.SettingsLobbyDataKey,
            HardpointState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += HardpointState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HardpointState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += HardpointState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += HardpointState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += HardpointState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncHardpointSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, bool ignoredLegacySpawnerFlag, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !HardpointState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        HardpointState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncHardpointLiveState(CSteamID hostId, string assignmentsData, int teamCount,
        string scoresData, int objectiveIndex, float objectiveElapsed, float contestTimeRemaining,
        int controller, bool suddenDeath, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        HardpointState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            objectiveIndex, objectiveElapsed, contestTimeRemaining, controller, suddenDeath,
            roundId, revision);
    }
}