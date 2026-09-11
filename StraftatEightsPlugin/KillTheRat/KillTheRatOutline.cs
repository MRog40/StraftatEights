using UnityEngine;

namespace StraftatEightsPlugin;

internal static class KillTheRatOutline
{
    private static PlayerHealth? _outlinedPlayer;

    internal static void ResetState()
    {
        _outlinedPlayer = null;
    }

    internal static void EnforceOutline()
    {
        GameMode activeMode = GameModeManager.ActiveMode;
        if (PlayerOutline.UpdateMode(activeMode))
        {
            _outlinedPlayer = null;
        }

        if (GameModeManager.ShouldClearPlayerOutlines
            || !GameModeManager.IsActive(GameMode.KillTheRat))
        {
            return;
        }

        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(KillTheRatState.CurrentRatPlayerId);
        PlayerOutline.ApplySingleTarget(ref _outlinedPlayer, health, Color.yellow);
    }
}
