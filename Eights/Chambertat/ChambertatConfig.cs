using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint ChambertatModId = 2718281829u;
    internal static ConfigEntry<bool> ChambertatEnabled = null!;

    private void InitializeChambertat()
    {
        const string section = "Game Mode Settings";
        ChambertatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Chambertat",
            "Each player gets one pistol shot and a Couperet, with no respawns during the take. "
            + "The last player alive wins 50 points, and the first player to reach the configured point limit wins.");

        ChambertatEnabled.SettingChanged += (_, _) =>
        {
            ChambertatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, ChambertatModId);
        ModeLobbyDataSync.RegisterKeys(ChambertatState.SettingsLobbyDataKey,
            ChambertatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += ChambertatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += ChambertatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += ChambertatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += ChambertatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += ChambertatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncChambertatSettings(CSteamID hostId, int roundId, int revision, bool enabled,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!ChambertatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        ChambertatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncChambertatLiveState(CSteamID hostId, string aliveData, string bulletsData,
        string scoresData, int takeId, int winnerId, float loadoutSecondsRemaining,
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        ChambertatState.ApplyLiveState(hostId, aliveData, bulletsData, scoresData,
            takeId, winnerId, loadoutSecondsRemaining, roundId, revision);
    }

    [CustomRPC]
    public void ChambertatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}