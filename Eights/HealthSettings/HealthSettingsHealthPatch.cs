using HarmonyLib;
using System.Reflection;

namespace Eights;

[HarmonyPatch]
internal static class PlayerHealth_PassiveHealthFeedback_Patch
{
    private static MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(PlayerHealth),
            "RpcLogic___HitFeedbackObservers_", method => method.ReturnType == typeof(void));
    }

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix()
    {
        return !HealthSettingsTuning.ApplyingPassiveHealth;
    }
}