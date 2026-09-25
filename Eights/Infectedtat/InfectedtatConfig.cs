using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint InfectedtatModId = 2718281832u;
    internal static ConfigEntry<bool> InfectedtatEnabled = null!;

    private void InitializeInfectedtat()
    {
        const string section = "Game Mode Settings";
        InfectedtatEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Infectedtat",
            "The host selects one infected player. Infectedtat players use Couperets and cannot pick up guns; survivors use 10 health and random allowed weapons. Survivors who die become infected.");

        InfectedtatEnabled.SettingChanged += (_, _) =>
        {
            InfectedtatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, InfectedtatModId);
        ModeLobbyDataSync.RegisterKeys(InfectedtatState.SettingsLobbyDataKey,
            InfectedtatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += InfectedtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += InfectedtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += InfectedtatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += InfectedtatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += InfectedtatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncInfectedtatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !InfectedtatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        InfectedtatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncInfectedtatLiveState(CSteamID hostId, int initialInfectedtatPlayerId,
        string infectedData, string scoresData, int winnerId, int roundId, int revision,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        InfectedtatState.ApplyLiveState(hostId, initialInfectedtatPlayerId, infectedData,
            scoresData, winnerId, roundId, revision);
    }
}