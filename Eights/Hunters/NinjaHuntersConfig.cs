using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal static ConfigEntry<bool> NinjaHuntersEnabled = null!;

    private void InitializeNinjaHunters()
    {
        const string section = "Game Mode Settings";
        NinjaHuntersEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Ninja Hunters",
            "Ninjas with Katanas fight Hunters with FG42s in single-life team takes. "
            + "Sides swap each take; when time expires, the first team to hold the first hardpoint "
            + "uncontested for 5 seconds wins the take.");

        NinjaHuntersEnabled.SettingChanged += (_, _) =>
        {
            HuntersState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, NinjaHuntersDefinition.Value.ModId);
        ModeLobbyDataSync.RegisterKeys(NinjaHuntersDefinition.Value.SettingsLobbyDataKey,
            NinjaHuntersDefinition.Value.LiveLobbyDataKey);
        MyceliumNetwork.LobbyCreated += HuntersState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += HuntersState.OnLobbyEntered;
        MyceliumNetwork.LobbyLeft += HuntersState.OnLobbyLeft;
        MyceliumNetwork.LobbyDataUpdated += HuntersState.OnLobbyDataUpdated;
        MyceliumNetwork.PlayerEntered += HuntersState.OnPlayerEntered;
        MyceliumNetwork.PlayerLeft += HuntersState.OnPlayerLeft;
    }

    [CustomRPC]
    public void SyncNinjaHuntersSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.NinjaHunters)
            || !NetworkAuthority.IsHostSender(info)
            || !HuntersState.TryAcceptSettingsSnapshot(NinjaHuntersDefinition.Value,
                hostId, roundId, revision))
        {
            return;
        }

        HuntersState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncNinjaHuntersLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int takeId, int takeWinnerId, int winnerId,
        float takeTimeRemaining, bool tieBreakActive, int tieBreakController,
        float tieBreakHoldProgress, int roundId, int revision, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.NinjaHunters)
            || !NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        HuntersState.ApplyLiveState(NinjaHuntersDefinition.Value, hostId, assignmentsData,
            teamCount, scoresData, takeId, takeWinnerId, winnerId, takeTimeRemaining,
            tieBreakActive, tieBreakController, tieBreakHoldProgress, roundId, revision);
    }
}
