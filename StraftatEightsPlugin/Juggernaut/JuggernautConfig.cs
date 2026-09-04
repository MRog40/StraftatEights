using BepInEx.Configuration;
using MyceliumNetworking;

namespace StraftatEightsPlugin;

// Config bindings + RPC entry points for the "Juggernaut" game mode (first blood becomes the
// Juggernaut, everyone else hunts them, crown transfers on kill). See JuggernautState for the synced
// runtime values/logic and JuggernautPatches for where it's actually enforced/observed.
public partial class Plugin
{
    internal const uint JuggernautModId = 3141592653u;

    internal static ConfigEntry<bool> JuggernautEnabled = null!;
    internal static ConfigEntry<int> JuggernautPointsPerSecond = null!;
    internal static ConfigEntry<int> JuggernautBonusHealthOnCrown = null!;
    internal static ConfigEntry<int> JuggernautHealthPerKill = null!;
    internal static ConfigEntry<int> JuggernautSpeedPercent = null!;
    internal static ConfigEntry<float> JuggernautRespawnDelaySeconds = null!;
    internal static ConfigEntry<bool> JuggernautShowOutline = null!;
    internal static ConfigEntry<bool> JuggernautShowScoreboard = null!;

    private void InitializeJuggernaut()
    {
        const string section = "Game Mode - Juggernaut";

        JuggernautEnabled = Config.Bind(section, "Enabled", false,
            "Host-controlled: turns on the Juggernaut game mode. First blood becomes the Juggernaut; everyone else hunts them to claim the crown.");
        JuggernautPointsPerSecond = Config.Bind(section, "Points Per Second", 5,
            new ConfigDescription("Host-controlled: points the Juggernaut earns per second while alive and holding the crown.", new AcceptableValueRange<int>(1, 50)));
        JuggernautBonusHealthOnCrown = Config.Bind(section, "Bonus Health On Crown", 25,
            new ConfigDescription("Host-controlled: bonus health granted whenever a player becomes (or reclaims) the Juggernaut.", new AcceptableValueRange<int>(0, 100)));
        JuggernautHealthPerKill = Config.Bind(section, "Health Per Kill", 10,
            new ConfigDescription("Host-controlled: bonus health granted to the Juggernaut for each kill they get while holding the crown.", new AcceptableValueRange<int>(0, 100)));
        JuggernautSpeedPercent = Config.Bind(section, "Juggernaut Speed %", 125,
            new ConfigDescription("Host-controlled: movement speed of whoever is currently the Juggernaut, as a percent of normal. Independent of Global Modifiers' Move Speed %.", new AcceptableValueRange<int>(100, 200)));
        JuggernautRespawnDelaySeconds = GameModeConfig.BindRespawnDelay(Config, section);
        JuggernautShowOutline = Config.Bind(section, "Show Juggernaut Outline", true,
            "Host-controlled: gives the current Juggernaut a visible colored outline for everyone else, so they're easy to spot.");
        JuggernautShowScoreboard = Config.Bind(section, "Show Scoreboard", true,
            "Host-controlled: shows a small on-screen scoreboard with everyone's Juggernaut points.");

        JuggernautEnabled.SettingChanged += (_, _) => JuggernautState.PushSettingsIfHost();
        JuggernautPointsPerSecond.SettingChanged += (_, _) => JuggernautState.PushSettingsIfHost();
        JuggernautBonusHealthOnCrown.SettingChanged += (_, _) => JuggernautState.PushSettingsIfHost();
        JuggernautHealthPerKill.SettingChanged += (_, _) => JuggernautState.PushSettingsIfHost();
        JuggernautSpeedPercent.SettingChanged += (_, _) => JuggernautState.PushSettingsIfHost();
        JuggernautRespawnDelaySeconds.SettingChanged += (_, _) => JuggernautState.PushSettingsIfHost();
        JuggernautShowOutline.SettingChanged += (_, _) => JuggernautState.PushSettingsIfHost();
        JuggernautShowScoreboard.SettingChanged += (_, _) => JuggernautState.PushSettingsIfHost();

        gameObject.AddComponent<JuggernautHud>();

        MyceliumNetwork.RegisterNetworkObject(this, JuggernautModId);
        // LobbyCreated fires for the host (Steam LobbyCreated_t); LobbyEntered only fires for
        // joining clients (Steam LobbyEnter_t) - the host needs both to ever re-apply/reset on its
        // own session start, since LobbyEntered alone never fires when hosting.
        MyceliumNetwork.LobbyCreated += JuggernautState.OnLobbyEntered;
        MyceliumNetwork.LobbyEntered += JuggernautState.OnLobbyEntered;
        MyceliumNetwork.PlayerEntered += JuggernautState.OnPlayerEntered;
    }

    // Invoked on every peer when the host (re)broadcasts its settings
    [CustomRPC]
    public void SyncJuggernautSettings(bool enabled, int pointsPerSecond, int bonusHealthOnCrown, int healthPerKill, int speedPercent, float respawnDelaySeconds, bool showOutline, bool showScoreboard)
    {
        Logger.LogInfo($"[Juggernaut] Received settings sync: enabled={enabled} speed%={speedPercent}");
        JuggernautState.ApplySettings(enabled, pointsPerSecond, bonusHealthOnCrown, healthPerKill, speedPercent, respawnDelaySeconds, showOutline, showScoreboard);
    }

    // Invoked on every peer whenever the host (re)broadcasts who's currently the Juggernaut and everyone's points
    [CustomRPC]
    public void SyncJuggernautLiveState(int juggernautPlayerId, string pointsData)
    {
        JuggernautState.ApplyLiveState(juggernautPlayerId, pointsData);
    }

    // Host-broadcast chat announcement (crown changes, etc) - every peer just writes it locally
    [CustomRPC]
    public void JuggernautAnnounce(string text)
    {
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.WriteLog(ClientInstance.ReplaceAllPlayerNameTags(text));
        }
    }
}
