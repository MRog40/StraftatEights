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

        if (GameModeManager.ShouldClearPlayerOutlines)
        {
            return;
        }

        if (!GameModeManager.IsActive(GameMode.KillTheRat))
        {
            if (!GameModeManager.IsActive(GameMode.Juggernaut)
                && !GameModeManager.IsActive(GameMode.MichaelMeyers)
                && _outlinedPlayer != null)
            {
                PlayerOutline.ClearTarget(ref _outlinedPlayer);
            }
            _outlinedPlayer = null;
            return;
        }

        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(KillTheRatState.CurrentRatPlayerId);
        PlayerOutline.ApplySingleTarget(ref _outlinedPlayer, health, Color.yellow);
    }
}
