using UnityEngine;

namespace StraftatEightsPlugin;

internal static class KillTheRatOutline
{
    private static PlayerHealth? _outlinedPlayer;
    private static GameMode _lastMode = GameMode.None;

    internal static void ResetState()
    {
        PlayerOutline.ClearApplied();
        _outlinedPlayer = null;
        _lastMode = GameMode.None;
    }

    internal static void EnforceOutline()
    {
        GameMode activeMode = GameModeManager.ActiveMode;
        if (_lastMode != activeMode)
        {
            PlayerOutline.ClearAll();
            _outlinedPlayer = null;
            _lastMode = activeMode;
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
                PlayerOutline.ClearApplied();
            }
            _outlinedPlayer = null;
            return;
        }

        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(KillTheRatState.CurrentRatPlayerId);
        if (health == null || !health.gameObject.activeInHierarchy)
        {
            if (_outlinedPlayer != null)
            {
                PlayerOutline.ClearApplied();
                _outlinedPlayer = null;
            }
            return;
        }

        if (_outlinedPlayer != health)
        {
            PlayerOutline.ClearApplied();
            _outlinedPlayer = health;
        }

        PlayerOutline.Apply(health, Color.yellow);
    }
}
