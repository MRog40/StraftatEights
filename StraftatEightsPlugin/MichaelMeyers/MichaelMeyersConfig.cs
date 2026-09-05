using BepInEx.Configuration;
using MyceliumNetworking;

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
        MyceliumNetwork.LobbyCreated += MichaelMeyersState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += MichaelMeyersState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += MichaelMeyersState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncMichaelMeyersSettings(bool enabled)
    {
        MichaelMeyersState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncMichaelMeyersLiveState(int michaelPlayerId, bool oneVsOne)
    {
        MichaelMeyersState.ApplyLiveState(michaelPlayerId, oneVsOne);
    }

    [CustomRPC]
    public void MichaelMeyersAnnounce(string text)
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.WriteLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}
