using HarmonyLib;
using UnityEngine;

namespace Eights;

internal static class WeaponPolicy
{
    internal static bool PrepareItemSpawn(ItemSpawner spawner)
    {
        if (GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameModeManager.IsHuntersActive)
        {
            return false;
        }

        if (GameModeManager.IsActive(GameMode.MichaelMeyers))
        {
            GameObject? flashlight = WeaponService.FindPrefab(MichaelMeyersState.FlashlightWeaponName)
                ?? WeaponService.FindPrefab("Flashlight");
            if (flashlight != null)
            {
                spawner.itemToSpawn = flashlight;
            }
            return true;
        }

        if (GameModeManager.UsesTeamWeaponLoadouts)
        {
            if (WeaponSettingsState.Allowed.Count == 0)
            {
                return false;
            }

            GameObject? teamPrefab = WeaponService.FindPrefab(
                WeaponSettingsState.Allowed[Random.Range(0, WeaponSettingsState.Allowed.Count)]);
            if (teamPrefab == null)
            {
                return false;
            }

            spawner.itemToSpawn = teamPrefab;
            return true;
        }

        if (!GameModeManager.IsVanillaScene && IsExclusiveLoadoutMode(GameModeManager.ActiveMode))
        {
            return false;
        }

        if (GameModeManager.ShouldIgnoreGlobalWeaponSettings)
        {
            return true;
        }
        if (!WeaponSettingsState.Enabled || WeaponSettingsState.Allowed.Count == 0)
        {
            return true;
        }
        if (WeaponSettingsState.Cycle && !GameModeManager.IsActive(GameMode.Infected))
        {
            return false;
        }

        GameObject? prefab = WeaponService.FindPrefab(
            WeaponSettingsState.Allowed[Random.Range(0, WeaponSettingsState.Allowed.Count)]);
        if (prefab != null)
        {
            spawner.itemToSpawn = prefab;
        }
        return true;
    }

    private static bool IsExclusiveLoadoutMode(GameMode mode)
    {
        return mode == GameMode.GunGame
            || mode == GameMode.SniperBattle
            || mode == GameMode.MichaelMeyers
            || mode == GameMode.KillTheRat
            || mode == GameMode.OneInTheChamber
            || mode == GameMode.HotPotato
            || mode == GameMode.Infidel
            || mode == GameMode.Assassin;
    }

    internal static bool CanEquip(PlayerPickup pickup, GameObject obj, bool rightHand)
    {
        InitializeGlobalPickupAmmo(obj);
        if (obj == null || GameModeManager.IsVanillaScene)
        {
            return true;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        if (weapon == null)
        {
            return true;
        }

        PlayerHealth? health = pickup.GetComponent<PlayerHealth>();
        switch (GameModeManager.ActiveMode)
        {
            case GameMode.NinjaHunters:
            case GameMode.RabbitHunters:
            case GameMode.TankBattle:
                int huntersPlayerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
                return huntersPlayerId < 0
                    || HuntersState.IsExpectedWeapon(weapon, huntersPlayerId);
            case GameMode.Infected:
                return health == null || !InfectedState.IsInfected(health)
                    || weapon.name.StartsWith(InfectedState.KnifeWeaponName,
                        System.StringComparison.Ordinal);
            case GameMode.Juggernaut:
                return health == null || !JuggernautState.IsCurrentJuggernaut(health)
                    || weapon.name.StartsWith(JuggernautState.WeaponName, System.StringComparison.Ordinal);
            case GameMode.KillTheRat:
                if (health == null)
                {
                    return true;
                }
                return KillTheRatState.IsRat(health)
                    ? KillTheRatState.IsRatWeapon(weapon)
                    : KillTheRatState.IsHumanWeapon(weapon);
            case GameMode.MichaelMeyers:
                return health != null
                    && ((!MichaelMeyersState.CanHoldCouperet(health)
                            && MichaelMeyersState.IsFlashlight(weapon))
                        || (MichaelMeyersState.CanHoldCouperet(health)
                            && MichaelMeyersState.IsCouperet(weapon))
                        || (MichaelMeyersState.CanHoldSurvivorWeapon(health)
                            && weapon.name.StartsWith(MichaelMeyersState.SurvivorWeaponName,
                                System.StringComparison.Ordinal)));
            case GameMode.Infidel:
                int infidelPlayerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
                return infidelPlayerId < 0 || InfidelState.IsAllowedWeapon(weapon, infidelPlayerId);
            case GameMode.Assassin:
                int assassinPlayerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
                return assassinPlayerId < 0 || AssassinState.IsAllowedWeapon(weapon, assassinPlayerId);
            case GameMode.HotPotato:
                int potatoPlayerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
                return potatoPlayerId < 0 || HotPotatoState.IsAllowedWeapon(weapon, potatoPlayerId);
            case GameMode.OneInTheChamber:
                return (OneInTheChamberState.IsPistol(weapon) && rightHand)
                    || (OneInTheChamberState.IsCouperet(weapon) && !rightHand);
            case GameMode.SniperBattle:
                return SniperBattleState.IsSniperWeapon(weapon);
            case GameMode.GunGame:
                return false;
            default:
                return true;
        }
    }

    private static void InitializeGlobalPickupAmmo(GameObject obj)
    {
        if ((!GameModeManager.UsesTeamWeaponLoadouts
                && (GameModeManager.ShouldIgnoreGlobalWeaponSettings || !WeaponSettingsState.Enabled))
            || obj == null)
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

[HarmonyPatch(typeof(ItemSpawner), "Spawn")]
[HarmonyPriority(Priority.First)]
internal static class ItemSpawner_WeaponPolicy_Patch
{
    private static bool Prefix(ItemSpawner __instance)
    {
        return WeaponPolicy.PrepareItemSpawn(__instance);
    }
}

[HarmonyPatch(typeof(PlayerPickup), "SetObjectInHandServer")]
[HarmonyPriority(Priority.First)]
internal static class PlayerPickup_WeaponPolicy_Patch
{
    private static bool Prefix(PlayerPickup __instance, GameObject obj, bool rightHand)
    {
        return WeaponPolicy.CanEquip(__instance, obj, rightHand);
    }
}

[HarmonyPatch(typeof(PlayerPickup), "RightHandDrop")]
internal static class PlayerPickup_RightHandDropPolicy_Patch
{
    private static bool Prefix(PlayerPickup __instance)
    {
        return !WeaponDropPolicy.IsDropBlocked(__instance, true);
    }
}

[HarmonyPatch(typeof(PlayerPickup), "LeftHandDrop")]
internal static class PlayerPickup_LeftHandDropPolicy_Patch
{
    private static bool Prefix(PlayerPickup __instance)
    {
        return !WeaponDropPolicy.IsDropBlocked(__instance, false);
    }
}

[HarmonyPatch(typeof(PlayerPickup), "LeftHandFix")]
internal static class PlayerPickup_LeftHandFixPolicy_Patch
{
    private static bool Prefix(PlayerPickup __instance)
    {
        if (WeaponService.IsOwnerHandObjectPending(__instance, false))
        {
            return false;
        }

        if (WeaponService.IsOwnerHandObjectUnattached(__instance, false))
        {
            WeaponService.AttachUnparentedLeftWeapon(__instance);
            return false;
        }

        if (WeaponService.IsOwnerAttachmentPending(__instance))
        {
            WeaponService.AttachUnparentedLeftWeapon(__instance);
            return false;
        }

        return !WeaponDropPolicy.IsDropBlocked(__instance, false);
    }
}