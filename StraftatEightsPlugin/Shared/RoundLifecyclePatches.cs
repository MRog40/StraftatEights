using HarmonyLib;

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

        switch (GameModeManager.ActiveMode)
        {
            case GameMode.Default:
                DefaultGameModeState.OnRoundStarted();
                break;
            case GameMode.MichaelMeyers:
                MichaelMeyersState.OnRoundStarted();
                break;
            case GameMode.OneInTheChamber:
                OneInTheChamberState.OnRoundStarted();
                break;
            case GameMode.HotPotato:
                HotPotatoState.OnRoundStarted();
                break;
            case GameMode.Infidel:
                InfidelState.OnRoundStarted();
                break;
            case GameMode.Assassin:
                AssassinState.OnRoundStarted();
                break;
            case GameMode.Hardpoint:
                HardpointState.OnRoundStarted();
                break;
            case GameMode.CaptureTheFlag:
                CaptureTheFlagState.OnRoundStarted();
                break;
            case GameMode.SearchAndDestroy:
                SearchAndDestroyState.OnRoundStarted();
                break;
            case GameMode.TeamDeathmatch:
                TeamDeathmatchState.OnRoundStarted();
                break;
        }

            GameModeRespawn.ReleaseInitialSpawnControls();

        if (!GameModeManager.ShouldIgnoreGlobalWeaponSettings)
        {
            WeaponAmmoTuning.ScheduleLocalAmmoHudRefresh();
        }

        GameModeHud.AnnounceActiveMode();
    }
}