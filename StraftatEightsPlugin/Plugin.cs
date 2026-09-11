using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine.SceneManagement;

[assembly: ComputerysModdingUtilities.StraftatMod(isVanillaCompatible: false)]

namespace StraftatEightsPlugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("RugbugRedfern.MyceliumNetworking")]
[BepInProcess("STRAFTAT.exe")]
public partial class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger = null!;
    internal static ConfigEntry<bool> DebugLogging = null!;

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
        DebugLogging = Config.Bind("Diagnostics", "Debug Logging", false,
            "Enable detailed multiplayer, scene, HUD, and snapshot diagnostics.");
        DebugLog.Reset();
        DebugLog.Info($"Startup: version={MyPluginInfo.PLUGIN_VERSION} scene={SceneManager.GetActiveScene().name}");

        InitializeSafely("compatibility checks", FishNetCompatibility.LogPreflight);
        InitializeSafely("Mycelium transport recovery", MyceliumTransportRecovery.Initialize);
        InitializeSafely("weapon service", WeaponService.Initialize);
        InitializeSafely("game mode manager", GameModeManager.Initialize);
        InitializeSafely("shared player outline", PlayerOutline.Initialize);

        InitializeSafely("global modifiers", InitializeGlobalModifiers);
        InitializeSafely("health settings", InitializeHealthSettings);
        InitializeSafely("global weapons", InitializeGlobalWeapons);
        InitializeSafely("default game mode", InitializeDefaultGameMode);
        InitializeSafely("Michael Meyers", InitializeMichaelMeyers);
        InitializeSafely("Exterminators", InitializeKillTheRat);
        InitializeSafely("HVT", InitializeHVT);
        InitializeSafely("One in the Chamber", InitializeOneInTheChamber);
        InitializeSafely("Hot Potato", InitializeHotPotato);
        InitializeSafely("Infidel", InitializeInfidel);
        InitializeSafely("Free For All", InitializeFFA);
        InitializeSafely("Juggernaut", InitializeJuggernaut);
        InitializeSafely("Gun Game", InitializeGunGame);
        InitializeSafely("Sniper Battle", InitializeSniperBattle);

        try
        {
            gameObject.AddComponent<GameModeHud>();
        }
        catch (Exception exception)
        {
            Logger.LogError("[GameModeHud] Startup failed: " + exception.GetBaseException().Message);
        }

        PatchAllSafely(new Harmony(MyPluginInfo.PLUGIN_GUID));
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    private static void InitializeSafely(string featureName, Action initialize)
    {
        try
        {
            initialize();
        }
        catch (Exception exception)
        {
            Logger.LogError($"[Startup] Failed to initialize {featureName}: "
                + exception.GetBaseException().Message);
        }
    }

    private static void PatchAllSafely(Harmony harmony)
    {
        Type[] patchTypes;
        try
        {
            patchTypes = AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly)
                .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();
        }
        catch (Exception exception)
        {
            Logger.LogError("[Harmony] Could not discover patch classes: "
                + exception.GetBaseException().Message);
            return;
        }

        foreach (Type patchType in patchTypes)
        {
            try
            {
                int patchedCount = harmony.CreateClassProcessor(patchType).Patch().Count();
                if (patchedCount == 0)
                {
                    const string reason = "no patch target was installed";
                    HarmonyPatchStatus.RecordFailure(patchType, reason);
                    Logger.LogError($"[Harmony] Disabled patch class {patchType.FullName}: {reason}.");
                    continue;
                }

                Logger.LogInfo($"[Harmony] Patched {patchType.FullName} ({patchedCount} method(s)).");
            }
            catch (Exception exception)
            {
                HarmonyPatchStatus.RecordFailure(patchType, exception.GetBaseException().Message);
                Logger.LogError($"[Harmony] Disabled patch class {patchType.FullName}: "
                    + exception.GetBaseException().Message);
            }
        }
    }

    private void Update()
    {
        DebugLog.Every("plugin-heartbeat", 2f,
            $"Heartbeat: lobby={MyceliumNetworking.MyceliumNetwork.InLobby} "
            + $"host={MyceliumNetworking.MyceliumNetwork.IsHost} "
            + $"lobbyHost={MyceliumNetworking.MyceliumNetwork.LobbyHost.m_SteamID} "
            + $"localPlayer={ClientInstance.Instance?.PlayerId ?? -1} "
            + $"players={PlayerLookup.GetConnectedPlayerIds().Count} "
            + $"mode={GameModeManager.ActiveMode} phase={GameModeManager.Phase} "
            + $"round={GameModeManager.RoundId} scene={SceneManager.GetActiveScene().name} "
            + $"mainMenu={PauseManager.Instance?.inMainMenu.ToString() ?? "missing"} "
            + $"victoryMenu={PauseManager.Instance?.inVictoryMenu.ToString() ?? "missing"}");
        MyceliumTransportRecovery.Update();
        GameModeManager.PeriodicPushIfHost();
        GameModeManager.PeriodicActiveModePushIfHost();
        GlobalModifiersState.PeriodicPushIfHost();
        HealthSettingsState.PeriodicPushIfHost();
        HealthSettingsState.ServerTick();
        WeaponSettingsState.UpdateLocalCycle();
        WeaponSettingsState.PeriodicPushIfHost();
        WeaponSettingsState.EnsureCycleLoadouts();
        GameModeManager.EnsureActiveModeLoadouts();
        PlayerOutline.EnforceOutline();
        RespawnProtection.Update();
    }
}