using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: ComputerysModdingUtilities.StraftatMod(isVanillaCompatible: false)]

namespace Eights;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInDependency("RugbugRedfern.MyceliumNetworking")]
[BepInDependency("kestrel.straftat.modmenu", BepInDependency.DependencyFlags.SoftDependency)]
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

        InitializeSafely("compatibility checks", FishNetCompatibility.LogPreflight);
        InitializeSafely("Mycelium transport recovery", MyceliumTransportRecovery.Initialize);
        InitializeSafely("weapon service", WeaponService.Initialize);
        InitializeSafely("game mode manager", GameModeManager.Initialize);
        InitializeSafely("shared player lookup", PlayerLookup.Initialize);
        InitializeSafely("ModMenu integration", ModMenuIntegration.Initialize);
        InitializeSafely("shared player outline", PlayerOutline.Initialize);
        InitializeSafely("blood cleanup", InitializeBloodCleanup);

        InitializeSafely("global modifiers", InitializeGlobalModifiers);
        InitializeSafely("health settings", InitializeHealthSettings);
        InitializeSafely("global weapons", InitializeGlobalWeapons);
        InitializeSafely("default game mode", InitializeDefaultGameMode);
        InitializeSafely("Michael Meyers", InitializeMichaelMeyers);
        InitializeSafely("Kill The Rat", InitializeKillTheRat);
        InitializeSafely("HVT", InitializeHVT);
        InitializeSafely("Infected", InitializeInfected);
        InitializeSafely("Hot Pot: Infected", InitializeHotPotInfected);
        InitializeSafely("One in the Chamber", InitializeOneInTheChamber);
        InitializeSafely("Hot Potato", InitializeHotPotato);
        InitializeSafely("Infidel", InitializeInfidel);
        InitializeSafely("Assassin", InitializeAssassin);
        InitializeSafely("Free For All", InitializeFFA);
        InitializeSafely("Nife", InitializeNife);
        InitializeSafely("Juggernaut", InitializeJuggernaut);
        InitializeSafely("Gun Game", InitializeGunGame);
        InitializeSafely("Sniper Battle", InitializeSniperBattle);
        InitializeSafely("Hardpoint", InitializeHardpoint);
        InitializeSafely("Capture the Flag", InitializeCaptureTheFlag);
        InitializeSafely("Search and Destroy", InitializeSearchAndDestroy);
        InitializeSafely("Team Deathmatch", InitializeTeamDeathmatch);
        InitializeSafely("Ninja Hunters", InitializeNinjaHunters);
        InitializeSafely("Rabbit Hunt", InitializeRabbitHunters);
        InitializeSafely("Tank Battle", InitializeTankBattle);
        Config.Save();

        try
        {
            gameObject.AddComponent<GameModeHud>();
        }
        catch (Exception exception)
        {
            Logger.LogError("[GameModeHud] Startup failed: " + exception.GetBaseException().Message);
        }

        PatchAllSafely(new Harmony(MyPluginInfo.PLUGIN_GUID));
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
        BloodCleanupState.Update();
        PositionMarkerDebug.Update();
        MyceliumTransportRecovery.Update();
        GameModeManager.EnsureVanillaScene();
        GameModeManager.UpdateRoundEndCountdown();
        GameModeManager.PeriodicPushIfHost();
        GameModeManager.PeriodicActiveModePushIfHost();
        GameModeManager.PollLobbyStateIfClient();
        PlayerNameSync.PeriodicPushIfHost();
        PlayerNameSync.PollIfClient();
        DefaultGameModeState.ServerTick(Time.unscaledDeltaTime);
        DefaultGameModeState.ClientTick(Time.unscaledDeltaTime);
        CaptureTheFlagState.ServerTick(Time.unscaledDeltaTime);
        HardpointState.ServerTick(Time.unscaledDeltaTime);
        JuggernautState.ServerTick(Time.unscaledDeltaTime);
        HVTState.ServerTick(Time.unscaledDeltaTime);
        KillTheRatState.ServerTick(Time.unscaledDeltaTime);
        MichaelMeyersState.ServerTick(Time.unscaledDeltaTime);
        MichaelMeyersState.ClientTick(Time.unscaledDeltaTime);
        InfidelState.ServerTick(Time.unscaledDeltaTime);
        InfidelState.ClientTick(Time.unscaledDeltaTime);
        AssassinState.ServerTick(Time.unscaledDeltaTime);
        AssassinState.ClientTick(Time.unscaledDeltaTime);
        SearchAndDestroyState.ServerTick(Time.unscaledDeltaTime);
        HuntersState.ServerTick(Time.unscaledDeltaTime);
        ModeTimeoutState.ServerTick(Time.unscaledDeltaTime);
        ModeTimeoutState.ClientTick(Time.unscaledDeltaTime);
        SearchAndDestroyState.PollLocalInput();
        SearchAndDestroyState.ApplyLocalMovementLock();
        GlobalModifiersState.PeriodicPushIfHost();
        GlobalModifiersState.PollSettingsIfClient();
        HealthSettingsState.PeriodicPushIfHost();
        HealthSettingsState.PollSettingsIfClient();
        GameModeHud.PeriodicPushTakeResult();
        HealthSettingsState.ServerTick();
        WeaponSettingsState.PeriodicPushIfHost();
        WeaponSettingsState.PollSettingsIfClient();
        GameModeManager.EnsureActiveModeLoadouts();
        WeaponSettingsState.EnsureDefaultKnifeLoadouts();
        PlayerOutline.EnforceOutline();
        TeammateMarker.Enforce();
        HardpointMarker.Update();
        HuntersMarker.Update();
        CaptureTheFlagMarker.Update();
        SearchAndDestroyMarker.Update();
        RespawnProtection.Update();
    }
}