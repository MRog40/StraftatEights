using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_ReserveAmmo_Patch
{
    private static void Prefix(Weapon __instance)
    {
        bool enabled = WeaponSettingsState.Enabled;
        WeaponAmmoTuning.ApplyToWeapon(__instance, enabled, WeaponSettingsState.SpareMagazines);
    }

    private static void Postfix(Weapon __instance)
    {
        bool customReloading = __instance != null && WeaponAmmoTuning.IsReloading(__instance);
        if (!WeaponSettingsState.Enabled || __instance == null || !__instance.IsOwner || !__instance.needsAmmo || __instance.gameObject.layer != 8 || (!customReloading && __instance.reloadWeapon))
        {
            return;
        }

        int spareRounds = WeaponAmmoTuning.GetSpareRounds(__instance);
        int currentAmmo = customReloading ? 0 : (__instance.currentAmmo > 0 ? __instance.currentAmmo : 0);
        PauseManager.Instance.ChangeAmmoText(spareRounds.ToString(), currentAmmo + " / ", __instance.inRightHand);
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
internal static class PlayerPickup_WeaponAmmoPickup_Patch
{
    private static void Prefix(GameObject obj)
    {
        if (!WeaponSettingsState.Enabled || obj == null)
        {
            return;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        if (weapon == null || obj.transform.GetComponentInParent<ItemSpawner>() == null)
        {
            return;
        }

        WeaponAmmoTuning.InitializeFromSpawnerPickup(weapon, WeaponSettingsState.SpareMagazines);
    }
}