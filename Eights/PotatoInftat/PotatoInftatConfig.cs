using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint PotatoInftatModId = 2718281834u;
    internal static ConfigEntry<bool> PotatoInftatEnabled = null!;

    private void InitializePotatoInftat()
    {
        const string section = "Game Mode Settings";
        PotatoInftatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "PotatoInftat",
            "Infectedtat players use HandGrenades instead of Couperets. Survivors use 10 health and random allowed weapons; survivors who die become infected.");

        PotatoInftatEnabled.SettingChanged += (_, _) =>
        {
            PotatoInftatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, PotatoInftatModId);
        ModeLobbyDataSync.RegisterKeys(PotatoInftatState.SettingsLobbyDataKey,
            PotatoInftatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += PotatoInftatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += PotatoInftatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += PotatoInftatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += PotatoInftatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += PotatoInftatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncPotatoInftatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !PotatoInftatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        PotatoInftatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncPotatoInftatLiveState(CSteamID hostId, int initialInfectedtatPlayerId,
        string infectedData, string scoresData, int winnerId, int roundId, int revision,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        PotatoInftatState.ApplyLiveState(hostId, initialInfectedtatPlayerId, infectedData,
            scoresData, winnerId, roundId, revision);
    }
}
