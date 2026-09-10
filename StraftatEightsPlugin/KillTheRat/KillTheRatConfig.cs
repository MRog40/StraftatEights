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
        KillTheRatEnabled = Config.Bind(section, "Kill the Rat Enabled", false,
            "Host-controlled: the Rat gains three points per second; players gain ten points for killing the Rat.");

        KillTheRatEnabled.SettingChanged += (_, _) =>
        {
            KillTheRatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, KillTheRatModId);
        MyceliumNetwork.LobbyCreated += KillTheRatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += KillTheRatState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += KillTheRatState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncKillTheRatSettings(CSteamID hostId, int roundId, int revision, bool enabled)
    {
        if (!KillTheRatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        KillTheRatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncKillTheRatLiveState(CSteamID hostId, int ratPlayerId, string pointsData,
        int winnerId, int roundId, int revision)
    {
        KillTheRatState.ApplyLiveState(hostId, ratPlayerId, pointsData, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void KillTheRatAnnounce(string text)
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.WriteLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}