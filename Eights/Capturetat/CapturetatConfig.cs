using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint CapturetatModId = 1618033995u;
    internal static ConfigEntry<bool> CapturetatEnabled = null!;

    private void InitializeCapturetat()
    {
        const string section = "Game Mode Settings";
        CapturetatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Capturetat",
            "Teams steal the enemy flag and return it to their own base while protecting their flag. "
            + "Each capture awards 25 points, and the first team to reach the configured point limit wins; after time expires, respawns stop and sudden death resolves the match.");
        CapturetatEnabled.SettingChanged += (_, _) =>
        {
            CapturetatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, CapturetatModId);
        ModeLobbyDataSync.RegisterKeys(CapturetatState.SettingsLobbyDataKey,
            CapturetatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += CapturetatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += CapturetatState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += CapturetatState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += CapturetatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += CapturetatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += CapturetatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncCapturetatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, bool ignoredLegacySpawnerFlag, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !CapturetatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        CapturetatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncCapturetatLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, string flagsData, float matchTimeRemaining,
        bool suddenDeath, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        CapturetatState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            flagsData, matchTimeRemaining, suddenDeath, roundId, revision);
    }
}
