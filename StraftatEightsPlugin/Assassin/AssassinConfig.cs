using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint AssassinModId = 3141592658u;
    internal static ConfigEntry<bool> AssassinEnabled = null!;

    private void InitializeAssassin()
    {
        const string section = "Game Mode Settings";
        AssassinEnabled = Config.Bind(section, "Assassin Enabled", false,
            "Host-controlled: one hidden Assassin, one public King, and delayed role loadouts.");

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
        int winnerId, int takeId, bool weaponsUnlocked, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info) || !GameModeManager.IsActive(GameMode.Assassin))
        {
            return;
        }

        AssassinState.ApplyLiveState(hostId, kingPlayerId, scoresData, winnerId, takeId,
            weaponsUnlocked, roundId, revision);
    }

    [CustomRPC]
    public void SyncAssassinRole(CSteamID hostId, int takeId, bool isAssassin,
        bool isKing, bool announce, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        AssassinState.ApplyLocalRole(hostId, takeId, isAssassin, isKing, announce);
    }

    [CustomRPC]
    public void AssassinAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        if (MatchLogs.Instance != null)
        {
            MatchLogs.Instance.WriteLocalLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}
