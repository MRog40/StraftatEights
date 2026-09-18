using HarmonyLib;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PlayerHealth), "Update")]
internal static class PlayerHealth_SearchAndDestroyWeaponDrop_Patch
{
    private static void Prefix(PlayerHealth __instance)
    {
        if (!GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || !__instance.IsOwner || __instance.health > 0f
            || __instance.health <= -1000f)
        {
            return;
        }

        __instance.shouldDropWeapon = true;
    }
}