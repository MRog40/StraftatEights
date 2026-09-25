using BepInEx.Configuration;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint SnipertatModId = 1618033992u;
    internal static ConfigEntry<bool> SnipertatEnabled = null!;

    private void InitializeSnipertat()
    {
        const string section = "Game Mode Settings";
        SnipertatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Snipertat",
            "Players respawn with only the M2000 sniper rifle, unlimited ammunition, and reduced health. "
            + "Each kill awards 10 points, and the first player to reach the configured point limit wins.");

        SnipertatEnabled.SettingChanged += (_, _) => { SnipertatState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };

        MyceliumNetwork.RegisterNetworkObject(this, SnipertatModId);
        ModeLobbyDataSync.RegisterKeys(SnipertatState.SettingsLobbyDataKey,
            SnipertatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += SnipertatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += SnipertatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += SnipertatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += SnipertatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += SnipertatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncSnipertatSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!SnipertatState.TryAcceptSettingsSnapshot(hostId, roundId, revision, "sniper-battle-rpc"))
        {
            return;
        }
        SnipertatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncSnipertatLiveState(CSteamID hostId, string pointsData, int winnerId, int roundId,
        int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        SnipertatState.ApplyLiveState(hostId, pointsData, winnerId, roundId, revision, "rpc");
    }

    [CustomRPC]
    public void SnipertatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}