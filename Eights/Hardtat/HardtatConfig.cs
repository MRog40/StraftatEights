using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint HardtatModId = 1618033994u;
    internal const float HardtatSpawnRandomness = 0.35f;

    internal static ConfigEntry<bool> HardtatEnabled = null!;

    private void InitializeHardtat()
    {
        const string section = "Game Mode Settings";
        HardtatEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Hardtat",
            "Teams rotate between map hardpoints and earn 1 point for every second of uncontested control. "
            + "The first team to reach the configured point limit wins; if the match timer expires while tied, the game enters sudden death.");

        HardtatEnabled.SettingChanged += (_, _) =>
        {
            HardtatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, HardtatModId);
        ModeLobbyDataSync.RegisterKeys(HardtatState.SettingsLobbyDataKey,
            HardtatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += HardtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HardtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += HardtatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += HardtatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += HardtatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncHardtatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, bool ignoredLegacySpawnerFlag, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !HardtatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        HardtatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncHardtatLiveState(CSteamID hostId, string assignmentsData, int teamCount,
        string scoresData, int objectiveIndex, float objectiveElapsed, float contestTimeRemaining,
        int controller, bool suddenDeath, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        HardtatState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            objectiveIndex, objectiveElapsed, contestTimeRemaining, controller, suddenDeath,
            roundId, revision);
    }
}