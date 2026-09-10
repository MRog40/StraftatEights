using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_UpdatePolicy_Patch
{
    private static void Prefix(Weapon __instance)
    {
        if (!GameModeManager.ShouldIgnoreGlobalWeaponSettingsFor(__instance))
        {
            WeaponAmmoTuning.ApplyToWeapon(__instance, WeaponSettingsState.Enabled,
                WeaponSettingsState.SpareMagazines);
        }

        if (GameModeManager.IsActive(GameMode.GunGame))
        {
            WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
            WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
        }
        else if (GameModeManager.IsActive(GameMode.KillTheRat)
            && KillTheRatState.IsHumanWeapon(__instance))
        {
            WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
            WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
        }

        if (GameModeManager.IsActive(GameMode.SniperBattle)
            && __instance != null
            && SniperBattleState.IsSniperWeapon(__instance)
            && __instance.needsAmmo
            && __instance.gameObject.layer == 8
            && __instance.currentAmmo <= 0)
        {
            __instance.currentAmmo = 1;
            __instance.cantTakeSafeBool = false;
            __instance.noAmmoClicks = 0;
        }
    }

    private static void Postfix(Weapon __instance)
    {
        if (GameModeManager.IsActive(GameMode.GunGame))
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
        else if (GameModeManager.IsActive(GameMode.KillTheRat)
            && KillTheRatState.IsHumanWeapon(__instance))
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
        else if (GameModeManager.IsActive(GameMode.OneInTheChamber)
            && OneInTheChamberState.IsPistol(__instance))
        {
            PlayerHealth? health = __instance.playerController == null
                ? null
                : __instance.playerController.GetComponent<PlayerHealth>();
            int playerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
            if (playerId >= 0)
            {
                OneInTheChamberState.EnforcePistolAmmo(__instance, playerId);
            }
        }

        if (!GameModeManager.ShouldIgnoreGlobalWeaponSettingsFor(__instance))
        {
            WeaponAmmoTuning.TryStartManualReload(__instance, WeaponSettingsState.Enabled,
                WeaponSettingsState.SpareMagazines);
            bool customReloading = __instance != null && WeaponAmmoTuning.IsReloading(__instance);
            if (WeaponSettingsState.Enabled && __instance != null && __instance.IsOwner
                && __instance.needsAmmo && __instance.gameObject.layer == 8
                && (customReloading || !__instance.reloadWeapon))
            {
                int spareRounds = WeaponAmmoTuning.GetSpareRounds(__instance);
                int currentAmmo = customReloading ? 0 : Mathf.Max(0, __instance.currentAmmo);
                PauseManager.Instance.ChangeAmmoText(spareRounds.ToString(), currentAmmo + " / ",
                    __instance.inRightHand);
            }
        }

        if (__instance != null && __instance.inRightHand && __instance.playerController != null)
        {
            MovementPolicy.Apply(__instance.playerController);
        }
    }
}