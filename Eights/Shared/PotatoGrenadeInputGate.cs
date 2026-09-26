using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Eights;

internal static class PotatoGrenadeInputGate
{
    private sealed class GateState
    {
        internal bool AwaitingRelease;
    }

    private static readonly ConditionalWeakTable<FirstPersonController, GateState> States = new();

    internal static bool AllowPinPull(DualLauncher launcher, bool value)
    {
        FirstPersonController? player = launcher.playerController;
        if (!value || !IsPotatoMode() || !launcher.IsOwner || player == null || !player)
        {
            return true;
        }

        GateState state = States.GetValue(player, _ => new GateState());
        if (state.AwaitingRelease)
        {
            return false;
        }

        state.AwaitingRelease = true;
        return true;
    }

    internal static void ObserveRelease(FirstPersonController player)
    {
        if (!player.IsOwner || !States.TryGetValue(player, out GateState state))
        {
            return;
        }

        if (!IsPressed(player.fire1) && !IsPressed(player.fire2))
        {
            state.AwaitingRelease = false;
        }
    }

    private static bool IsPotatoMode()
    {
        return GameModeManager.ActiveMode == GameMode.Potatotat
            || GameModeManager.ActiveMode == GameMode.PotatoInftat;
    }

    private static bool IsPressed(InputAction action)
    {
        return action != null && action.ReadValue<float>() > 0.1f;
    }
}

[HarmonyPatch(typeof(DualLauncher), "SetBool")]
internal static class DualLauncher_PotatoGrenadePinGate_Patch
{
    private static bool Prefix(DualLauncher __instance, bool value)
    {
        return PotatoGrenadeInputGate.AllowPinPull(__instance, value);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "Update")]
internal static class FirstPersonController_PotatoGrenadeReleaseGate_Patch
{
    private static void Prefix(FirstPersonController __instance)
    {
        PotatoGrenadeInputGate.ObserveRelease(__instance);
    }
}