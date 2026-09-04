using BepInEx.Configuration;
using MyceliumNetworking;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint FFAModId = 2718281828u;

    internal static ConfigEntry<bool> FFAEnabled = null!;
    internal static ConfigEntry<float> FFARespawnDelaySeconds = null!;
    internal static ConfigEntry<int> FFAKillsToWin = null!;

    private void InitializeFFA()
    {
        const string section = "Game Mode - Free For All";
        FFAEnabled = Config.Bind(section, "Enabled", false,
            "Host-controlled: enables free for all. Each player fights for their own kill count.");
        FFARespawnDelaySeconds = GameModeConfig.BindRespawnDelay(Config, section);
        FFAKillsToWin = Config.Bind(section, "Kills To Win", 10,
            new ConfigDescription("Host-controlled: kills required to win the take.", new AcceptableValueRange<int>(3, 30)));

        FFAEnabled.SettingChanged += (_, _) => { FFAState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };
        FFARespawnDelaySeconds.SettingChanged += (_, _) => FFAState.PushSettingsIfHost();
        FFAKillsToWin.SettingChanged += (_, _) => FFAState.PushSettingsIfHost();

        MyceliumNetwork.RegisterNetworkObject(this, FFAModId);
        MyceliumNetwork.LobbyCreated += FFAState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += FFAState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += FFAState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncFFASettings(bool enabled, float respawnDelaySeconds, int killsToWin)
    {
        FFAState.ApplySettings(enabled, respawnDelaySeconds, killsToWin);
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