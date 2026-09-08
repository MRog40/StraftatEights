using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint OneInTheChamberModId = 2718281829u;
    internal static ConfigEntry<bool> OneInTheChamberEnabled = null!;

    private void InitializeOneInTheChamber()
    {
        const string section = "Game Mode Settings";
        OneInTheChamberEnabled = Config.Bind(section, "One in the Chamber Enabled", false,
            "Host-controlled: players get one Pistol shot, a Couperet, and no respawns; the last player alive wins.");

        OneInTheChamberEnabled.SettingChanged += (_, _) =>
        {
            OneInTheChamberState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, OneInTheChamberModId);
        MyceliumNetwork.LobbyCreated += OneInTheChamberState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += OneInTheChamberState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += OneInTheChamberState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncOneInTheChamberSettings(CSteamID hostId, int roundId, int revision, bool enabled)
    {
        if (!OneInTheChamberState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        OneInTheChamberState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncOneInTheChamberLiveState(CSteamID hostId, string aliveData, string bulletsData,
        int roundId, int revision)
    {
        OneInTheChamberState.ApplyLiveState(hostId, aliveData, bulletsData, roundId, revision);
    }

    [CustomRPC]
    public void OneInTheChamberAnnounce(string text)
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.WriteLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}