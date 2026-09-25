using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal static ConfigEntry<bool> HunttatEnabled = null!;

    private void InitializeHunttat()
    {
        const string section = "Game Mode Settings";
        HunttatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Hunttat",
            "Hunters with Tromblonj fight Rabbits with Smith Carbine in single-life team takes. "
            + "Sides swap each take; when time expires, the first team to hold the first hardpoint "
            + "uncontested for 5 seconds wins the take.");

        HunttatEnabled.SettingChanged += (_, _) =>
        {
            HuntModesState.PushSettingsIfHost(HunttatDefinition.Value,
                HunttatEnabled.Value);
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, HunttatDefinition.Value.ModId);
        ModeLobbyDataSync.RegisterKeys(HunttatDefinition.Value.SettingsLobbyDataKey,
            HunttatDefinition.Value.LiveLobbyDataKey);
        HuntModesState.SubscribeLifecycleEvents();
    }

    [CustomRPC]
    public void SyncHunttatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.Hunttat)
            || !NetworkAuthority.IsHostSender(info)
            || !HuntModesState.TryAcceptSettingsSnapshot(HunttatDefinition.Value,
                hostId, roundId, revision))
        {
            return;
        }

        HuntModesState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncHunttatLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int takeId, int takeWinnerId, int winnerId,
        float takeTimeRemaining, bool tieBreakActive, int tieBreakController,
        float tieBreakHoldProgress, int roundId, int revision, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.Hunttat)
            || !NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        HuntModesState.ApplyLiveState(HunttatDefinition.Value, hostId, assignmentsData,
            teamCount, scoresData, takeId, takeWinnerId, winnerId, takeTimeRemaining,
            tieBreakActive, tieBreakController, tieBreakHoldProgress, roundId, revision);
    }
}