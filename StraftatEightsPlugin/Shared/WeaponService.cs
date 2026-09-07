using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StraftatEightsPlugin;

internal static class WeaponService
{
    private static readonly Dictionary<string, GameObject> Prefabs = new(StringComparer.Ordinal);
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static MethodInfo? SetObjectInHandLogic;
    private static MethodInfo? SetObjectInHandObserver;
    private static MethodInfo? SetObjectInHandObserverLogic;
    private static bool _attachmentMethodsResolved;
    private static bool _attachmentMethodsAvailable;
    private static readonly Dictionary<int, int> RequestVersions = new();

    internal static bool IsFinalGameScreen
    {
        get
        {
            if (PauseManager.Instance != null && PauseManager.Instance.inVictoryMenu)
            {
                return true;
            }

            string sceneName = SceneManager.GetActiveScene().name;
            return sceneName == "VictoryScene" || sceneName == "EndGame";
        }
    }

    internal static void Initialize()
    {
        ResolveAttachmentMethods();
    }

    internal static void ResetPendingRequests()
    {
        RequestVersions.Clear();
    }

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
        return (value ?? string.Empty).Split(',', ';').Select(item => item.Trim())
            .Where(item => item.Length > 0 && Prefabs.ContainsKey(item))
            .Distinct(StringComparer.Ordinal).ToList();
    }

    internal static void GiveWeapon(int playerId, string weaponName, int? spareMagazines = null)
    {
        if (Plugin.Instance != null && !IsFinalGameScreen)
        {
            int requestVersion = RequestVersions.TryGetValue(playerId, out int previousVersion)
                ? previousVersion + 1
                : 1;
            RequestVersions[playerId] = requestVersion;
            Plugin.Instance.StartCoroutine(GiveWeaponCoroutine(playerId, weaponName, spareMagazines,
                SessionState.Generation, GameModeManager.RoundId, requestVersion));
        }
    }

    private static IEnumerator GiveWeaponCoroutine(int playerId, string weaponName, int? spareMagazines,
        int sessionGeneration, int roundId, int requestVersion)
    {
        NetworkManager? networkManager = FishNet.InstanceFinder.NetworkManager;
        GameObject? prefab = FindPrefab(weaponName);
        if (!IsCurrentRequest(playerId, requestVersion) || !SessionState.IsCurrent(sessionGeneration)
            || GameModeManager.RoundId != roundId
            || networkManager == null || !networkManager.IsServer || prefab == null
            || !ResolveAttachmentMethods()) yield break;

        PlayerPickup? pickup = null;
        PlayerManager? manager = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (!IsCurrentRequest(playerId, requestVersion) || !SessionState.IsCurrent(sessionGeneration)
                || GameModeManager.RoundId != roundId
                || IsFinalGameScreen) yield break;
            if (ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client))
            {
                manager = client.PlayerSpawner;
                pickup = manager?.player?.GetComponent<PlayerPickup>();
            }
            if (pickup != null && manager?.player != null) break;
            yield return new WaitForSeconds(0.25f);
        }
        if (pickup == null || manager?.player == null || !IsCurrentRequest(playerId, requestVersion)
            || !SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || IsFinalGameScreen) yield break;

        DespawnHeldWeapon(networkManager, pickup.objInHand);
        DespawnHeldWeapon(networkManager, pickup.objInLeftHand);
        pickup.sync___set_value_hasObjectInHand(false, true);
        pickup.sync___set_value_hasObjectInLeftHand(false, true);
        pickup.sync___set_value_objInHand(null, true);
        pickup.sync___set_value_objInLeftHand(null, true);
        yield return new WaitForSeconds(0.15f);
        if (!IsCurrentRequest(playerId, requestVersion) || !SessionState.IsCurrent(sessionGeneration)
            || GameModeManager.RoundId != roundId
            || IsFinalGameScreen) yield break;

        GameObject weapon = UnityEngine.Object.Instantiate(prefab, manager.player.transform.position, manager.player.transform.rotation);
        ItemBehaviour? item = weapon.GetComponent<ItemBehaviour>();
        if (item != null) item.dispenserStart = true;
        Rigidbody? body = weapon.GetComponent<Rigidbody>();
        if (body != null) { body.isKinematic = true; body.useGravity = false; }
        networkManager.ServerManager.Spawn(weapon);
        yield return new WaitForSeconds(0.1f);
        if (!IsCurrentRequest(playerId, requestVersion) || !SessionState.IsCurrent(sessionGeneration)
            || GameModeManager.RoundId != roundId
            || IsFinalGameScreen)
        {
            DespawnHeldWeapon(networkManager, weapon);
            yield break;
        }

        Weapon? weaponComponent = weapon.GetComponent<Weapon>();
        if (item == null || weaponComponent == null)
        {
            DespawnHeldWeapon(networkManager, weapon);
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
        SetObjectInHandLogic!.Invoke(pickup, args);
        pickup.sync___set_value_hasObjectInHand(true, true);
        pickup.sync___set_value_objInHand(weapon, true);
        SetObjectInHandObserver!.Invoke(pickup, args);
        SetObjectInHandObserverLogic!.Invoke(pickup, args);
        pickup.HandsReconstruct();
        pickup.UpdateIKPoistion();
        item.InstantComeBackOnFire();
        if (item != null) item.dispenserStart = false;
    }

    private static bool IsCurrentRequest(int playerId, int requestVersion)
    {
        return RequestVersions.TryGetValue(playerId, out int currentVersion)
            && currentVersion == requestVersion;
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

        if (!ResolveAttachmentMethods())
        {
            return;
        }
        object[] args = { weapon, expectedParent.position, expectedParent.rotation, pickup.gameObject, true };
        SetObjectInHandObserverLogic!.Invoke(pickup, args);
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

    internal static void ClearHeldWeapons(PlayerPickup pickup)
    {
        NetworkManager? networkManager = FishNet.InstanceFinder.NetworkManager;
        if (networkManager == null || !networkManager.IsServer)
        {
            return;
        }

        DespawnHeldWeapon(networkManager, pickup.objInHand);
        DespawnHeldWeapon(networkManager, pickup.objInLeftHand);
        pickup.sync___set_value_hasObjectInHand(false, true);
        pickup.sync___set_value_hasObjectInLeftHand(false, true);
        pickup.sync___set_value_objInHand(null, true);
        pickup.sync___set_value_objInLeftHand(null, true);
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

    private static bool ResolveAttachmentMethods()
    {
        if (_attachmentMethodsResolved)
        {
            return _attachmentMethodsAvailable;
        }

        _attachmentMethodsResolved = true;
        SetObjectInHandLogic = FindAttachmentMethod("RpcLogic___SetObjectInHandServer_");
        SetObjectInHandObserver = FindAttachmentMethod("SetObjectInHandObserver");
        SetObjectInHandObserverLogic = FindAttachmentMethod("RpcLogic___SetObjectInHandObserver_");
        _attachmentMethodsAvailable = SetObjectInHandLogic != null
            && SetObjectInHandObserver != null
            && SetObjectInHandObserverLogic != null;
        if (!_attachmentMethodsAvailable)
        {
            Plugin.Logger.LogError("[WeaponService] Could not resolve FishNet weapon attachment methods. "
                + "Weapon grants are disabled until the game assembly is updated.");
        }
        return _attachmentMethodsAvailable;
    }

    private static MethodInfo? FindAttachmentMethod(string namePrefix)
    {
        MethodInfo[] candidates = typeof(PlayerPickup).GetMethods(Flags)
            .Where(method => method.Name.StartsWith(namePrefix, StringComparison.Ordinal)
                && HasAttachmentSignature(method)).ToArray();
        if (candidates.Length > 1)
        {
            Plugin.Logger.LogWarning($"[WeaponService] Multiple attachment methods match '{namePrefix}'. "
                + $"Using '{candidates[0].Name}'.");
        }
        return candidates.FirstOrDefault();
    }

    private static bool HasAttachmentSignature(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        return parameters.Length == 5
            && parameters[0].ParameterType == typeof(GameObject)
            && parameters[1].ParameterType == typeof(Vector3)
            && parameters[2].ParameterType == typeof(Quaternion)
            && parameters[3].ParameterType == typeof(GameObject)
            && parameters[4].ParameterType == typeof(bool);
    }

}