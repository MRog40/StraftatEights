using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint AssassintatModId = 3141592658u;
    internal static ConfigEntry<bool> AssassintatEnabled = null!;

    private void InitializeAssassintat()
    {
        const string section = "Game Mode Settings";
        AssassintatEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Assassintat",
            "A hidden Assassintat hunts the public King while bodyguards protect the King, with role weapons unlocking after a delay. "
            + "Killing the King awards the Assassintat 50 points; eliminating the Assassintat awards the King 30 points, each surviving "
            + "bodyguard 10 points, and a bodyguard killer 20 extra points, with the first player to reach the configured point "
            + "limit winning.");

        AssassintatEnabled.SettingChanged += (_, _) =>
        {
            AssassintatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, AssassintatModId);
        ModeLobbyDataSync.RegisterKeys(AssassintatState.SettingsLobbyDataKey, AssassintatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += AssassintatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += AssassintatState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += AssassintatState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += AssassintatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += AssassintatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += AssassintatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncAssassintatSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !AssassintatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        AssassintatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncAssassintatLiveState(CSteamID hostId, int kingPlayerId, string scoresData,
        int winnerId, int takeId, bool weaponsUnlocked, int weaponDelaySeconds,
        float takeTimeRemaining, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        AssassintatState.ApplyLiveState(hostId, kingPlayerId, scoresData, winnerId, takeId,
            weaponsUnlocked, weaponDelaySeconds, takeTimeRemaining, roundId, revision);
    }

    [CustomRPC]
    public void SyncAssassintatRole(CSteamID hostId, int takeId, bool isAssassintat,
        bool isKing, bool announce, int weaponDelaySeconds, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        AssassintatState.ApplyLocalRole(hostId, takeId, isAssassintat, isKing, announce,
            weaponDelaySeconds);
    }

    [CustomRPC]
    public void AssassintatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}
