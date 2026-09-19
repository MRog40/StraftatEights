using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint FFAModId = 2718281828u;

    internal static ConfigEntry<bool> FFAEnabled = null!;

    private void InitializeFFA()
    {
        const string section = "Game Mode Settings";
        FFAEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Free For All",
            "Every player fights independently and respawns after death. Each kill awards 10 points, and the first player to reach "
            + "the configured point limit wins.");

        FFAEnabled.SettingChanged += (_, _) => { FFAState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };

        MyceliumNetwork.RegisterNetworkObject(this, FFAModId);
        ModeLobbyDataSync.RegisterKeys(FFAState.SettingsLobbyDataKey, FFAState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += FFAState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += FFAState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += FFAState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += FFAState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += FFAState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncFFASettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!FFAState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        FFAState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncFFALiveState(CSteamID hostId, string killsData, int winnerId, int roundId, int revision,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        FFAState.ApplyLiveState(hostId, killsData, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void FFAAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}