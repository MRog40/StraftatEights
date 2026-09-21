using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace Eights;

public partial class Plugin
{
    internal static ConfigEntry<bool> RabbitHuntersEnabled = null!;

    private void InitializeRabbitHunters()
    {
        const string section = "Game Mode Settings";
        RabbitHuntersEnabled = ModeConfigMigration.BindModeEnabled(Config, section,
            "Rabbit Hunters",
            "Hunters with Tromblonjs fight Rabbits with dual-wielded Stylus weapons in single-life team takes. "
            + "Sides swap each take; when time expires, the first team to hold the first hardpoint "
            + "uncontested for 5 seconds wins the take.");

        RabbitHuntersEnabled.SettingChanged += (_, _) =>
        {
            HuntersState.PushSettingsIfHost(RabbitHuntersDefinition.Value,
                RabbitHuntersEnabled.Value);
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, RabbitHuntersDefinition.Value.ModId);
        ModeLobbyDataSync.RegisterKeys(RabbitHuntersDefinition.Value.SettingsLobbyDataKey,
            RabbitHuntersDefinition.Value.LiveLobbyDataKey);
    }

    [CustomRPC]
    public void SyncRabbitHuntersSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.RabbitHunters)
            || !NetworkAuthority.IsHostSender(info)
            || !HuntersState.TryAcceptSettingsSnapshot(RabbitHuntersDefinition.Value,
                hostId, roundId, revision))
        {
            return;
        }

        HuntersState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncRabbitHuntersLiveState(CSteamID hostId, string assignmentsData,
        int teamCount, string scoresData, int takeId, int takeWinnerId, int winnerId,
        float takeTimeRemaining, bool tieBreakActive, int tieBreakController,
        float tieBreakHoldProgress, int roundId, int revision, RPCInfo info)
    {
        if (!GameModeManager.IsActive(GameMode.RabbitHunters)
            || !NetworkAuthority.IsHostSender(info))
        {
            return;
        }

        HuntersState.ApplyLiveState(RabbitHuntersDefinition.Value, hostId, assignmentsData,
            teamCount, scoresData, takeId, takeWinnerId, winnerId, takeTimeRemaining,
            tieBreakActive, tieBreakController, tieBreakHoldProgress, roundId, revision);
    }
}