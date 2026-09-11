using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint KillTheRatModId = 3141592655u;
    internal static ConfigEntry<bool> KillTheRatEnabled = null!;

    private void InitializeKillTheRat()
    {
        const string section = "Game Mode Settings";
        KillTheRatEnabled = Config.Bind(section, "Exterminators Enabled", false,
            "Host-controlled: the Rat gains three points per second; players gain ten points for killing the Rat.");

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
        if (!GameModeManager.IsActive(GameMode.KillTheRat))
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
        if (MatchLogs.Instance != null)
        {
            MatchLogs.Instance.WriteLocalLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}