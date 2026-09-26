using HarmonyLib;

namespace Eights;

[HarmonyPatch(typeof(FirstPersonController), "PlaySoundServer", new[] { typeof(int) })]
internal static class FirstPersonController_FootstepAudio_Patch
{
    private static bool Prefix(FirstPersonController __instance, int __0)
    {
        return FootstepAudioRules.ShouldPlay(__0, GlobalModifiersState.SilentWalking,
            __instance.isSprinting);
    }
}