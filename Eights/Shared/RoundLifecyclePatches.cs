using HarmonyLib;
using MyceliumNetworking;

namespace Eights;

[HarmonyPatch(typeof(PauseManager), "InvokeRoundStarted")]
internal static class PauseManager_RoundLifecycle_Patch
{
    private static void Postfix()
    {
        if (GameModeManager.IsVanillaScene)
        {
            GameModeManager.EnsureVanillaScene();
            return;
        }

        if (!GameModeManager.BeginRound())
        {
            return;
        }

        Plugin.Logger.LogInfo($"[GameMode] Round started: mode={GameModeManager.ActiveMode} "
            + $"host={MyceliumNetwork.IsHost} phase={GameModeManager.Phase}");

        switch (GameModeManager.ActiveMode)
        {
            case GameMode.Straftat:
                StraftatState.OnRoundStarted();
                break;
            case GameMode.Michaeltat:
                MichaeltatState.OnRoundStarted();
                break;
            case GameMode.Chambertat:
                ChambertatState.OnRoundStarted();
                break;
            case GameMode.Potatotat:
                PotatotatState.OnRoundStarted();
                break;
            case GameMode.Infideltat:
                InfideltatState.OnRoundStarted();
                break;
            case GameMode.Assassintat:
                AssassintatState.OnRoundStarted();
                break;
            case GameMode.Hardtat:
                HardtatState.OnRoundStarted();
                break;
            case GameMode.Capturetat:
                CapturetatState.OnRoundStarted();
                break;
            case GameMode.Sndtat:
            case GameMode.Countertat:
                SndtatState.OnRoundStarted();
                break;
            case GameMode.Tdmtat:
                TdmtatState.OnRoundStarted();
                break;
            case GameMode.Ninjatat:
            case GameMode.Hunttat:
            case GameMode.Tanktat:
                HuntModesState.OnRoundStarted();
                break;
            case GameMode.Infectedtat:
                InfectedtatState.OnRoundStarted();
                break;
            case GameMode.PotatoInftat:
                PotatoInftatState.OnRoundStarted();
                break;
            case GameMode.Nifetat:
                NifetatState.OnRoundStarted();
                break;
        }

        if (!GameModeManager.ShouldIgnoreGlobalWeaponSettings)
        {
            WeaponAmmoTuning.ScheduleLocalAmmoHudRefresh();
        }

        GameModeHud.AnnounceActiveMode();
    }
}

[HarmonyPatch(typeof(GameManager), "SetStartTime")]
internal static class GameManager_PreRoundTimer_Patch
{
    private static void Prefix(ref float serverTimeTillStart)
    {
        if (!GameModeManager.IsCustomMode)
        {
            return;
        }

        if (serverTimeTillStart < GameModeManager.EffectivePreRoundSeconds)
        {
            serverTimeTillStart = GameModeManager.EffectivePreRoundSeconds;
        }
    }
}