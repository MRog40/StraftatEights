using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint FfatatModId = 2718281828u;

    internal static ConfigEntry<bool> FfatatEnabled = null!;

    private void InitializeFfatat()
    {
        const string section = "Game Mode Settings";
        FfatatEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Ffatat",
            "Every player fights independently and respawns after death. Each kill awards 10 points, and the first player to reach "
            + "the configured point limit wins.");

        FfatatEnabled.SettingChanged += (_, _) => { FfatatState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };

        MyceliumNetwork.RegisterNetworkObject(this, FfatatModId);
        ModeLobbyDataSync.RegisterKeys(FfatatState.SettingsLobbyDataKey, FfatatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += FfatatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += FfatatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += FfatatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += FfatatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += FfatatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncFfatatSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!FfatatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        FfatatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncFfatatLiveState(CSteamID hostId, string killsData, int winnerId, int roundId, int revision,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        FfatatState.ApplyLiveState(hostId, killsData, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void FfatatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}