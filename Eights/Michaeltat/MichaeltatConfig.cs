using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint MichaeltatModId = 3141592654u;
    internal static ConfigEntry<bool> MichaeltatEnabled = null!;

    private void InitializeMichaeltat()
    {
        const string section = "Game Mode Settings";
        MichaeltatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Michaeltat",
            "Michael hunts the other players with a Couperet while everyone else tries to stay alive. "
            + "Michael moves 10% faster. Everyone has 10 health until the final battle, when the last survivor "
            + "gets a Couperet and both final players return to 100 health. If at least two players remain after "
            + "90 seconds, the take ends without awarding points.");

        MichaeltatEnabled.SettingChanged += (_, _) =>
        {
            MichaeltatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, MichaeltatModId);
        ModeLobbyDataSync.RegisterKeys(MichaeltatState.SettingsLobbyDataKey, MichaeltatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += MichaeltatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += MichaeltatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += MichaeltatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += MichaeltatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += MichaeltatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncMichaeltatSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!MichaeltatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        MichaeltatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncMichaeltatLiveState(CSteamID hostId, int michaelPlayerId, int survivorCount, bool oneVsOne,
        string scoresData, float timeRemaining, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        MichaeltatState.ApplyLiveState(hostId, michaelPlayerId, survivorCount, oneVsOne,
            scoresData, timeRemaining, roundId, revision);
    }

    [CustomRPC]
    public void MichaeltatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}
