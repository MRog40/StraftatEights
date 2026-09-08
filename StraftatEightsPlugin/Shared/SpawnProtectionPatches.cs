using HarmonyLib;
using System.Reflection;

namespace StraftatEightsPlugin;

[HarmonyPatch]
internal static class PlayerHealth_SpawnProtectionDamage_Patch
{
    private static MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(PlayerHealth),
            "RpcLogic___RemoveHealth_",
            method => method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType == typeof(float));
    }

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(PlayerHealth __instance, float damage)
    {
        return damage <= 0f || !SpawnProtectionState.IsProtected(__instance);
    }
}
