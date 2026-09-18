using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;
using System.Linq;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint GunGameModId = 1618033990u;
    internal static ConfigEntry<bool> GunGameEnabled = null!;
    internal static ConfigEntry<string> GunGameWeaponOrder = null!;

    private void InitializeGunGame()
    {
        const string modeSection = "Game Mode Settings";
        const string weaponSection = "Weapon Settings";
        const string defaultWeaponOrder =
            "Glock, Webley, SMG, Bukanee, Shotgun, AR15, QCW05, HK_G11, M2000, Couperet";
        GunGameEnabled = ModeConfigMigration.BindModeEnabled(Config, modeSection, "Gun Game",
            "Host-controlled: players advance through the weapon list with each kill.");
        ConfigDefinition legacyDefinition = new(modeSection, "Gun Game Weapon Order");
        bool hasLegacyOrder = Config.Keys.Contains(legacyDefinition);
        ConfigEntry<string>? legacyOrder = hasLegacyOrder
            ? Config.Bind(legacyDefinition, defaultWeaponOrder,
                new ConfigDescription("Host-controlled: exact prefab IDs in progression order."))
            : null;
        GunGameWeaponOrder = Config.Bind(weaponSection, "Gun Game Weapons",
            legacyOrder?.Value ?? defaultWeaponOrder,
            "Host-controlled: exact prefab IDs in progression order.");
        Config.Remove(legacyDefinition);

        GunGameEnabled.SettingChanged += (_, _) => { GunGameState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };
        GunGameWeaponOrder.SettingChanged += (_, _) => GunGameState.PushSettingsIfHost();
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
        DebugLog.Info($"GunGame settings received source=rpc host={hostId.m_SteamID} round={roundId} "
            + $"revision={revision} enabled={enabled} orderLength={weaponOrder?.Length ?? 0}");
        if (!GunGameState.TryAcceptSettingsSnapshot(hostId, roundId, revision, "gun-game-rpc"))
        {
            return;
        }
        GunGameState.ApplySettings(enabled, weaponOrder ?? string.Empty);
        DebugLog.Info($"[GunGame] Accepted settings via RPC: round={roundId} revision={revision}");
    }

    [CustomRPC]
    public void SyncGunGameLiveState(CSteamID hostId, string? progressData, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        DebugLog.Info($"GunGame live state received source=rpc host={hostId.m_SteamID} round={roundId} "
            + $"revision={revision} payloadLength={progressData?.Length ?? 0}");
        GunGameState.ApplyLiveState(hostId, progressData ?? string.Empty, roundId, revision, "gun-game-rpc");
    }
}