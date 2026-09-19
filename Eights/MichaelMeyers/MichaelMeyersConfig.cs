using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint MichaelMeyersModId = 3141592654u;
    internal static ConfigEntry<bool> MichaelMeyersEnabled = null!;

    private void InitializeMichaelMeyers()
    {
        const string section = "Game Mode Settings";
        MichaelMeyersEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Michael Meyers",
            "Michael hunts the other players with a Couperet while everyone else tries to stay alive. "
            + "The last player alive wins the take and receives the normal round award; if at least two players "
            + "remain after 90 seconds, the take ends without awarding points.");

        MichaelMeyersEnabled.SettingChanged += (_, _) =>
        {
            MichaelMeyersState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, MichaelMeyersModId);
        ModeLobbyDataSync.RegisterKeys(MichaelMeyersState.SettingsLobbyDataKey, MichaelMeyersState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += MichaelMeyersState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += MichaelMeyersState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += MichaelMeyersState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += MichaelMeyersState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += MichaelMeyersState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncMichaelMeyersSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!MichaelMeyersState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        MichaelMeyersState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncMichaelMeyersLiveState(CSteamID hostId, int michaelPlayerId, int survivorCount, bool oneVsOne,
        string scoresData, float timeRemaining, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        MichaelMeyersState.ApplyLiveState(hostId, michaelPlayerId, survivorCount, oneVsOne,
            scoresData, timeRemaining, roundId, revision);
    }

    [CustomRPC]
    public void MichaelMeyersAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}
