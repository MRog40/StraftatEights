using HarmonyLib;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PlayerHealth), "RpcWriter___Observers_HitFeedbackObservers_2166136261")]
internal static class PlayerHealth_PassiveHealthFeedbackWriter_Patch
{
    private static bool Prefix()
    {
        return !HealthSettingsTuning.ApplyingPassiveHealth;
    }
}