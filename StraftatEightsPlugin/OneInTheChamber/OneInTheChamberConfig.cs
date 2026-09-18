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
        OneInTheChamberEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "One in the Chamber",
            "Host-controlled: players get one Pistol shot, a Couperet, and no respawns; the last player alive wins.");

        OneInTheChamberEnabled.SettingChanged += (_, _) =>
        {
            OneInTheChamberState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, OneInTheChamberModId);
        ModeLobbyDataSync.RegisterKeys(OneInTheChamberState.SettingsLobbyDataKey,
            OneInTheChamberState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += OneInTheChamberState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += OneInTheChamberState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += OneInTheChamberState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += OneInTheChamberState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += OneInTheChamberState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncOneInTheChamberSettings(CSteamID hostId, int roundId, int revision, bool enabled,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!OneInTheChamberState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        OneInTheChamberState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncOneInTheChamberLiveState(CSteamID hostId, string aliveData, string bulletsData,
        string scoresData, int takeId, int winnerId, float loadoutSecondsRemaining,
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        OneInTheChamberState.ApplyLiveState(hostId, aliveData, bulletsData, scoresData,
            takeId, winnerId, loadoutSecondsRemaining, roundId, revision);
    }

    [CustomRPC]
    public void OneInTheChamberAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, 2f, true);
    }
}