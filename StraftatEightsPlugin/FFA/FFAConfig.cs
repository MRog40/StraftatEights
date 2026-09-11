using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint FFAModId = 2718281828u;

    internal static ConfigEntry<bool> FFAEnabled = null!;

    private void InitializeFFA()
    {
        const string section = "Game Mode Settings";
        FFAEnabled = Config.Bind(section, "Free For All Enabled", false,
            "Host-controlled: players score ten points per kill; the first to 100 points wins.");

        FFAEnabled.SettingChanged += (_, _) => { FFAState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };

        MyceliumNetwork.RegisterNetworkObject(this, FFAModId);
        ModeLobbyDataSync.RegisterKeys(FFAState.SettingsLobbyDataKey, FFAState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += FFAState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += FFAState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += FFAState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += FFAState.OnPlayerEntered;
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
        if (!GameModeManager.IsActive(GameMode.FreeForAll))
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
        if (MatchLogs.Instance != null)
        {
            MatchLogs.Instance.WriteLocalLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}