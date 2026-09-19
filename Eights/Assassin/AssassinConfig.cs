using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint AssassinModId = 3141592658u;
    internal static ConfigEntry<bool> AssassinEnabled = null!;

    private void InitializeAssassin()
    {
        const string section = "Game Mode Settings";
        AssassinEnabled = ModeConfigMigration.BindModeEnabled(Config, section, "Assassin",
            "A hidden Assassin hunts the public King while bodyguards protect the King, with role weapons unlocking after a delay. "
            + "Killing the King awards the Assassin 50 points; eliminating the Assassin awards the King 30 points, each surviving "
            + "bodyguard 10 points, and a bodyguard killer 20 extra points, with the first player to reach the configured point "
            + "limit winning.");

        AssassinEnabled.SettingChanged += (_, _) =>
        {
            AssassinState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, AssassinModId);
        ModeLobbyDataSync.RegisterKeys(AssassinState.SettingsLobbyDataKey, AssassinState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += AssassinState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += AssassinState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += AssassinState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += AssassinState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += AssassinState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += AssassinState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncAssassinSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !AssassinState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        AssassinState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncAssassinLiveState(CSteamID hostId, int kingPlayerId, string scoresData,
        int winnerId, int takeId, bool weaponsUnlocked, int weaponDelaySeconds,
        float takeTimeRemaining, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        AssassinState.ApplyLiveState(hostId, kingPlayerId, scoresData, winnerId, takeId,
            weaponsUnlocked, weaponDelaySeconds, takeTimeRemaining, roundId, revision);
    }

    [CustomRPC]
    public void SyncAssassinRole(CSteamID hostId, int takeId, bool isAssassin,
        bool isKing, bool announce, int weaponDelaySeconds, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        AssassinState.ApplyLocalRole(hostId, takeId, isAssassin, isKing, announce,
            weaponDelaySeconds);
    }

    [CustomRPC]
    public void AssassinAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }
}
