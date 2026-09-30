using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch]
internal static class PlayerPickup_SetObjectInHandServerState_Patch
{
    private const int CanEquipState = 1;
    private const int ServerBeforeOwnershipTransferState = 2;

    private static MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(PlayerPickup),
            "RpcLogic___SetObjectInHandServer_",
            method => method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 5 } parameters
                && parameters[0].ParameterType == typeof(GameObject)
                && parameters[1].ParameterType == typeof(Vector3)
                && parameters[2].ParameterType == typeof(Quaternion)
                && parameters[3].ParameterType == typeof(GameObject)
                && parameters[4].ParameterType == typeof(bool));
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Prefix(PlayerPickup __instance, GameObject obj, bool rightHand,
        out int __state)
    {
        bool canEquip = WeaponPolicy.CanEquip(__instance, obj, rightHand);
        __state = (canEquip ? CanEquipState : 0)
            | (__instance.IsServer && !__instance.IsOwner
                ? ServerBeforeOwnershipTransferState
                : 0);
    }

    private static void Postfix(PlayerPickup __instance, GameObject obj, bool rightHand,
        int __state)
    {
        bool canEquip = (__state & CanEquipState) != 0;
        if (!canEquip || (__state & ServerBeforeOwnershipTransferState) == 0
            || !__instance.IsServer || obj == null || !obj)
        {
            return;
        }

        if (rightHand)
        {
            __instance.sync___set_value_objInHand(obj, true);
            __instance.sync___set_value_hasObjectInHand(true, true);
        }
        else
        {
            __instance.sync___set_value_objInLeftHand(obj, true);
            __instance.sync___set_value_hasObjectInLeftHand(true, true);
        }
    }
}

[HarmonyPatch]
internal static class PlayerPickup_DropObjectServerState_Patch
{
    private static MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(PlayerPickup),
            "RpcLogic___DropObjectServer_",
            method => method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 2 } parameters
                && parameters[0].ParameterType == typeof(GameObject)
                && parameters[1].ParameterType == typeof(bool));
    }

    private static bool Prepare() => TargetMethod() != null;

    private static bool Prefix(PlayerPickup __instance, GameObject obj, bool rightHand,
        out bool __state)
    {
        GameObject? currentObject = rightHand ? __instance.objInHand : __instance.objInLeftHand;
        __state = !WeaponDropPolicy.IsDropBlocked(__instance, rightHand)
            && currentObject != null && currentObject
            && ReferenceEquals(currentObject, obj);
        return __state;
    }

    private static void Postfix(PlayerPickup __instance, GameObject obj, bool rightHand,
        bool __state)
    {
        if (!__state || !__instance.IsServer || __instance.IsOwner || obj == null || !obj)
        {
            return;
        }

        GameObject? currentObject = rightHand ? __instance.objInHand : __instance.objInLeftHand;
        if (currentObject != null && currentObject && !ReferenceEquals(currentObject, obj))
        {
            return;
        }

        if (rightHand)
        {
            __instance.sync___set_value_hasObjectInHand(false, true);
            __instance.sync___set_value_objInHand(null, true);
        }
        else
        {
            __instance.sync___set_value_hasObjectInLeftHand(false, true);
            __instance.sync___set_value_objInLeftHand(null, true);
        }
    }
}