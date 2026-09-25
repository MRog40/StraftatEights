using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

// Config bindings + RPC entry points for the "Juggertat" game mode (first blood becomes the
// Juggertat, everyone else hunts them, crown transfers on kill). See JuggertatState for the synced
// runtime values/logic and JuggertatPatches for where it's actually enforced/observed.
public partial class Plugin
{
    internal const uint JuggertatModId = 3141592656u;

    internal static ConfigEntry<bool> JuggertatEnabled = null!;

    private void InitializeJuggertat()
    {
        const string section = "Game Mode Settings";

        JuggertatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Juggertat",
            "The first killer becomes the powerful Juggertat, while everyone else hunts them for the crown. "
            + "Normal kills award 10 points, killing the Juggertat awards 20 points to the new holder, and the first player to "
            + "reach the configured point limit wins.");

        JuggertatEnabled.SettingChanged += (_, _) => { JuggertatState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };

        MyceliumNetwork.RegisterNetworkObject(this, JuggertatModId);
        ModeLobbyDataSync.RegisterKeys(JuggertatState.SettingsLobbyDataKey, JuggertatState.LiveLobbyDataKey);
        // LobbyCreated fires for the host (Steam LobbyCreated_t); LobbyEntered only fires for
        // joining clients (Steam LobbyEnter_t) - the host needs both to ever re-apply/reset on its
        // own session start, since LobbyEntered alone never fires when hosting.
        MyceliumNetwork.LobbyCreated += JuggertatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += JuggertatState.OnLobbyEntered;
        MyceliumNetwork.LobbyDataUpdated += JuggertatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += JuggertatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += JuggertatState.OnPlayerLeft;
    }

    // Invoked on every peer when the host (re)broadcasts its settings
    [CustomRPC]
    public void SyncJuggertatSettings(CSteamID hostId, int roundId, int revision, bool enabled, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        if (!JuggertatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        JuggertatState.ApplySettings(enabled);
    }

    // Invoked on every peer whenever the host (re)broadcasts who's currently the Juggertat and everyone's points
    [CustomRPC]
    public void SyncJuggertatLiveState(CSteamID hostId, int juggernautPlayerId, int juggernautKills,
        string pointsData, int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        JuggertatState.ApplyLiveState(hostId, juggernautPlayerId, juggernautKills, pointsData,
            roundId, revision);
    }

    // Host-broadcast chat announcement (crown changes, etc) - every peer just writes it locally
    [CustomRPC]
    public void JuggertatAnnounce(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.ReceiveAnnouncement(text, GameModeHud.AnnouncementDuration, true);
    }

    [CustomRPC]
    public void JuggertatAnnounceTarget(string text, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }
        GameModeHud.AnnounceTarget(ClientInstance.ReplaceAllPlayerNameTags(text));
    }
}
