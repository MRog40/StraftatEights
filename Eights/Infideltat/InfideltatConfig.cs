using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint InfideltatModId = 2718281831u;
    internal static ConfigEntry<bool> InfideltatEnabled = null!;

    private void InitializeInfideltat()
    {
        const string section = "Game Mode Settings";
        InfideltatEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Infideltat",
            "One hidden Infideltat faces the terrorists, with delayed weapons and different health and movement rules for each role. "
            + "Terrorists earn 30 points for killing the Infideltat, while the Infideltat earns 50 points for outliving all terrorists; "
            + "the first to reach the configured point limit wins.");

        InfideltatEnabled.SettingChanged += (_, _) =>
        {
            InfideltatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, InfideltatModId);
        ModeLobbyDataSync.RegisterKeys(InfideltatState.SettingsLobbyDataKey, InfideltatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += InfideltatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += InfideltatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += InfideltatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += InfideltatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += InfideltatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncInfideltatSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!InfideltatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        InfideltatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncInfideltatLiveState(CSteamID hostId, string scoresData, int winnerId,
        int takeId, bool weaponsUnlocked, float takeTimeRemaining, int roundId, int revision,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        InfideltatState.ApplyLiveState(hostId, scoresData, winnerId, takeId,
            weaponsUnlocked, takeTimeRemaining, roundId, revision);
    }

    [CustomRPC]
    public void SyncInfideltatRole(CSteamID hostId, int takeId, bool isInfideltat, bool announce, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        InfideltatState.ApplyLocalRole(hostId, takeId, isInfideltat, announce);
    }

    [CustomRPC]
    public void SyncInfideltatBeep(CSteamID hostId, int takeId, int beepId,
        UnityEngine.Vector3 position, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        InfideltatState.ApplyInfideltatBeep(hostId, takeId, beepId, position);
    }

}