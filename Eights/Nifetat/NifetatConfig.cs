using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint NifetatModId = 2718281833u;
    internal static ConfigEntry<bool> NifetatEnabled = null!;

    private void InitializeNifetat()
    {
        const string section = "Game Mode Settings";
        NifetatEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Nifetat",
            "Melee-only Ffatat. Every player has 10 health and uses one randomly selected melee weapon each round.");

        NifetatEnabled.SettingChanged += (_, _) =>
        {
            NifetatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, NifetatModId);
        ModeLobbyDataSync.RegisterKeys(NifetatState.SettingsLobbyDataKey, NifetatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += NifetatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += NifetatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += NifetatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += NifetatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += NifetatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncNifetatSettings(CSteamID hostId, int roundId, int revision, bool enabled,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !NifetatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        NifetatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncNifetatLiveState(CSteamID hostId, string selectedWeapon, string killsData,
        int winnerId, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        NifetatState.ApplyLiveState(hostId, selectedWeapon, killsData, winnerId, roundId, revision);
    }
}