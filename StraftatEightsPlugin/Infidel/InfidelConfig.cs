using BepInEx.Configuration;
using MyceliumNetworking;
using Steamworks;

namespace StraftatEightsPlugin;

public partial class Plugin
{
    internal const uint InfidelModId = 2718281831u;
    internal static ConfigEntry<bool> InfidelEnabled = null!;

    private void InitializeInfidel()
    {
        const string section = "Game Mode Settings";
        InfidelEnabled = Config.Bind(section, "Infidel Enabled", false,
            "Host-controlled: one private Infidel role, delayed AK loadouts, slow movement, and role-based scoring.");

        InfidelEnabled.SettingChanged += (_, _) =>
        {
            InfidelState.PushSettingsIfHost();
            GameModeManager.OnSettingsChanged();
        };

        MyceliumNetwork.RegisterNetworkObject(this, InfidelModId);
        MyceliumNetwork.LobbyCreated += InfidelState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += InfidelState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += InfidelState.OnPlayerEntered;
    }

    [CustomRPC]
    public void SyncInfidelSettings(CSteamID hostId, int roundId, int revision, bool enabled)
    {
        if (!InfidelState.TryAcceptSettingsSnapshot(hostId, roundId, revision))
        {
            return;
        }
        InfidelState.ApplySettings(enabled);
    }

    [CustomRPC]
    public void SyncInfidelLiveState(CSteamID hostId, string scoresData, int winnerId,
        int subRoundId, bool weaponsUnlocked, int roundId, int revision)
    {
        InfidelState.ApplyLiveState(hostId, scoresData, winnerId, subRoundId,
            weaponsUnlocked, roundId, revision);
    }

    [CustomRPC]
    public void SyncInfidelRole(CSteamID hostId, int subRoundId, bool isInfidel, bool announce)
    {
        InfidelState.ApplyLocalRole(hostId, subRoundId, isInfidel, announce);
    }

    [CustomRPC]
    public void InfidelAnnounce(string text)
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.WriteLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}