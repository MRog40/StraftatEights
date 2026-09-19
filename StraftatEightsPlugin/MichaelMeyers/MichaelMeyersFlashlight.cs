using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(FlashLight), "Update")]
internal static class FlashLight_MichaelMeyersColor_Patch
{
    private sealed class OriginalLightColor
    {
        internal OriginalLightColor(Color color)
        {
            Value = color;
        }

        internal Color Value { get; }
    }

    private static readonly FieldInfo? LightField = AccessTools.Field(typeof(FlashLight), "light");
    private static readonly ConditionalWeakTable<FlashLight, OriginalLightColor> AppliedColors = new();

    private static void Postfix(FlashLight __instance)
    {
        if (__instance == null || !__instance)
        {
            return;
        }

        bool michaelMeyersActive = GameModeManager.IsActive(GameMode.MichaelMeyers);
        bool hasAppliedColor = AppliedColors.TryGetValue(__instance,
            out OriginalLightColor? originalColor);
        if (michaelMeyersActive && hasAppliedColor)
        {
            return;
        }

        if (!michaelMeyersActive && !hasAppliedColor)
        {
            return;
        }

        Light? light = FindLight(__instance);
        if (light == null || !light)
        {
            return;
        }

        if (michaelMeyersActive)
        {
            AppliedColors.Add(__instance, new OriginalLightColor(light.color));
            light.color = Color.red;
        }
        else
        {
            light.color = originalColor!.Value;
            AppliedColors.Remove(__instance);
        }
    }

    private static Light? FindLight(FlashLight flashlight)
    {
        GameObject? lightObject = LightField?.GetValue(flashlight) as GameObject;
        if (lightObject != null && lightObject)
        {
            Light? directLight = lightObject.GetComponent<Light>();
            return directLight != null && directLight
                ? directLight
                : lightObject.GetComponentInChildren<Light>(true);
        }

        return flashlight.GetComponentInChildren<Light>(true);
    }
}