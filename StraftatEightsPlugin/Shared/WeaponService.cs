using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FishNet.Managing;
using FishNet.Object;
using MyceliumNetworking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StraftatEightsPlugin;

internal static class WeaponService
{
    private static readonly Dictionary<string, GameObject> Prefabs = new(StringComparer.Ordinal);
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static MethodInfo? SetObjectInHandLogic;
    private static MethodInfo? SetObjectInHandObserverLogic;
    private static bool _attachmentMethodsResolved;
    private static bool _attachmentMethodsAvailable;
    private static readonly RequestVersionTracker RequestVersions = new();
    private static readonly RequestVersionTracker LeftHandRequestVersions = new();
    private static readonly HashSet<int> PendingOwnerAttachments = new();

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
        LeftHandRequestVersions.Clear();
        PendingOwnerAttachments.Clear();
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
        return WeaponListParser.Parse(value, Prefabs.Keys);
    }

    internal static void GiveWeapon(int playerId, string weaponName, int? spareMagazines = null,
        bool unlimitedAmmo = false, bool clearBothHands = true)
    {
        if (Plugin.Instance != null && !IsFinalGameScreen)
        {
            int requestVersion = RequestVersions.Next(playerId);
            Plugin.Instance.StartCoroutine(GiveWeaponCoroutine(playerId, weaponName, spareMagazines, unlimitedAmmo,
                SessionState.Generation, GameModeManager.RoundId, requestVersion, RequestVersions, true,
                clearBothHands));
        }
    }

    internal static void GiveWeaponToLeftHand(int playerId, string weaponName)
    {
        if (Plugin.Instance != null && !IsFinalGameScreen)
        {
            int requestVersion = LeftHandRequestVersions.Next(playerId);
            Plugin.Instance.StartCoroutine(GiveWeaponCoroutine(playerId, weaponName, null, false,
                SessionState.Generation, GameModeManager.RoundId, requestVersion,
                LeftHandRequestVersions, false, false));
        }
    }

    private static IEnumerator GiveWeaponCoroutine(int playerId, string weaponName, int? spareMagazines,
        bool unlimitedAmmo,
        int sessionGeneration, int roundId, int requestVersion, RequestVersionTracker requestVersions,
        bool rightHand, bool clearBothHands)
    {
        NetworkManager? networkManager = FishNet.InstanceFinder.NetworkManager;
        GameObject? prefab = FindPrefab(weaponName);
        if (prefab == null)
        {
            Plugin.Logger.LogWarning($"Weapon prefab '{weaponName}' was not found in Resources/RandomWeapons.");
            yield break;
        }

        if (!requestVersions.IsCurrent(playerId, requestVersion) || !SessionState.IsCurrent(sessionGeneration)
            || GameModeManager.RoundId != roundId
            || networkManager == null || !networkManager.IsServer
            || !ResolveAttachmentMethods()) yield break;

        PlayerPickup? pickup = null;
        PlayerManager? manager = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (!requestVersions.IsCurrent(playerId, requestVersion) || !SessionState.IsCurrent(sessionGeneration)
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
        if (pickup == null || manager?.player == null || !requestVersions.IsCurrent(playerId, requestVersion)
            || !SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || IsFinalGameScreen) yield break;

        if (clearBothHands)
        {
            DespawnHeldWeapon(networkManager, pickup.objInHand);
            DespawnHeldWeapon(networkManager, pickup.objInLeftHand);
            pickup.sync___set_value_hasObjectInHand(false, true);
            pickup.sync___set_value_hasObjectInLeftHand(false, true);
            pickup.sync___set_value_objInHand(null, true);
            pickup.sync___set_value_objInLeftHand(null, true);
        }
        else if (rightHand)
        {
            DespawnHeldWeapon(networkManager, pickup.objInHand);
            pickup.sync___set_value_hasObjectInHand(false, true);
            pickup.sync___set_value_objInHand(null, true);
        }
        else
        {
            DespawnHeldWeapon(networkManager, pickup.objInLeftHand);
            pickup.sync___set_value_hasObjectInLeftHand(false, true);
            pickup.sync___set_value_objInLeftHand(null, true);
        }
        yield return new WaitForSeconds(0.15f);
        if (!requestVersions.IsCurrent(playerId, requestVersion) || !SessionState.IsCurrent(sessionGeneration)
            || GameModeManager.RoundId != roundId
            || IsFinalGameScreen) yield break;

        GameObject weapon = UnityEngine.Object.Instantiate(prefab, manager.player.transform.position, manager.player.transform.rotation);
        ItemBehaviour? item = weapon.GetComponent<ItemBehaviour>();
        if (item != null) item.dispenserStart = true;
        Rigidbody? body = weapon.GetComponent<Rigidbody>();
        if (body != null) { body.isKinematic = true; body.useGravity = false; }
        networkManager.ServerManager.Spawn(weapon);
        yield return new WaitForSeconds(0.1f);
        if (!requestVersions.IsCurrent(playerId, requestVersion) || !SessionState.IsCurrent(sessionGeneration)
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

        WeaponAmmoTuning.ResetWeaponState(weaponComponent);

        if (unlimitedAmmo)
        {
            WeaponAmmoTuning.InitializeUnlimited(weaponComponent);
        }
        else if (spareMagazines.HasValue)
        {
            WeaponAmmoTuning.InitializeFromSpawnerPickup(weaponComponent, spareMagazines.Value);
        }

        Transform hand = rightHand
            ? (weaponComponent.requireBothHands
                ? pickup.pickupPositionBothHand[item.camChildIndex]
                : pickup.pickupPositionRightHand[item.camChildIndex])
            : pickup.pickupPositionLeftHand[item.camChildIndexLeftHand];
        weapon.transform.SetPositionAndRotation(hand.position, hand.rotation);
        object[] args = { weapon, hand.position, hand.rotation, manager.player.gameObject, rightHand };
        SetObjectInHandLogic!.Invoke(pickup, args);
        if (rightHand)
        {
            pickup.sync___set_value_hasObjectInHand(true, true);
            pickup.sync___set_value_objInHand(weapon, true);
        }
        else
        {
            pickup.sync___set_value_hasObjectInLeftHand(true, true);
            pickup.sync___set_value_objInLeftHand(weapon, true);
        }
        SetObjectInHandObserverLogic!.Invoke(pickup, args);
        pickup.HandsReconstruct();
        if (!rightHand)
        {
            pickup.SetLeftIKTarget(item.gripLeft);
        }
        pickup.UpdateIKPoistion();
        item.InstantComeBackOnFire();
        if (item != null) item.dispenserStart = false;
        NotifyOwnerWeaponAttached(playerId);
    }

    internal static void AttachUnparentedWeapon(PlayerPickup pickup)
    {
        AttachUnparentedWeapon(pickup, true);
    }

    internal static void AttachUnparentedLeftWeapon(PlayerPickup pickup)
    {
        AttachUnparentedWeapon(pickup, false);
    }

    private static void AttachUnparentedWeapon(PlayerPickup pickup, bool rightHand)
    {
        bool hasWeapon = rightHand ? pickup.hasObjectInHand : pickup.hasObjectInLeftHand;
        GameObject? weapon = rightHand ? pickup.objInHand : pickup.objInLeftHand;
        if (!pickup.IsOwner || !hasWeapon || weapon == null)
        {
            return;
        }

        ItemBehaviour? item = weapon.GetComponent<ItemBehaviour>();
        Weapon? weaponComponent = weapon.GetComponent<Weapon>();
        if (item == null || weaponComponent == null)
        {
            return;
        }

        Transform? expectedParent = rightHand
            ? (weaponComponent.requireBothHands
                ? pickup.pickupPositionBothHand[item.camChildIndex]
                : pickup.pickupPositionRightHand[item.camChildIndex])
            : pickup.pickupPositionLeftHand[item.camChildIndexLeftHand];
        if (expectedParent == null)
        {
            return;
        }

        bool isInExpectedHand = rightHand ? weaponComponent.inRightHand : weaponComponent.inLeftHand;
        if (weapon.transform.parent == expectedParent && isInExpectedHand &&
            item.playerPickup == pickup && item.rootObject == pickup.gameObject)
        {
            return;
        }

        if (!ResolveAttachmentMethods())
        {
            return;
        }
        object[] args = { weapon, expectedParent.position, expectedParent.rotation, pickup.gameObject, rightHand };
        SetObjectInHandObserverLogic!.Invoke(pickup, args);
        pickup.HandsReconstruct();
        if (rightHand)
        {
            pickup.SetRightIKTarget(item.gripRight);
            if (weaponComponent.requireBothHands)
            {
                pickup.SetLeftIKTarget(item.gripLeft);
            }
        }
        else
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

    internal static void AttachGrantedWeaponForOwner(int playerId)
    {
        if (Plugin.Instance == null)
        {
            return;
        }

        if (!PendingOwnerAttachments.Add(playerId))
        {
            return;
        }
        Plugin.Instance.StartCoroutine(AttachGrantedWeaponAfterSync(playerId, SessionState.Generation));
    }

    internal static bool IsOwnerAttachmentPending(PlayerPickup pickup)
    {
        return pickup.IsOwner && ClientInstance.Instance != null
            && PendingOwnerAttachments.Contains(ClientInstance.Instance.PlayerId);
    }

    internal static bool IsOwnerHandObjectPending(PlayerPickup pickup, bool rightHand)
    {
        if (!pickup.IsOwner)
        {
            return false;
        }

        bool hasObject = rightHand ? pickup.hasObjectInHand : pickup.hasObjectInLeftHand;
        GameObject? objectInHand = rightHand ? pickup.objInHand : pickup.objInLeftHand;
        return hasObject && (objectInHand == null || !objectInHand);
    }

    private static IEnumerator AttachGrantedWeaponAfterSync(int playerId, int sessionGeneration)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (!SessionState.IsCurrent(sessionGeneration))
            {
                PendingOwnerAttachments.Remove(playerId);
                yield break;
            }

            if (ClientInstance.Instance == null || ClientInstance.Instance.PlayerId != playerId)
            {
                yield return new WaitForSeconds(0.1f);
                continue;
            }

            PlayerManager? manager = ClientInstance.Instance.PlayerSpawner;
            PlayerPickup? pickup = manager?.player?.playerPickupScript;
            GameObject? rightObject = pickup?.objInHand;
            GameObject? leftObject = pickup?.objInLeftHand;
            if (pickup != null && ((rightObject != null && rightObject) || (leftObject != null && leftObject)))
            {
                pickup.hasObjectInHand = rightObject != null && rightObject;
                pickup.hasObjectInLeftHand = leftObject != null && leftObject;
                bool rightAttached = rightObject == null || !rightObject || AttachWeaponLocally(pickup, rightObject, true);
                bool leftAttached = leftObject == null || !leftObject || AttachWeaponLocally(pickup, leftObject, false);
                if (rightAttached && leftAttached)
                {
                    PendingOwnerAttachments.Remove(playerId);
                    yield break;
                }
            }

            yield return new WaitForSeconds(0.1f);
        }

        PendingOwnerAttachments.Remove(playerId);
    }

    private static bool AttachWeaponLocally(PlayerPickup pickup, GameObject weapon, bool rightHand)
    {
        Weapon? weaponComponent = weapon.GetComponent<Weapon>();
        ItemBehaviour? item = weapon.GetComponent<ItemBehaviour>();
        if (weaponComponent == null || item == null || !ResolveAttachmentMethods())
        {
            return false;
        }

        try
        {
            Transform hand = rightHand
                ? (weaponComponent.requireBothHands
                    ? pickup.pickupPositionBothHand[item.camChildIndex]
                    : pickup.pickupPositionRightHand[item.camChildIndex])
                : pickup.pickupPositionLeftHand[item.camChildIndexLeftHand];
            object[] args = { weapon, hand.position, hand.rotation, pickup.gameObject, rightHand };
            SetObjectInHandObserverLogic!.Invoke(pickup, args);
            pickup.HandsReconstruct();
            if (!rightHand)
            {
                pickup.SetLeftIKTarget(item.gripLeft);
            }
            pickup.UpdateIKPoistion();
            item.InstantComeBackOnFire();
            item.dispenserStart = false;
            return weapon.layer == 8;
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogDebug($"[WeaponService] Owner attachment retry: {exception.GetBaseException().Message}");
            return false;
        }
    }

    internal static void NotifyOwnerWeaponAttached(int playerId)
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        if (ClientInstance.Instance != null && ClientInstance.Instance.PlayerId == playerId)
        {
            AttachGrantedWeaponForOwner(playerId);
        }

        MyceliumNetwork.RPC(Plugin.GlobalWeaponsModId, nameof(Plugin.AttachServerGrantedWeapon),
            ReliableType.Reliable, playerId);
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
        SetObjectInHandObserverLogic = FindAttachmentMethod("RpcLogic___SetObjectInHandObserver_");
        _attachmentMethodsAvailable = SetObjectInHandLogic != null
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