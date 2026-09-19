using HarmonyLib;

namespace Eights;

[HarmonyPatch(typeof(PlayerHealth), "Update")]
internal static class PlayerHealth_HealthSettings_Patch
{
    private static void Postfix(PlayerHealth __instance)
    {
        if (RespawnProtection.IsProtected(__instance))
        {
            return;
        }

        HealthSettingsTuning.ApplyIfChanged(__instance, HealthSettingsState.MaxHealthMultiplier, HealthSettingsState.TuningVersion);
        HealthSettingsTuning.RegenerateIfNeeded(__instance, HealthSettingsTuning.GetMemory(__instance));
    }
}