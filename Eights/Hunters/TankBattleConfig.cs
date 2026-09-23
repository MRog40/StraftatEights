using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal static ConfigEntry<bool> TankBattleEnabled = null!;

    private void InitializeTankBattle()
    {
        const string section = "Game Mode Settings";
        TankBattleEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Tank Battle",
            "Both teams use HK_Caws. Every player has 400 health, cannot regenerate, jump, or slide, "
            + "and stays crouched while moving slowly in single-life team takes. Sides swap each take; "
            + "when time expires, the first team to hold the first hardpoint uncontested for 5 seconds "
            + "wins the take.");

        TankBattleEnabled.SettingChanged += (_, _) =>
        {
            HuntersState.PushSettingsIfHost(TankBattleDefinition.Value, TankBattleEnabled.Value);
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, TankBattleDefinition.Value.ModId);
        ModeLobbyDataSync.RegisterKeys(TankBattleDefinition.Value.SettingsLobbyDataKey,
            TankBattleDefinition.Value.LiveLobbyDataKey);
        HuntersState.SubscribeLifecycleEvents();
    }

    [CustomRPC]
    public void SyncTankBattleSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.TankBattle)
            || !NetworkAuthority.IsHostSender(info)
            || !HuntersState.TryAcceptSettingsSnapshot(TankBattleDefinition.Value,
                hostId, roundId, revision))
        {
            return;
        }

        HuntersState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncTankBattleLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int takeId, int takeWinnerId, int winnerId,
        float takeTimeRemaining, bool tieBreakActive, int tieBreakController,
        float tieBreakHoldProgress, int roundId, int revision, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.TankBattle)
            || !NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        HuntersState.ApplyLiveState(TankBattleDefinition.Value, hostId, assignmentsData,
            teamCount, scoresData, takeId, takeWinnerId, winnerId, takeTimeRemaining,
            tieBreakActive, tieBreakController, tieBreakHoldProgress, roundId, revision);
    }
}