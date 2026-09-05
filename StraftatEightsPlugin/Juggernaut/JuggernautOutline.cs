using UnityEngine;

namespace StraftatEightsPlugin;

// Gives the current Juggernaut a colored outline visible to other players, using the same outline
// shader properties the base game's enemy-outline feature uses (_ASEOutlineWidth/_ASEOutlineColor on
// each body part's SkinnedMeshRenderer material).
internal static class JuggernautOutline
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

        if (!GameModeManager.IsActive(GameMode.Juggernaut))
        {
            if (_outlinedPlayer != null)
            {
                PlayerOutline.ClearApplied();
                _outlinedPlayer = null;
            }
            return;
        }

        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(JuggernautState.CurrentJuggernautPlayerId);
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
            PlayerOutline.Apply(health, Color.red);
            _outlinedPlayer = health;
        }
    }
}
