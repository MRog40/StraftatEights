using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

// Config bindings + RPC entry points for the "Juggernaut" game mode (first blood becomes the
// Juggernaut, everyone else hunts them, crown transfers on kill). See JuggernautState for the synced
// runtime values/logic and JuggernautPatches for where it's actually enforced/observed.
public partial class Plugin
{
    internal const uint JuggernautModId = 3141592656u;

    internal static ConfigEntry<bool> JuggernautEnabled = null!;

    private void InitializeJuggernaut()
    {
        const string section = "Game Mode Settings";

        JuggernautEnabled = Config.Bind(section, "Juggernaut Enabled", false,
            "Host-controlled: first blood becomes the Juggernaut; everyone else hunts them for the crown.");

        JuggernautEnabled.SettingChanged += (_, _) => { JuggernautState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };

        MyceliumNetwork.RegisterNetworkObject(this, JuggernautModId);
        ModeLobbyDataSync.RegisterKeys(JuggernautState.SettingsLobbyDataKey, JuggernautState.LiveLobbyDataKey);
        // LobbyCreated fires for the host (Steam LobbyCreated_t); LobbyEntered only fires for
        // joining clients (Steam LobbyEnter_t) - the host needs both to ever re-apply/reset on its
        // own session start, since LobbyEntered alone never fires when hosting.
        MyceliumNetwork.LobbyCreated += JuggernautState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += JuggernautState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += JuggernautState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += JuggernautState.OnPlayerEntered;
    }

    // Invoked on every peer when the host (re)broadcasts its settings
    [CustomRPC]
    public void SyncJuggernautSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!JuggernautState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        Logger.LogInfo($"[Juggernaut] Received settings sync: enabled={enabled}");
        JuggernautState.ApplySettings(enabled);
    }

    // Invoked on every peer whenever the host (re)broadcasts who's currently the Juggernaut and everyone's points
    [CustomRPC]
    public void SyncJuggernautLiveState(CSteamID hostId, int juggernautPlayerId, int juggernautKills,
        string pointsData, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!GameModeManager.IsActive(GameMode.Juggernaut))
        {
            return;
        }
        JuggernautState.ApplyLiveState(hostId, juggernautPlayerId, juggernautKills, pointsData,
            roundId, revision);
    }

    // Host-broadcast chat announcement (crown changes, etc) - every peer just writes it locally
    [CustomRPC]
    public void JuggernautAnnounce(string text, RPCInfo info)
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

    [CustomRPC]
    public void JuggernautAnnounceTarget(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.AnnounceTarget(ClientInstance.ReplaceAllPlayerNameTags(text));
    }
}
