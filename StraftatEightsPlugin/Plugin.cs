using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace StraftatEightsPlugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("RugbugRedfern.MyceliumNetworking")]
[BepInProcess("STRAFTAT.exe")]
public partial class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger = null!;

    // Used by feature modules that need a persistent MonoBehaviour to host coroutines (e.g. delayed
    // auto-respawn) or attach child components (e.g. a HUD) to - the plugin object outlives scenes.
    internal static Plugin Instance = null!;

    // Each feature module (GlobalModifiers, Juggernaut, and future game modes) contributes an
    // InitializeXxx() call here and lives in its own folder as a `partial class Plugin` (for
    // config/RPC) plus its own state/patch classes - see GlobalModifiers/ for the reference layout.
    // Shared/ holds cross-mode helpers (player lookups, team/weapon utilities) so future modes don't
    // duplicate them.
    private void Awake()  
    {
        Instance = this;
        Logger = base.Logger;

        InitializeGlobalModifiers();
        InitializeHealthSettings();
        InitializeJuggernaut();

        new Harmony(MyPluginInfo.PLUGIN_GUID).PatchAll();
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private void Update()
    {
        GlobalModifiersState.PeriodicPushIfHost();
        HealthSettingsState.PeriodicPushIfHost();
        HealthSettingsState.ServerTick();
        JuggernautState.PeriodicPushSettingsIfHost();
    }
}

