using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch]
internal static class PlayerSetup_TeamOutlineRefresh_Patch
{
    private static MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(PlayerSetup), "RpcLogic___ChangeDress_",
            method => method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 3 } parameters
                && parameters[0].ParameterType == typeof(GameObject)
                && parameters[1].ParameterType == typeof(GameObject)
                && parameters[2].ParameterType == typeof(Vector3));
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix()
    {
        TeamOutline.RequestRefresh();
    }
}