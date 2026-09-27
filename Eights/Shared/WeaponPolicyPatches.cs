using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Eights;

internal static class WeaponPolicy
{
    internal static bool PrepareItemSpawn(ItemSpawner spawner)
    {
        if (GameModeManager.IsBombModeActive
            || GameModeManager.IsHuntersActive)
        {
            return false;
        }

        if (GameModeManager.IsActive(GameMode.Michaeltat))
        {
            GameObject? flashlight = WeaponService.FindPrefab(MichaeltatState.FlashlightWeaponName)
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
        return mode == GameMode.Guntat
            || mode == GameMode.Snipertat
            || mode == GameMode.Michaeltat
            || mode == GameMode.Ratatat
            || mode == GameMode.Chambertat
            || mode == GameMode.Potatotat
            || mode == GameMode.Infideltat
            || mode == GameMode.Assassintat
            || mode == GameMode.Nifetat;
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
            case GameMode.Ninjatat:
            case GameMode.Hunttat:
            case GameMode.Tanktat:
                int huntersPlayerId = pickup.playerValues?.playerClient?.PlayerId
                    ?? health?.playerValues?.playerClient?.PlayerId ?? -1;
                return huntersPlayerId >= 0
                    && HuntModesState.IsExpectedWeapon(weapon, huntersPlayerId);
            case GameMode.Infectedtat:
                int infectedPlayerId = PlayerLookup.FindPlayerId(pickup);
                return InfectedtatRules.CanEquipWeapon(infectedPlayerId >= 0,
                    InfectedtatState.IsInfectedtat(infectedPlayerId), weapon.name);
            case GameMode.PotatoInftat:
                return health == null || !PotatoInftatState.IsInfectedtat(health)
                    || weapon.name.StartsWith(PotatoInftatState.GrenadeWeaponName,
                        System.StringComparison.Ordinal);
            case GameMode.Juggertat:
                return health == null || !JuggertatState.IsCurrentJuggertat(health)
                    || weapon.name.StartsWith(JuggertatState.WeaponName, System.StringComparison.Ordinal);
            case GameMode.Ratatat:
                if (health == null)
                {
                    return true;
                }
                return RatatatState.IsRat(health)
                    ? RatatatState.IsRatWeapon(weapon)
                    : RatatatState.IsHumanWeapon(weapon);
            case GameMode.Michaeltat:
                return health != null
                    && ((!MichaeltatState.CanHoldCouperet(health)
                            && MichaeltatState.IsFlashlight(weapon))
                        || (MichaeltatState.CanHoldCouperet(health)
                            && MichaeltatState.IsCouperet(weapon))
                        || (MichaeltatState.CanHoldSurvivorWeapon(health)
                            && weapon.name.StartsWith(MichaeltatState.SurvivorWeaponName,
                                System.StringComparison.Ordinal)));
            case GameMode.Infideltat:
                int infidelPlayerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
                return infidelPlayerId < 0 || InfideltatState.IsAllowedWeapon(weapon, infidelPlayerId);
            case GameMode.Assassintat:
                int assassinPlayerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
                return assassinPlayerId < 0 || AssassintatState.IsAllowedWeapon(weapon, assassinPlayerId);
            case GameMode.Potatotat:
                int potatoPlayerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
                return potatoPlayerId < 0 || PotatotatState.IsAllowedWeapon(weapon, potatoPlayerId);
            case GameMode.Chambertat:
                return ChambertatState.IsPistol(weapon)
                    || ChambertatState.IsCouperet(weapon);
            case GameMode.Snipertat:
                return SnipertatState.IsSniperWeapon(weapon);
            case GameMode.Guntat:
                return false;
            case GameMode.Nifetat:
                return NifetatState.IsSelectedWeapon(weapon);
            default:
                return true;
        }
    }

    internal static void CancelDefaultKnifeGrantForGunPickup(PlayerPickup pickup, GameObject obj)
    {
        if (pickup == null || !pickup || !pickup.IsServer
            || !GameModeManager.CanUseDefaultKnifeFallback || !WeaponSettingsState.DefaultKnife
            || obj == null || !obj)
        {
            return;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        if (weapon == null || DefaultKnifeRules.IsKnife(weapon.name))
        {
            return;
        }

        PlayerHealth? health = pickup.GetComponent<PlayerHealth>();
        int playerId = pickup.playerValues?.playerClient?.PlayerId
            ?? health?.playerValues?.playerClient?.PlayerId ?? -1;
        WeaponService.CancelPendingDefaultKnifeGrant(playerId);
        RemoveDefaultKnifeIfHeld(pickup, pickup.objInHand, true);
        RemoveDefaultKnifeIfHeld(pickup, pickup.objInLeftHand, false);
    }

    private static void RemoveDefaultKnifeIfHeld(PlayerPickup pickup, GameObject? heldObject,
        bool rightHand)
    {
        if (heldObject == null || !heldObject)
        {
            return;
        }

        Weapon? heldWeapon = heldObject.GetComponent<Weapon>();
        if (heldWeapon != null && DefaultKnifeRules.IsDefaultKnife(heldWeapon.name))
        {
            WeaponService.RemoveHeldWeapon(pickup, rightHand, heldWeapon);
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

[HarmonyPatch(typeof(PlayerPickup), "Update")]
internal static class PlayerPickup_DefaultKnifeFallback_Patch
{
    private static void Postfix(PlayerPickup __instance)
    {
        WeaponSettingsState.TrackLocalDefaultKnifeFallback(__instance);
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
        bool canEquip = WeaponPolicy.CanEquip(__instance, obj, rightHand);
        if (canEquip)
        {
            WeaponPolicy.CancelDefaultKnifeGrantForGunPickup(__instance, obj);
        }
        return canEquip;
    }
}

[HarmonyPatch]
[HarmonyPriority(Priority.First)]
internal static class PlayerPickup_WeaponServerLogicPolicy_Patch
{
    private static System.Reflection.MethodBase? TargetMethod()
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

    private static bool Prefix(PlayerPickup __instance, GameObject obj, bool rightHand)
    {
        bool canEquip = WeaponPolicy.CanEquip(__instance, obj, rightHand);
        if (canEquip)
        {
            WeaponPolicy.CancelDefaultKnifeGrantForGunPickup(__instance, obj);
        }
        return canEquip;
    }
}

[HarmonyPatch(typeof(PlayerPickup), "RightHandPickup")]
internal static class PlayerPickup_InfectedtatRightHandPickup_Patch
{
    private static bool Prefix(PlayerPickup __instance)
    {
        return !IsInfectedOrUnresolved(__instance);
    }

    private static bool IsInfectedOrUnresolved(PlayerPickup pickup)
    {
        if (!GameModeManager.IsActive(GameMode.Infectedtat))
        {
            return false;
        }

        int playerId = PlayerLookup.FindPlayerId(pickup);
        return playerId < 0 || InfectedtatState.IsInfectedtat(playerId);
    }
}

[HarmonyPatch(typeof(PlayerPickup), "LeftHandPickup")]
internal static class PlayerPickup_InfectedtatLeftHandPickup_Patch
{
    private static bool Prefix(PlayerPickup __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Infectedtat))
        {
            return true;
        }

        int playerId = PlayerLookup.FindPlayerId(__instance);
        return playerId >= 0 && !InfectedtatState.IsInfectedtat(playerId);
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

[HarmonyPatch(typeof(PlayerPickup), "SwitchWeapons")]
internal static class PlayerPickup_GuntatSwitchWeapons_Patch
{
    private static bool Prefix()
    {
        return !GameModeManager.IsActive(GameMode.Guntat);
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

[HarmonyPatch]
internal static class PlayerPickup_CouperetDropServerLogic_Patch
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

    private static void Postfix(PlayerPickup __instance, GameObject obj, bool rightHand)
    {
        if (obj == null || !obj)
        {
            return;
        }

        Weapon? weapon = obj.GetComponent<Weapon>();
        if (weapon == null || !DefaultKnifeRules.IsDefaultKnife(weapon.name))
        {
            return;
        }

        WeaponService.QueueCouperetDropDespawn(__instance, obj, rightHand);
    }
}

[HarmonyPatch]
internal static class PlayerPickup_CouperetReattachLogic_Patch
{
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

    private static void Prefix(GameObject obj)
    {
        WeaponService.CancelPendingCouperetDrop(obj);
    }
}

[HarmonyPatch(typeof(PlayerPickup), nameof(PlayerPickup.HandsReconstruct))]
internal static class PlayerPickup_LeftHandOnlyReconstruct_Patch
{
    private static readonly FieldInfo? RightIdlePositionField =
        AccessTools.Field(typeof(PlayerPickup), "RightIdlePosition");

    private static bool Prefix(PlayerPickup __instance)
    {
        GameObject? rightHandObject = __instance.objInHand;
        GameObject? leftHandObject = __instance.objInLeftHand;
        if (rightHandObject != null || leftHandObject == null || !leftHandObject)
        {
            return true;
        }

        ItemBehaviour? leftHandItem = leftHandObject.GetComponent<ItemBehaviour>();
        Transform? rightIdlePosition = RightIdlePositionField?.GetValue(__instance) as Transform;
        if (leftHandItem == null || rightIdlePosition == null)
        {
            return false;
        }

        __instance.SetRightIKTarget(rightIdlePosition);
        __instance.SetLeftIKTarget(leftHandItem.gripLeft);
        __instance.UpdateIKPoistion();
        return false;
    }
}