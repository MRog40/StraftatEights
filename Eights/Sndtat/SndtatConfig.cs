using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal const uint SndtatModId = 1618033996u;
    internal static ConfigEntry<bool> SndtatEnabled = null!;

    private void InitializeSndtat()
    {
        const string section = "Game Mode Settings";
        SndtatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Sndtat",
            "Two teams alternate between attacking and defending, with attackers planting a bomb at one of two sites and defenders defusing it or eliminating the attackers. "
            + "A take win awards 40 points with no respawns, and the first team to reach the configured point limit wins.");
        SndtatEnabled.SettingChanged += (_, _) =>
        {
            SndtatState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, SndtatModId);
        ModeLobbyDataSync.RegisterKeys(SndtatState.SettingsLobbyDataKey,
            SndtatState.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += SndtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += SndtatState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += SndtatState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += SndtatState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += SndtatState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += SndtatState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncSndtatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, bool ignoredLegacySpawnerFlag, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info)
            || !SndtatState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }

        SndtatState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncSndtatLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, string stateData, int takeId, int winnerId,
        int roundId, int revision, RPCInfo info)
    {
        if (!NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        SndtatState.ApplyLiveState(hostId, assignmentsData, teamCount, scoresData,
            stateData, takeId, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void RequestSndtatInteraction(int playerId, int commandId, bool pressed,
        bool lookingAtBomb, RPCInfo info)
    {
        SndtatState.HandleInteractionRequest(playerId, commandId, pressed,
            lookingAtBomb, info);
    }

    [CustomRPC]
    public void RequestSndtatBombDrop(int playerId, int commandId, RPCInfo info)
    {
        SndtatState.HandleBombDropRequest(playerId, commandId, info);
    }
}
