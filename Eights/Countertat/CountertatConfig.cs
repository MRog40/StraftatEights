using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint CountertatModId = 1618034000u;
    internal static ConfigEntry<bool> CountertatEnabled = null!;

    private void InitializeCountertat()
    {
        CountertatEnabled = ModeConfigMigration.BindModeEnabled(Config,
            "Game Mode Settings", "Countertat",
            "Two fixed teams fight over the bomb. Aboubi attacks, Shadow Force defends, "
            + "and each take assigns weapons by player slot.");
        CountertatEnabled.SettingChanged += (_, _) =>
        {
            CountertatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, CountertatModId);
        ModeLobbyDataSync.RegisterKeys(CountertatState.SettingsLobbyDataKey);
        MyceliumNetwork.LobbyCreated += CountertatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += CountertatState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += CountertatState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += CountertatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += CountertatState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncCountertatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !CountertatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        CountertatState.ApplySettings(enabled);
    }
}