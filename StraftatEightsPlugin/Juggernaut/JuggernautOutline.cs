using UnityEngine;

namespace StraftatEightsPlugin;

// Gives the current Juggernaut a colored outline visible to other players, using the same outline
// shader properties the base game's enemy-outline feature uses (_ASEOutlineWidth/_ASEOutlineColor on
// each body part's SkinnedMeshRenderer material).
internal static class JuggernautOutline
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
            || !GameModeManager.IsActive(GameMode.Juggernaut))
        {
            return;
        }

        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(JuggernautState.CurrentJuggernautPlayerId);
        PlayerOutline.ApplySingleTarget(ref _outlinedPlayer, health, new Color(1f, 0.42f, 0f));
    }
}
