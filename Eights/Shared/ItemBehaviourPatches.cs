using HarmonyLib;

namespace Eights;

[HarmonyPatch(typeof(ItemBehaviour), "Start")]
internal static class ItemBehaviour_Start_ParentGuard_Patch
{
    private static void Prefix(ItemBehaviour __instance, out bool __state)
    {
        __state = false;
        if (__instance == null || !__instance || __instance.transform.parent != null
            || __instance.dispenserStart || __instance.gameObject.name == "Pig Held Item")
        {
            return;
        }

        __instance.dispenserStart = true;
        __state = true;
    }

    private static void Postfix(ItemBehaviour __instance, bool __state)
    {
        if (__state && __instance != null && __instance)
        {
            __instance.dispenserStart = false;
        }
    }
}