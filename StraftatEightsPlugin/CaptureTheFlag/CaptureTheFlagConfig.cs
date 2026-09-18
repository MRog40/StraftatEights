using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint CaptureTheFlagModId = 1618033995u;
    internal static ConfigEntry<bool> CaptureTheFlagEnabled = null!;

    private void InitializeCaptureTheFlag()
    {
        const string section = "Game Mode Settings";
        CaptureTheFlagEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Capture The Flag",
            "Host-controlled: teams capture the enemy flag and return it to their base. Uses the authored objective map automatically.");
        CaptureTheFlagEnabled.SettingChanged += (_, _) =>
        {
            CaptureTheFlagState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, CaptureTheFlagModId);
        ModeLobbyDataSync.RegisterKeys(CaptureTheFlagState.SettingsLobbyDataKey,
            CaptureTheFlagState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += CaptureTheFlagState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += CaptureTheFlagState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += CaptureTheFlagState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += CaptureTheFlagState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += CaptureTheFlagState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += CaptureTheFlagState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncCaptureTheFlagSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, bool ignoredLegacySpawnerFlag, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !CaptureTheFlagState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        CaptureTheFlagState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncCaptureTheFlagLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, string flagsData, float matchTimeRemaining,
        bool suddenDeath, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        CaptureTheFlagState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            flagsData, matchTimeRemaining, suddenDeath, roundId, revision);
    }
}
