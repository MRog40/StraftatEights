using HarmonyLib;

namespace Eights;

[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class FirstPersonController_DefaultVoid_Patch
{
    private static bool Prefix(FirstPersonController __instance)
    {
        return !StraftatState.HandleVoidFall(__instance);
    }
}