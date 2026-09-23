using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(PlayerPickup), "RightHandFix")]
internal static class PlayerPickup_WeaponHandPolicy_Patch
{
    private static bool Prefix(PlayerPickup __instance)
    {
        if (WeaponService.IsOwnerHandObjectPending(__instance, true))
        {
            return false;
        }

        if (WeaponService.IsOwnerHandObjectUnattached(__instance, true))
        {
            WeaponService.AttachUnparentedWeapon(__instance);
            return false;
        }

        if (WeaponService.IsOwnerAttachmentPending(__instance))
        {
            WeaponService.AttachUnparentedWeapon(__instance);
            return false;
        }

        if (WeaponDropPolicy.IsDropBlocked(__instance, true))
        {
            WeaponService.AttachUnparentedWeapon(__instance);
            return false;
        }

        return true;
    }
}