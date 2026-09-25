using HarmonyLib;

namespace Eights;

[HarmonyPatch(typeof(PlayerHealth), "Update")]
internal static class PlayerHealth_SndtatWeaponDrop_Patch
{
    private static void Prefix(PlayerHealth __instance)
    {
        if (!GameModeManager.IsBombModeActive
            || !__instance.IsOwner || __instance.health > 0f
            || __instance.health <= -1000f)
        {
            return;
        }

        __instance.shouldDropWeapon = true;
    }
}