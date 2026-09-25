using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint GuntatModId = 1618033990u;
    internal static ConfigEntry<bool> GuntatEnabled = null!;

    private void InitializeGuntat()
    {
        const string modeSection = "Game Mode Settings";
        GuntatEnabled = ModeConfigMigration.BindModeEnabled(Config, modeSection, "Guntat",
            "Players advance through the configured weapon list by getting kills, receiving the next weapon after each progression step. "
            + "Each kill adds 10 progression points, and the first player to complete the weapon progression wins.");

        GuntatEnabled.SettingChanged += (_, _) => { GuntatState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };
        MyceliumNetwork.RegisterNetworkObject(this, GuntatModId);
        ModeLobbyDataSync.RegisterKeys(GuntatState.SettingsLobbyDataKey, GuntatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += GuntatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += GuntatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += GuntatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += GuntatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += GuntatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncGuntatSettings(CSteamID hostId, int roundId, int revision, bool enabled, string? weaponOrder,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GuntatState.TryAcceptSettingsSnapshot(hostId, roundId, revision, "gun-game-rpc"))
        {
            return;
        }
        GuntatState.ApplySettings(enabled, weaponOrder ?? string.Empty);
    }

    [CustomRPC]
    public void SyncGuntatLiveState(CSteamID hostId, string? progressData, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GuntatState.ApplyLiveState(hostId, progressData ?? string.Empty, roundId, revision, "gun-game-rpc");
    }
}