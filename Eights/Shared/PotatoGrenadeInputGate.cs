using System;
using System.Reflection;
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
    private static readonly ConditionalWeakTable<DualLauncher, object> SeenLaunchers = new();

    internal static void ObserveLauncherBeforeUpdate(DualLauncher launcher)
    {
        if (launcher == null || !launcher || !launcher.IsOwner || !IsPotatoMode())
        {
            return;
        }

        FirstPersonController? player = launcher.playerController;
        if (player == null || !player)
        {
            player = launcher.behaviour?.playerController;
        }

        if (player == null || !player || SeenLaunchers.TryGetValue(launcher, out _))
        {
            return;
        }

        SeenLaunchers.Add(launcher, new object());
        RequireReleaseBeforePinPull(player);
    }

    internal static void RequireReleaseBeforePinPull(FirstPersonController? player)
    {
        if (player == null || !player || !player.IsOwner || !IsPotatoMode())
        {
            return;
        }

        States.GetValue(player, _ => new GateState()).AwaitingRelease = true;
    }

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

[HarmonyPatch]
internal static class DualLauncher_PotatoGrenadeFire_Patch
{
    private static MethodBase? TargetMethod()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public
            | BindingFlags.NonPublic;
        return typeof(DualLauncher).GetMethod("Fire", flags, null, Type.EmptyTypes, null);
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(DualLauncher __instance)
    {
        if (__instance == null || !__instance || !__instance.IsOwner)
        {
            return;
        }

        WeaponAmmoTuning.MarkPotatoGrenadeSpent(__instance.GetComponent<Weapon>());
    }
}

[HarmonyPatch(typeof(DualLauncher), "Update")]
internal static class DualLauncher_PotatoGrenadeAttachGate_Patch
{
    private static void Prefix(DualLauncher __instance)
    {
        PotatoGrenadeInputGate.ObserveLauncherBeforeUpdate(__instance);
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

[HarmonyPatch]
internal static class DualLauncher_PotatoInftatGrenadeThrow_Patch
{
    private static MethodBase? TargetMethod()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public
            | BindingFlags.NonPublic;
        foreach (MethodInfo method in typeof(DualLauncher).GetMethods(flags))
        {
            if (!method.Name.StartsWith("RpcLogic___ServerFire_", StringComparison.Ordinal))
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 5
                && parameters[0].ParameterType == typeof(Vector3)
                && parameters[1].ParameterType == typeof(Vector3)
                && parameters[2].ParameterType == typeof(uint)
                && parameters[3].ParameterType == typeof(trickShotData)
                && parameters[4].ParameterType == typeof(int))
            {
                return method;
            }
        }

        return null;
    }

    private static void Postfix(DualLauncher __instance)
    {
        PotatotatState.OnServerGrenadeThrown(__instance);
        PotatoInftatState.OnServerGrenadeThrown(__instance);
    }
}