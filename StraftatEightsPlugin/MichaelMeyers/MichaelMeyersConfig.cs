using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint MichaelMeyersModId = 3141592654u;
    internal static ConfigEntry<bool> MichaelMeyersEnabled = null!;

    private void InitializeMichaelMeyers()
    {
        const string section = "Game Mode Settings";
        MichaelMeyersEnabled = Config.Bind(section, "Michael Meyers Enabled", false,
            "Host-controlled: one player hunts the other players with a Couperet; the last player alive wins.");

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
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            return;
        }
        MichaelMeyersState.ApplyLiveState(hostId, michaelPlayerId, survivorCount, oneVsOne, roundId, revision);
    }

    [CustomRPC]
    public void MichaelMeyersAnnounce(string text, RPCInfo info)
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
