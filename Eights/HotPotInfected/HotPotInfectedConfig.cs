using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint HotPotInfectedModId = 2718281834u;
    internal static ConfigEntry<bool> HotPotInfectedEnabled = null!;

    private void InitializeHotPotInfected()
    {
        const string section = "Game Mode Settings";
        HotPotInfectedEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Hot Pot: Infected",
            "Infected players use HandGrenades instead of Couperets. Survivors use 10 health and random allowed weapons; survivors who die become infected.");

        HotPotInfectedEnabled.SettingChanged += (_, _) =>
        {
            HotPotInfectedState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, HotPotInfectedModId);
        ModeLobbyDataSync.RegisterKeys(HotPotInfectedState.SettingsLobbyDataKey,
            HotPotInfectedState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += HotPotInfectedState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HotPotInfectedState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += HotPotInfectedState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += HotPotInfectedState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += HotPotInfectedState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncHotPotInfectedSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !HotPotInfectedState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        HotPotInfectedState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncHotPotInfectedLiveState(CSteamID hostId, int initialInfectedPlayerId,
        string infectedData, string scoresData, int winnerId, int roundId, int revision,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        HotPotInfectedState.ApplyLiveState(hostId, initialInfectedPlayerId, infectedData,
            scoresData, winnerId, roundId, revision);
    }
}
