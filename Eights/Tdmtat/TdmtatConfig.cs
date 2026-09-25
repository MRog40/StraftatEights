using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint TdmtatModId = 1618033997u;
    internal const float TdmtatSpawnRandomness = 0.35f;
    internal static ConfigEntry<bool> TdmtatEnabled = null!;

    private void InitializeTdmtat()
    {
        const string section = "Game Mode Settings";
        TdmtatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Tdmtat",
            "Teams respawn and fight for kills throughout the match. Each kill awards 10 points to the team, and the first team to reach the configured point limit wins.");
        TdmtatEnabled.SettingChanged += (_, _) =>
        {
            TdmtatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, TdmtatModId);
        ModeLobbyDataSync.RegisterKeys(TdmtatState.SettingsLobbyDataKey,
            TdmtatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += TdmtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += TdmtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += TdmtatState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += TdmtatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += TdmtatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += TdmtatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncTdmtatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, bool ignoredLegacySpawnerFlag, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !TdmtatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        TdmtatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncTdmtatLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        TdmtatState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            roundId, revision);
    }
}