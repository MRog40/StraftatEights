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
            case GameMode.NinjaHunters:
            case GameMode.RabbitHunters:
            case GameMode.TankBattle:
                HuntersState.OnRoundStarted();
                break;
            case GameMode.Infected:
                InfectedState.OnRoundStarted();
                break;
            case GameMode.HotPotInfected:
                HotPotInfectedState.OnRoundStarted();
                break;
            case GameMode.Nife:
                NifeState.OnRoundStarted();
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