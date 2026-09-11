using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_GlobalWeaponsSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (GameModeManager.ShouldIgnoreGlobalWeaponSettings || !WeaponSettingsState.Enabled || !WeaponSettingsState.Cycle || __instance.player == null)
        {
            return;
        }
        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null && !JuggernautState.IsCurrentJuggernaut(__instance.player))
        {
            string? selectedWeapon = WeaponSettingsState.GetSelectedWeapon(client.PlayerId);
            if (selectedWeapon != null)
            {
                WeaponSettingsState.RequestLoadout(client.PlayerId, selectedWeapon);
            }
        }
    }
}

[HarmonyPatch(typeof(PlayerPickup), "RightHandFix")]
internal static class PlayerPickup_WeaponHandPolicy_Patch
{
    private static bool Prefix(PlayerPickup __instance)
    {
        if (WeaponService.IsOwnerHandObjectPending(__instance, true))
        {
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