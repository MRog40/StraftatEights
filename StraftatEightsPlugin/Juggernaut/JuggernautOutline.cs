using UnityEngine;

namespace StraftatEightsPlugin;

// Gives the current Juggernaut a colored outline visible to other players, using the same outline
// shader properties the base game's enemy-outline feature uses (_ASEOutlineWidth/_ASEOutlineColor on
// each body part's SkinnedMeshRenderer material).
internal static class JuggernautOutline
{
    internal static void ResetState()
    {
        PlayerOutline.ClearApplied();
    }

    internal static void EnforceOutline()
    {
        if (GameModeManager.IsActive(GameMode.SniperBattle)
            || GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            PlayerOutline.ClearAll();
            return;
        }

        if (!GameModeManager.IsActive(GameMode.Juggernaut))
        {
            PlayerOutline.ClearApplied();
            return;
        }

        PlayerOutline.ClearAll();
        PlayerHealth? health = PlayerLookup.FindPlayerHealthById(JuggernautState.CurrentJuggernautPlayerId);
        if (health != null && health.gameObject.activeInHierarchy)
        {
            PlayerOutline.Apply(health, Color.red);
        }
    }
}
