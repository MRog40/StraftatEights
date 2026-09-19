using BepInEx.Configuration;
using System.Collections.Generic;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint SniperBattleModId = 1618033992u;
    internal static ConfigEntry<bool> SniperBattleEnabled = null!;

    private void InitializeSniperBattle()
    {
        const string section = "Game Mode Settings";
        SniperBattleEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Sniper Battle",
            "Players respawn with only the M2000 sniper rifle, unlimited ammunition, and reduced health. "
            + "Each kill awards 10 points, and the first player to reach the configured point limit wins.");

        SniperBattleEnabled.SettingChanged += (_, _) => { SniperBattleState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };

        MyceliumNetwork.RegisterNetworkObject(this, SniperBattleModId);
        ModeLobbyDataSync.RegisterKeys(SniperBattleState.SettingsLobbyDataKey,
            SniperBattleState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += SniperBattleState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += SniperBattleState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += SniperBattleState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += SniperBattleState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += SniperBattleState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncSniperBattleSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!SniperBattleState.TryAcceptSettingsSnapshot(hostId, roundId, revision, "sniper-battle-rpc"))
        {
            return;
        }
        SniperBattleState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncSniperBattleLiveState(CSteamID hostId, string pointsData, int winnerId, int roundId,
        int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        SniperBattleState.ApplyLiveState(hostId, pointsData, winnerId, roundId, revision, "rpc");
    }

    [CustomRPC]
    public void SniperBattleAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}