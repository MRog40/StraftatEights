using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint KillTheRatModId = 3141592655u;
    internal static ConfigEntry<bool> KillTheRatEnabled = null!;

    private void InitializeKillTheRat()
    {
        const string section = "Game Mode Settings";
        KillTheRatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Kill The Rat",
            "One player becomes the Rat and earns 3 points each second while alive, while the other players hunt them. "
            + "Killing the Rat awards 10 points, and the first player to reach the configured point limit wins.");

        KillTheRatEnabled.SettingChanged += (_, _) =>
        {
            KillTheRatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, KillTheRatModId);
        ModeLobbyDataSync.RegisterKeys(KillTheRatState.SettingsLobbyDataKey, KillTheRatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += KillTheRatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += KillTheRatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += KillTheRatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += KillTheRatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += KillTheRatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncKillTheRatSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!KillTheRatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        KillTheRatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncKillTheRatLiveState(CSteamID hostId, int ratPlayerId, string pointsData,
        int winnerId, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        KillTheRatState.ApplyLiveState(hostId, ratPlayerId, pointsData, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void KillTheRatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}