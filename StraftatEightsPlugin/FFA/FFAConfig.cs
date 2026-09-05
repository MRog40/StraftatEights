using BepInEx.Configuration;
using MyceliumNetworking;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint FFAModId = 2718281828u;

    internal static ConfigEntry<bool> FFAEnabled = null!;
    internal static ConfigEntry<int> FFAKillsToWin = null!;

    private void InitializeFFA()
    {
        const string section = "Game Mode Settings";
        FFAEnabled = Config.Bind(section, "Free For All Enabled", false,
            "Host-controlled: players score kills independently; the first to the limit wins.");
        FFAKillsToWin = Config.Bind(section, "Free For All Kills To Win", 10,
            new ConfigDescription("Host-controlled: kills required to win the take.", new AcceptableValueRange<int>(3, 30)));

        FFAEnabled.SettingChanged += (_, _) => { FFAState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };
        FFAKillsToWin.SettingChanged += (_, _) => FFAState.PushSettingsIfHost();

        MyceliumNetwork.RegisterNetworkObject(this, FFAModId);
        MyceliumNetwork.LobbyCreated += FFAState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += FFAState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += FFAState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncFFASettings(bool enabled, int killsToWin)
    {
        FFAState.ApplySettings(enabled, killsToWin);
    }

    [CustomRPC]
    public void SyncFFALiveState(string killsData, int winnerId)
    {
        FFAState.ApplyLiveState(killsData, winnerId);
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