using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HVTOutline
{
    private static PlayerHealth? _outlinedPlayer;

    internal static void ResetState()
    {
        _outlinedPlayer = null;
    }

    internal static void EnforceOutline()
    {
        if (PlayerOutline.UpdateMode(GameModeManager.ActiveMode))
        {
            _outlinedPlayer = null;
        }

        if (GameModeManager.ShouldClearPlayerOutlines
            || !GameModeManager.IsActive(GameMode.HVT))
        {
            return;
        }

        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(HVTState.CurrentHVTPlayerId);
        PlayerOutline.ApplySingleTarget(ref _outlinedPlayer, health, Color.blue);
    }
}
