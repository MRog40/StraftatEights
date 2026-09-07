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
            "Host-controlled: players score kills independently; the first to the limit wins.");

        FFAEnabled.SettingChanged += (_, _) => { FFAState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };

        MyceliumNetwork.RegisterNetworkObject(this, FFAModId);
        MyceliumNetwork.LobbyCreated += FFAState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += FFAState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += FFAState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncFFASettings(CSteamID hostId, int roundId, int revision, bool enabled)
    {
        if (!FFAState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        FFAState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncFFALiveState(CSteamID hostId, string killsData, int winnerId, int roundId, int revision)
    {
        FFAState.ApplyLiveState(hostId, killsData, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void FFAAnnounce(string text)
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.WriteLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}