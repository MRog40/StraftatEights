using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint SniperBattleModId = 1618033992u;
    internal static ConfigEntry<bool> SniperBattleEnabled = null!;
    internal static ConfigEntry<int> SniperBattlePointsToWin = null!;

    private void InitializeSniperBattle()
    {
        const string section = "Game Mode Settings";
        SniperBattleEnabled = Config.Bind(section, "Sniper Battle Enabled", false,
            "Host-controlled: players respawn with only the M2000, which has unlimited ammo, and score one point per kill.");
        SniperBattlePointsToWin = Config.Bind(section, "Sniper Battle Points To Win", 10,
            new ConfigDescription("Host-controlled: points required to win Sniper Battle.", new AcceptableValueRange<int>(3, 30)));

        SniperBattleEnabled.SettingChanged += (_, _) => { SniperBattleState.PushSettingsIfHost(); GameModeManager.OnSettingsChanged(); };
        SniperBattlePointsToWin.SettingChanged += (_, _) => SniperBattleState.PushSettingsIfHost();

        MyceliumNetwork.RegisterNetworkObject(this, SniperBattleModId);
        MyceliumNetwork.LobbyCreated += SniperBattleState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += SniperBattleState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += SniperBattleState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncSniperBattleSettings(CSteamID hostId, int roundId, int revision,
        bool enabled, int pointsToWin)
    {
        if (!SniperBattleState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        SniperBattleState.ApplySettings(enabled, pointsToWin);
    }

    [CustomRPC]
    public void SyncSniperBattleLiveState(CSteamID hostId, string pointsData, int winnerId, int roundId, int revision)
    {
        SniperBattleState.ApplyLiveState(hostId, pointsData, winnerId, roundId, revision);
    }

    [CustomRPC]
    public void SniperBattleAnnounce(string text)
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.WriteLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}