using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_UpdatePolicy_Patch
{
    private static void Prefix(Weapon __instance, out bool __state)
    {
        RestoreFreshPotatoGrenadeAmmo(__instance);
        bool droppedAmmoProtectionEnabled = false;
        if (GameModeManager.UsesTeamWeaponLoadouts
            || !GameModeManager.ShouldIgnoreGlobalWeaponSettingsFor(__instance))
        {
            bool ammoTuningEnabled = GameModeManager.UsesTeamWeaponLoadouts
                || WeaponSettingsState.Enabled;
            droppedAmmoProtectionEnabled = ammoTuningEnabled;
            WeaponAmmoTuning.ApplyToWeapon(__instance, ammoTuningEnabled,
                WeaponSettingsState.SpareMagazines);
        }

        if (GuntatState.IsCurrentProgressWeapon(__instance))
        {
            droppedAmmoProtectionEnabled = true;
            WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
            WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
        }
        else if (GameModeManager.IsActive(GameMode.Ratatat)
            && RatatatState.IsHumanWeapon(__instance))
        {
            droppedAmmoProtectionEnabled = true;
            WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
            WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
        }
        else if (GameModeManager.IsActive(GameMode.Infideltat) && InfideltatState.WeaponsUnlocked)
        {
            droppedAmmoProtectionEnabled = true;
            WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
            WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
        }
        else if (GameModeManager.IsActive(GameMode.Assassintat)
            && AssassintatState.IsUnlimitedWeapon(__instance))
        {
            droppedAmmoProtectionEnabled = true;
            WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
            WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
        }
        else if (GameModeManager.IsActive(GameMode.Potatotat)
            && PotatotatState.IsPotatotatWeapon(__instance))
        {
            droppedAmmoProtectionEnabled = true;
            WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
            WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
        }
        else if (GameModeManager.IsHuntersActive
            && HuntModesState.IsExpectedWeapon(__instance))
        {
            droppedAmmoProtectionEnabled = true;
            WeaponAmmoTuning.ApplyUnlimitedToWeapon(__instance);
            WeaponAmmoTuning.TryStartManualReload(__instance, true, 0);
        }

        if (GameModeManager.IsActive(GameMode.Snipertat)
            && __instance != null
            && SnipertatState.IsSniperWeapon(__instance)
            && __instance.needsAmmo
            && __instance.gameObject.layer == 8
            && __instance.currentAmmo <= 0)
        {
            __instance.currentAmmo = 1;
            __instance.cantTakeSafeBool = false;
            __instance.noAmmoClicks = 0;
        }

        if (__instance != null && __instance.gameObject.layer == 7
            && __instance.currentAmmo <= 0)
        {
            WeaponAmmoTuning.RestoreDroppedWeaponAmmo(__instance, droppedAmmoProtectionEnabled,
                WeaponSettingsState.SpareMagazines);
        }
        __state = WeaponAmmoTuning.SuppressEmptyWeaponAutoDrop(__instance);
    }

    private static void RestoreFreshPotatoGrenadeAmmo(Weapon weapon)
    {
        WeaponAmmoTuning.TryRestoreFreshPotatoGrenadeAmmo(weapon);
    }

    private static void Postfix(Weapon __instance, bool __state)
    {
        if (GuntatState.IsCurrentProgressWeapon(__instance))
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
        else if (GameModeManager.IsActive(GameMode.Ratatat)
            && RatatatState.IsHumanWeapon(__instance))
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
        else if (GameModeManager.IsActive(GameMode.Infideltat) && InfideltatState.WeaponsUnlocked)
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
        else if (GameModeManager.IsActive(GameMode.Assassintat)
            && AssassintatState.IsUnlimitedWeapon(__instance))
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
        else if (GameModeManager.IsActive(GameMode.Potatotat)
            && PotatotatState.IsPotatotatWeapon(__instance))
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
        else if (GameModeManager.IsHuntersActive
            && HuntModesState.IsExpectedWeapon(__instance))
        {
            WeaponAmmoTuning.UpdateUnlimitedAmmoHud(__instance);
        }
        else if (GameModeManager.IsActive(GameMode.Chambertat)
            && ChambertatState.IsPistol(__instance))
        {
            PlayerHealth? health = __instance.playerController == null
                ? null
                : __instance.playerController.GetComponent<PlayerHealth>();
            int playerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
            if (playerId >= 0)
            {
                ChambertatState.EnforcePistolAmmo(__instance, playerId);
            }
        }

        if (GameModeManager.UsesTeamWeaponLoadouts
            || !GameModeManager.ShouldIgnoreGlobalWeaponSettingsFor(__instance))
        {
            bool ammoTuningEnabled = GameModeManager.UsesTeamWeaponLoadouts
                || WeaponSettingsState.Enabled;
            WeaponAmmoTuning.TryStartManualReload(__instance, ammoTuningEnabled,
                WeaponSettingsState.SpareMagazines);
            bool customReloading = __instance != null && WeaponAmmoTuning.IsReloading(__instance);
            if (ammoTuningEnabled && __instance != null && __instance.IsOwner
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

        WeaponAmmoTuning.RestoreEmptyWeaponAutoDrop(__instance, __state);
        WeaponAmmoTuning.RestoreVisibilityAfterDrop(__instance);
    }
}