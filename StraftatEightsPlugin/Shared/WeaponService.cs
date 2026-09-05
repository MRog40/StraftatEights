using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class WeaponService
{
    private static readonly Dictionary<string, GameObject> Prefabs = new(StringComparer.Ordinal);
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static MethodInfo? SetObjectInHandLogic;
    private static MethodInfo? SetObjectInHandObserver;
    private static MethodInfo? SetObjectInHandObserverLogic;

    internal static void CachePrefabs()
    {
        if (Prefabs.Count != 0) return;
        foreach (GameObject prefab in Resources.LoadAll<GameObject>("RandomWeapons"))
        {
            if (prefab != null && !Prefabs.ContainsKey(prefab.name)) Prefabs[prefab.name] = prefab;
        }
    }

    internal static GameObject? FindPrefab(string weaponName)
    {
        CachePrefabs();
        return Prefabs.TryGetValue(weaponName.Trim(), out GameObject prefab) ? prefab : null;
    }

    internal static List<string> ParseWeaponList(string value)
    {
        CachePrefabs();
        return value.Split(',', ';').Select(item => item.Trim())
            .Where(item => item.Length > 0 && Prefabs.ContainsKey(item))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    internal static void GiveWeapon(int playerId, string weaponName, int? spareMagazines = null)
    {
        if (Plugin.Instance != null) Plugin.Instance.StartCoroutine(GiveWeaponCoroutine(playerId, weaponName, spareMagazines));
    }

    private static IEnumerator GiveWeaponCoroutine(int playerId, string weaponName, int? spareMagazines)
    {
        NetworkManager? networkManager = FishNet.InstanceFinder.NetworkManager;
        GameObject? prefab = FindPrefab(weaponName);
        if (networkManager == null || !networkManager.IsServer || prefab == null) yield break;

        PlayerPickup? pickup = null;
        PlayerManager? manager = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client))
            {
                manager = client.PlayerSpawner;
                pickup = manager?.player?.GetComponent<PlayerPickup>();
            }
            if (pickup != null && manager?.player != null) break;
            yield return new WaitForSeconds(0.25f);
        }
        if (pickup == null || manager?.player == null) yield break;

        DespawnHeldWeapon(networkManager, pickup.objInHand);
        DespawnHeldWeapon(networkManager, pickup.objInLeftHand);
        pickup.sync___set_value_hasObjectInHand(false, true);
        pickup.sync___set_value_hasObjectInLeftHand(false, true);
        pickup.sync___set_value_objInHand(null, true);
        pickup.sync___set_value_objInLeftHand(null, true);
        yield return new WaitForSeconds(0.15f);

        GameObject weapon = UnityEngine.Object.Instantiate(prefab, manager.player.transform.position, manager.player.transform.rotation);
        ItemBehaviour? item = weapon.GetComponent<ItemBehaviour>();
        if (item != null) item.dispenserStart = true;
        Rigidbody? body = weapon.GetComponent<Rigidbody>();
        if (body != null) { body.isKinematic = true; body.useGravity = false; }
        networkManager.ServerManager.Spawn(weapon);
        yield return new WaitForSeconds(0.1f);

        SetObjectInHandLogic ??= typeof(PlayerPickup).GetMethod("RpcLogic___SetObjectInHandServer_46969756", Flags);
        SetObjectInHandObserver ??= typeof(PlayerPickup).GetMethod("SetObjectInHandObserver", Flags);
        SetObjectInHandObserverLogic ??= typeof(PlayerPickup).GetMethod("RpcLogic___SetObjectInHandObserver_46969756", Flags);
        Weapon? weaponComponent = weapon.GetComponent<Weapon>();
        if (item == null || weaponComponent == null)
        {
            yield break;
        }

        if (spareMagazines.HasValue)
        {
            WeaponAmmoTuning.InitializeFromSpawnerPickup(weaponComponent, spareMagazines.Value);
        }

        Transform hand = weaponComponent.requireBothHands
            ? pickup.pickupPositionBothHand[item.camChildIndex]
            : pickup.pickupPositionRightHand[item.camChildIndex];
        weapon.transform.SetPositionAndRotation(hand.position, hand.rotation);
        object[] args = { weapon, hand.position, hand.rotation, manager.player.gameObject, true };
        SetObjectInHandLogic?.Invoke(pickup, args);
        pickup.sync___set_value_hasObjectInHand(true, true);
        pickup.sync___set_value_objInHand(weapon, true);
        SetObjectInHandObserver?.Invoke(pickup, args);
        SetObjectInHandObserverLogic?.Invoke(pickup, args);
        pickup.HandsReconstruct();
        pickup.UpdateIKPoistion();
        item.InstantComeBackOnFire();
        if (item != null) item.dispenserStart = false;
    }

    internal static void AttachUnparentedWeapon(PlayerPickup pickup)
    {
        if (!pickup.IsOwner || !pickup.hasObjectInHand || pickup.objInHand == null)
        {
            return;
        }

        GameObject weapon = pickup.objInHand;
        ItemBehaviour? item = weapon.GetComponent<ItemBehaviour>();
        Weapon? weaponComponent = weapon.GetComponent<Weapon>();
        if (item == null || weaponComponent == null)
        {
            return;
        }

        Transform? expectedParent = weaponComponent.requireBothHands
            ? pickup.pickupPositionBothHand[item.camChildIndex]
            : pickup.pickupPositionRightHand[item.camChildIndex];
        if (expectedParent == null)
        {
            return;
        }

        if (weapon.transform.parent == expectedParent && weaponComponent.inRightHand &&
            item.playerPickup == pickup && item.rootObject == pickup.gameObject)
        {
            return;
        }

        SetObjectInHandObserverLogic ??= typeof(PlayerPickup).GetMethod("RpcLogic___SetObjectInHandObserver_46969756", Flags);
        object[] args = { weapon, expectedParent.position, expectedParent.rotation, pickup.gameObject, true };
        SetObjectInHandObserverLogic?.Invoke(pickup, args);
        pickup.HandsReconstruct();
        pickup.SetRightIKTarget(item.gripRight);
        if (weaponComponent.requireBothHands)
        {
            pickup.SetLeftIKTarget(item.gripLeft);
        }
        pickup.UpdateIKPoistion();
        item.InstantComeBackOnFire();
        item.dispenserStart = false;
    }

    private static void DespawnHeldWeapon(NetworkManager networkManager, GameObject? heldWeapon)
    {
        if (heldWeapon == null) return;
        NetworkObject? networkObject = heldWeapon.GetComponentInParent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            networkManager.ServerManager.Despawn(networkObject);
        }
        else
        {
            UnityEngine.Object.Destroy(heldWeapon);
        }
    }

}