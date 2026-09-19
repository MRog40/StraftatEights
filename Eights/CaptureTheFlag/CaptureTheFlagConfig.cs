using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint CaptureTheFlagModId = 1618033995u;
    internal static ConfigEntry<bool> CaptureTheFlagEnabled = null!;

    private void InitializeCaptureTheFlag()
    {
        const string section = "Game Mode Settings";
        CaptureTheFlagEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Capture The Flag",
            "Teams steal the enemy flag and return it to their own base while protecting their flag. "
            + "Each capture awards 25 points, and the first team to reach the configured point limit wins; after time expires, respawns stop and sudden death resolves the match.");
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
