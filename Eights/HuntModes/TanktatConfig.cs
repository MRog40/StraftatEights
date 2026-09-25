using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal static ConfigEntry<bool> TanktatEnabled = null!;

    private void InitializeTanktat()
    {
        const string section = "Game Mode Settings";
        TanktatEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Tanktat",
            "Both teams use HK_Caws. Every player has 400 health, cannot regenerate, jump, or slide, "
            + "and stays crouched while moving slowly in single-life team takes. Sides swap each take; "
            + "when time expires, the first team to hold the first hardpoint uncontested for 5 seconds "
            + "wins the take.");

        TanktatEnabled.SettingChanged += (_, _) =>
        {
            HuntModesState.PushSettingsIfHost(TanktatDefinition.Value, TanktatEnabled.Value);
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, TanktatDefinition.Value.ModId);
        ModeLobbyDataSync.RegisterKeys(TanktatDefinition.Value.SettingsLobbyDataKey,
            TanktatDefinition.Value.LiveLobbyDataKey);
        HuntModesState.SubscribeLifecycleEvents();
    }

    [CustomRPC]
    public void SyncTanktatSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.Tanktat)
            || !NetworkAuthority.IsHostSender(info)
            || !HuntModesState.TryAcceptSettingsSnapshot(TanktatDefinition.Value,
                hostId, roundId, revision))
        {
            return;
        }

        HuntModesState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncTanktatLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int takeId, int takeWinnerId, int winnerId,
        float takeTimeRemaining, bool tieBreakActive, int tieBreakController,
        float tieBreakHoldProgress, int roundId, int revision, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.Tanktat)
            || !NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        HuntModesState.ApplyLiveState(TanktatDefinition.Value, hostId, assignmentsData,
            teamCount, scoresData, takeId, takeWinnerId, winnerId, takeTimeRemaining,
            tieBreakActive, tieBreakController, tieBreakHoldProgress, roundId, revision);
    }
}