using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint GunGameModId = 1618033990u;
    internal static ConfigEntry<bool> GunGameEnabled = null!;

    private void InitializeGunGame()
    {
        const string modeSection = "Game Mode Settings";
        GunGameEnabled = ModeConfigMigration.BindModeEnabled(Config, modeSection, "Gun Game",
            "Players advance through the configured weapon list by getting kills, receiving the next weapon after each progression step. "
            + "Each kill adds 10 progression points, and the first player to complete the weapon progression wins.");

        GunGameEnabled.SettingChanged += (_, _) => { GunGameState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };
        MyceliumNetwork.RegisterNetworkObject(this, GunGameModId);
        ModeLobbyDataSync.RegisterKeys(GunGameState.SettingsLobbyDataKey, GunGameState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += GunGameState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += GunGameState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += GunGameState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += GunGameState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += GunGameState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncGunGameSettings(CSteamID hostId, int roundId, int revision, bool enabled, string? weaponOrder,
        RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GunGameState.TryAcceptSettingsSnapshot(hostId, roundId, revision, "gun-game-rpc"))
        {
            return;
        }
        GunGameState.ApplySettings(enabled, weaponOrder ?? string.Empty);
    }

    [CustomRPC]
    public void SyncGunGameLiveState(CSteamID hostId, string? progressData, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GunGameState.ApplyLiveState(hostId, progressData ?? string.Empty, roundId, revision, "gun-game-rpc");
    }
}