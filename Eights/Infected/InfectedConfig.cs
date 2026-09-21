using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint InfectedModId = 2718281832u;
    internal static ConfigEntry<bool> InfectedEnabled = null!;

    private void InitializeInfected()
    {
        const string section = "Game Mode Settings";
        InfectedEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Infected",
            "The host selects one infected player. Infected players use Couperets and cannot pick up guns; survivors use 10 health and random allowed weapons. Survivors who die become infected.");

        InfectedEnabled.SettingChanged += (_, _) =>
        {
            InfectedState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, InfectedModId);
        ModeLobbyDataSync.RegisterKeys(InfectedState.SettingsLobbyDataKey,
            InfectedState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += InfectedState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += InfectedState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += InfectedState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += InfectedState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += InfectedState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncInfectedSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !InfectedState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        InfectedState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncInfectedLiveState(CSteamID hostId, int initialInfectedPlayerId,
        string infectedData, string scoresData, int winnerId, int roundId, int revision,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        InfectedState.ApplyLiveState(hostId, initialInfectedPlayerId, infectedData,
            scoresData, winnerId, roundId, revision);
    }
}