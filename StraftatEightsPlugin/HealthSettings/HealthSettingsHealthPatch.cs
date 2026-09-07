using HarmonyLib;
using System.Reflection;

namespace StraftatEightsPlugin;

[HarmonyPatch]
internal static class PlayerHealth_PassiveHealthFeedbackWriter_Patch
{
    private static MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(PlayerHealth),
            "RpcWriter___Observers_HitFeedbackObservers_", method => method.ReturnType == typeof(void));
    }

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix()
    {
        return !HealthSettingsTuning.ApplyingPassiveHealth;
    }
}