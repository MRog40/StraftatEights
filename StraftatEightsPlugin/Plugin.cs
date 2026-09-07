using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;

[assembly: ComputerysModdingUtilities.StraftatMod(isVanillaCompatible: false)]

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

        FishNetCompatibility.LogPreflight();
        WeaponService.Initialize();
        GameModeManager.Initialize();
        gameObject.AddComponent<GameModeHud>();

        InitializeGlobalModifiers();
        InitializeHealthSettings();
        InitializeGlobalWeapons();
        InitializeDefaultGameMode();
        InitializeMichaelMeyers();
        InitializeFFA();
        InitializeJuggernaut();
        InitializeGunGame();
        InitializeSniperBattle();

        PatchAllSafely(new Harmony(MyPluginInfo.PLUGIN_GUID));
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private static void PatchAllSafely(Harmony harmony)
    {
        Type[] patchTypes = AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly)
            .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        foreach (Type patchType in patchTypes)
        {
            try
            {
                int patchedCount = harmony.CreateClassProcessor(patchType).Patch().Count();
                Logger.LogInfo($"[Harmony] Patched {patchType.FullName} ({patchedCount} method(s)).");
            }
            catch (Exception exception)
            {
                Logger.LogError($"[Harmony] Disabled patch class {patchType.FullName}: "
                    + exception.GetBaseException().Message);
            }
        }
    }

    private void Update()
    {
        GameModeManager.PeriodicPushIfHost();
        GlobalModifiersState.PeriodicPushIfHost();
        HealthSettingsState.PeriodicPushIfHost();
        HealthSettingsState.ServerTick();
        WeaponSettingsState.UpdateLocalCycle();
        WeaponSettingsState.PeriodicPushIfHost();
        WeaponSettingsState.EnsureCycleLoadouts();
        GameModeManager.EnsureActiveModeLoadouts();
    }
}

