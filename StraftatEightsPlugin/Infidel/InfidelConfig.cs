using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint InfidelModId = 2718281831u;
    internal static ConfigEntry<bool> InfidelEnabled = null!;

    private void InitializeInfidel()
    {
        const string section = "Game Mode Settings";
        InfidelEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Infidel",
            "Host-controlled: one private Infidel role, delayed AK loadouts, slow movement, and role-based scoring.");

        InfidelEnabled.SettingChanged += (_, _) =>
        {
            InfidelState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, InfidelModId);
        ModeLobbyDataSync.RegisterKeys(InfidelState.SettingsLobbyDataKey, InfidelState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += InfidelState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += InfidelState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += InfidelState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += InfidelState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += InfidelState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncInfidelSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!InfidelState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        InfidelState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncInfidelLiveState(CSteamID hostId, string scoresData, int winnerId,
        int takeId, bool weaponsUnlocked, float takeTimeRemaining, int roundId, int revision,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        InfidelState.ApplyLiveState(hostId, scoresData, winnerId, takeId,
            weaponsUnlocked, takeTimeRemaining, roundId, revision);
    }

    [CustomRPC]
    public void SyncInfidelRole(CSteamID hostId, int takeId, bool isInfidel, bool announce, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        InfidelState.ApplyLocalRole(hostId, takeId, isInfidel, announce);
    }

}