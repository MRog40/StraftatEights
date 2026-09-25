using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint NifeModId = 2718281833u;
    internal static ConfigEntry<bool> NifeEnabled = null!;

    private void InitializeNife()
    {
        const string section = "Game Mode Settings";
        NifeEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Nife",
            "Melee-only Free For All. Every player has 10 health and uses one randomly selected melee weapon each round.");

        NifeEnabled.SettingChanged += (_, _) =>
        {
            NifeState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, NifeModId);
        ModeLobbyDataSync.RegisterKeys(NifeState.SettingsLobbyDataKey, NifeState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += NifeState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += NifeState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += NifeState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += NifeState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += NifeState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncNifeSettings(CSteamID hostId, int roundId, int revision, bool enabled,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !NifeState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        NifeState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncNifeLiveState(CSteamID hostId, string selectedWeapon, string killsData,
        int winnerId, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        NifeState.ApplyLiveState(hostId, selectedWeapon, killsData, winnerId, roundId, revision);
    }
}