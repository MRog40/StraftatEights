using HarmonyLib;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PlayerHealth), "RpcLogic___HitFeedbackObservers_2166136261")]
internal static class PlayerHealth_PassiveHealthFeedback_Patch
{
    private static bool Prefix()
    {
        return !HealthSettingsTuning.ApplyingPassiveHealth;
    }
}