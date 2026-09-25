using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal static ConfigEntry<bool> NinjatatEnabled = null!;

    private void InitializeNinjatat()
    {
        const string section = "Game Mode Settings";
        NinjatatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Ninjatat",
            "Ninjas with Katanas fight Hunters with FG42s in single-life team takes. "
            + "Sides swap each take; when time expires, the first team to hold the first hardpoint "
            + "uncontested for 5 seconds wins the take.");

        NinjatatEnabled.SettingChanged += (_, _) =>
        {
            HuntModesState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, NinjatatDefinition.Value.ModId);
        ModeLobbyDataSync.RegisterKeys(NinjatatDefinition.Value.SettingsLobbyDataKey,
            NinjatatDefinition.Value.LiveLobbyDataKey);
        HuntModesState.SubscribeLifecycleEvents();
    }

    [CustomRPC]
    public void SyncNinjatatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.Ninjatat)
            || !NetworkAuthority.IsHostSender(info)
            || !HuntModesState.TryAcceptSettingsSnapshot(NinjatatDefinition.Value,
                hostId, roundId, revision))
        {
            return;
        }

        HuntModesState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncNinjatatLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int takeId, int takeWinnerId, int winnerId,
        float takeTimeRemaining, bool tieBreakActive, int tieBreakController,
        float tieBreakHoldProgress, int roundId, int revision, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.Ninjatat)
            || !NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        HuntModesState.ApplyLiveState(NinjatatDefinition.Value, hostId, assignmentsData,
            teamCount, scoresData, takeId, takeWinnerId, winnerId, takeTimeRemaining,
            tieBreakActive, tieBreakController, tieBreakHoldProgress, roundId, revision);
    }
}
