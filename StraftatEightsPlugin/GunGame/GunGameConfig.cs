using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint GunGameModId = 1618033990u;
    internal static ConfigEntry<bool> GunGameEnabled = null!;
    internal static ConfigEntry<string> GunGameWeaponOrder = null!;

    private void InitializeGunGame()
    {
        const string section = "Game Mode Settings";
        GunGameEnabled = Config.Bind(section, "Gun Game Enabled", false,
            "Host-controlled: players advance through the weapon list with each kill.");
        GunGameWeaponOrder = Config.Bind(section, "Gun Game Weapon Order",
            "Glock, Webley, SMG, Bukanee, Shotgun, AR15, QCW05, HK_G11, M2000, Couperet",
            "Host-controlled: exact prefab IDs in progression order.");

        GunGameEnabled.SettingChanged += (_, _) => { GunGameState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };
        GunGameWeaponOrder.SettingChanged += (_, _) => GunGameState.PushSettingsIfHost();
        MyceliumNetwork.RegisterNetworkObject(this, GunGameModId);
        ModeLobbyDataSync.RegisterKeys(GunGameState.SettingsLobbyDataKey, GunGameState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += GunGameState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += GunGameState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += GunGameState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += GunGameState.OnPlayerEntered;
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
        Plugin.Logger.LogInfo($"[GunGame] Accepted settings via RPC: round={roundId} revision={revision}");
    }

    [CustomRPC]
    public void SyncGunGameLiveState(CSteamID hostId, string? progressData, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GameModeManager.IsActive(GameMode.GunGame))
        {
            return;
        }
        DebugLog.Info($"GunGame live state received source=rpc host={hostId.m_SteamID} round={roundId} "
            + $"revision={revision} payloadLength={progressData?.Length ?? 0}");
        GunGameState.ApplyLiveState(hostId, progressData ?? string.Empty, roundId, revision, "gun-game-rpc");
    }
}