using HarmonyLib;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PlayerHealth), "Update")]
internal static class PlayerHealth_HealthSettings_Patch
{
    private static void Postfix(PlayerHealth __instance)
    {
        HealthSettingsTuning.ApplyIfChanged(__instance, HealthSettingsState.MaxHealthMultiplier, HealthSettingsState.TuningVersion);
        HealthSettingsTuning.RegenerateIfNeeded(__instance, HealthSettingsTuning.GetMemory(__instance));
    }
}