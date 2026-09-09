using HarmonyLib;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PauseManager), "InvokeRoundStarted")]
internal static class PauseManager_RoundLifecycle_Patch
{
    private static void Postfix()
    {
        GameModeManager.BeginRound();

        switch (GameModeManager.ActiveMode)
        {
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
        }

        if (!GameModeManager.ShouldIgnoreGlobalWeaponSettings)
        {
            WeaponAmmoTuning.ScheduleLocalAmmoHudRefresh();
        }

        GameModeHud.AnnounceActiveMode();
    }
}