using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using System.Reflection;

namespace StraftatEightsPlugin;

[HarmonyPatch]
internal static class PlayerHealth_SpawnProtectionDamage_Patch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        MethodInfo? removeHealth = AccessTools.Method(typeof(PlayerHealth), "RemoveHealth",
            new[] { typeof(float) });
        if (removeHealth != null)
        {
            yield return removeHealth;
        }

        MethodInfo? generatedMethod = FishNetCompatibility.FindGeneratedMethod(typeof(PlayerHealth),
            "RpcLogic___RemoveHealth_",
            method => method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType == typeof(float));
        if (generatedMethod != null && generatedMethod != removeHealth)
        {
            yield return generatedMethod;
        }
    }

    private static bool Prepare() => TargetMethods().Any();

    private static bool Prefix(PlayerHealth __instance, float damage)
    {
        return damage <= 0f || !SpawnProtectionState.IsProtected(__instance);
    }
}
