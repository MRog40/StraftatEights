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

namespace Eights;

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
    private static readonly RequestVersionTracker DefaultKnifeRequestVersions = new();
    private const byte RightHandAttachmentMask = 1;
    private const byte LeftHandAttachmentMask = 2;
    private static readonly Dictionary<int, byte> PendingOwnerAttachments = new();
    private static readonly Dictionary<int, int> PendingCouperetDespawns = new();
    private static int _couperetDropSequence;

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
        DefaultKnifeRequestVersions.Clear();
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
        bool unlimitedAmmo = false, bool clearBothHands = true, bool onlyIfNoGun = false)
    {
        if (Plugin.Instance != null && !IsFinalGameScreen)
        {
            int requestVersion = RequestVersions.Next(playerId);
            int defaultKnifeRequestVersion = onlyIfNoGun
                ? DefaultKnifeRequestVersions.Next(playerId)
                : 0;
            Plugin.Instance.StartCoroutine(GiveWeaponCoroutine(playerId, weaponName, spareMagazines, unlimitedAmmo,
                SessionState.Generation, GameModeManager.RoundId, requestVersion, RequestVersions, true,
                clearBothHands, onlyIfNoGun, defaultKnifeRequestVersion));
        }
    }

    internal static void GiveWeaponToLeftHand(int playerId, string weaponName)
    {
        if (Plugin.Instance != null && !IsFinalGameScreen)
        {
            int requestVersion = LeftHandRequestVersions.Next(playerId);
            Plugin.Instance.StartCoroutine(GiveWeaponCoroutine(playerId, weaponName, null, false,
                SessionState.Generation, GameModeManager.RoundId, requestVersion,
                LeftHandRequestVersions, false, false, false));
        }
    }

    private static IEnumerator GiveWeaponCoroutine(int playerId, string weaponName, int? spareMagazines,
        bool unlimitedAmmo,
        int sessionGeneration, int roundId, int requestVersion, RequestVersionTracker requestVersions,
        bool rightHand, bool clearBothHands, bool onlyIfNoGun, int defaultKnifeRequestVersion = 0)
    {
        NetworkManager? networkManager = FishNet.InstanceFinder.NetworkManager;
        GameObject? prefab = FindPrefab(weaponName);
        if (prefab == null)
        {
            Plugin.Logger.LogWarning($"Weapon prefab '{weaponName}' was not found in Resources/RandomWeapons "
                + $"for playerId={playerId} hand={(rightHand ? "right" : "left")}.");
            yield break;
        }

        if (!IsCurrentWeaponGrant(playerId, requestVersion, requestVersions,
                onlyIfNoGun, defaultKnifeRequestVersion)
            || !SessionState.IsCurrent(sessionGeneration)
            || GameModeManager.RoundId != roundId
            || networkManager == null || !networkManager.IsServer
            || !ResolveAttachmentMethods()) yield break;

        PlayerPickup? pickup = null;
        FirstPersonController? player = null;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (!IsCurrentWeaponGrant(playerId, requestVersion, requestVersions,
                    onlyIfNoGun, defaultKnifeRequestVersion)
                || !SessionState.IsCurrent(sessionGeneration)
                || GameModeManager.RoundId != roundId
                || IsFinalGameScreen) yield break;
            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(playerId);
            if (health != null && health && health.gameObject.activeInHierarchy)
            {
                player = health.GetComponent<FirstPersonController>();
                pickup = player != null && player ? player.playerPickupScript : null;
            }
            if (pickup != null && pickup && player != null && player) break;
            yield return new WaitForSeconds(0.25f);
        }
        if (pickup == null || !pickup || player == null || !player
            || !IsCurrentWeaponGrant(playerId, requestVersion, requestVersions,
                onlyIfNoGun, defaultKnifeRequestVersion)
            || !SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId
            || IsFinalGameScreen || (onlyIfNoGun && HasAnyHeldObject(pickup))) yield break;

        if (!onlyIfNoGun && clearBothHands)
        {
            DespawnHeldWeapon(networkManager, pickup.objInHand);
            DespawnHeldWeapon(networkManager, pickup.objInLeftHand);
            pickup.sync___set_value_hasObjectInHand(false, true);
            pickup.sync___set_value_hasObjectInLeftHand(false, true);
            pickup.sync___set_value_objInHand(null, true);
            pickup.sync___set_value_objInLeftHand(null, true);
        }
        else if (!onlyIfNoGun && rightHand)
        {
            DespawnHeldWeapon(networkManager, pickup.objInHand);
            pickup.sync___set_value_hasObjectInHand(false, true);
            pickup.sync___set_value_objInHand(null, true);
        }
        else if (!onlyIfNoGun)
        {
            DespawnHeldWeapon(networkManager, pickup.objInLeftHand);
            pickup.sync___set_value_hasObjectInLeftHand(false, true);
            pickup.sync___set_value_objInLeftHand(null, true);
        }
        yield return new WaitForSeconds(0.15f);
        if (!IsCurrentWeaponGrant(playerId, requestVersion, requestVersions,
                onlyIfNoGun, defaultKnifeRequestVersion)
            || !SessionState.IsCurrent(sessionGeneration)
            || GameModeManager.RoundId != roundId
            || IsFinalGameScreen || (onlyIfNoGun && HasAnyHeldObject(pickup))) yield break;

        GameObject weapon = UnityEngine.Object.Instantiate(prefab, player.transform.position, player.transform.rotation);
        ItemBehaviour? item = weapon.GetComponent<ItemBehaviour>();
        if (item != null) item.dispenserStart = true;
        Rigidbody? body = weapon.GetComponent<Rigidbody>();
        if (body != null) { body.isKinematic = true; body.useGravity = false; }
        networkManager.ServerManager.Spawn(weapon);
        yield return new WaitForSeconds(0.1f);
        if (!IsCurrentWeaponGrant(playerId, requestVersion, requestVersions,
                onlyIfNoGun, defaultKnifeRequestVersion)
            || !SessionState.IsCurrent(sessionGeneration)
            || GameModeManager.RoundId != roundId
            || IsFinalGameScreen || (onlyIfNoGun && HasAnyHeldObject(pickup)))
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
        object[] args = { weapon, hand.position, hand.rotation, player.gameObject, rightHand };
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
        NotifyOwnerWeaponAttached(playerId, rightHand);
    }

    private static bool HasAnyHeldObject(PlayerPickup pickup)
    {
        return pickup.hasObjectInHand || pickup.hasObjectInLeftHand
            || IsHeldObject(pickup.objInHand) || IsHeldObject(pickup.objInLeftHand);
    }

    private static bool IsHeldObject(GameObject? heldObject)
    {
        return heldObject != null && heldObject;
    }

    private static bool IsCurrentWeaponGrant(int playerId, int requestVersion,
        RequestVersionTracker requestVersions, bool onlyIfNoGun,
        int defaultKnifeRequestVersion)
    {
        return requestVersions.IsCurrent(playerId, requestVersion)
            && (!onlyIfNoGun || DefaultKnifeRequestVersions.IsCurrent(playerId,
                defaultKnifeRequestVersion));
    }

    internal static void CancelPendingDefaultKnifeGrant(int playerId)
    {
        if (playerId >= 0)
        {
            DefaultKnifeRequestVersions.Next(playerId);
        }
    }

    private static bool IsGun(GameObject? heldObject)
    {
        if (heldObject == null || !heldObject)
        {
            return false;
        }

        Weapon? weapon = heldObject.GetComponent<Weapon>();
        return weapon != null && weapon
            && !DefaultKnifeRules.IsKnife(weapon.name);
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

    internal static bool RemoveHeldWeapon(PlayerPickup pickup, bool rightHand, Weapon weapon)
    {
        NetworkManager? networkManager = FishNet.InstanceFinder.NetworkManager;
        if (networkManager == null || !networkManager.IsServer || pickup == null
            || weapon == null || !weapon)
        {
            return false;
        }

        GameObject? heldObject = rightHand ? pickup.objInHand : pickup.objInLeftHand;
        if (heldObject == null || !heldObject || heldObject != weapon.gameObject)
        {
            return false;
        }

        if (rightHand)
        {
            pickup.sync___set_value_hasObjectInHand(false, true);
            pickup.sync___set_value_objInHand(null, true);
        }
        else
        {
            pickup.sync___set_value_hasObjectInLeftHand(false, true);
            pickup.sync___set_value_objInLeftHand(null, true);
        }

        DespawnHeldWeapon(networkManager, heldObject);
        pickup.HandsReconstruct();
        pickup.UpdateIKPoistion();
        return true;
    }

    internal static void QueueCouperetDropDespawn(PlayerPickup pickup, GameObject heldObject,
        bool rightHand)
    {
        NetworkManager? networkManager = FishNet.InstanceFinder.NetworkManager;
        if (networkManager == null || !networkManager.IsServer || pickup == null || !pickup
            || heldObject == null || !heldObject)
        {
            return;
        }

        int objectId = heldObject.GetInstanceID();
        int sequence = unchecked(++_couperetDropSequence);
        PendingCouperetDespawns[objectId] = sequence;
        if (Plugin.Instance != null)
        {
            Plugin.Instance.StartCoroutine(DespawnCouperetAfterDrop(pickup, heldObject,
                rightHand, objectId, sequence));
        }
        else
        {
            DespawnHeldWeapon(networkManager, heldObject);
        }
    }

    internal static void CancelPendingCouperetDrop(GameObject heldObject)
    {
        if (heldObject != null && heldObject)
        {
            PendingCouperetDespawns.Remove(heldObject.GetInstanceID());
        }
    }

    private static IEnumerator DespawnCouperetAfterDrop(PlayerPickup pickup, GameObject heldObject,
        bool rightHand, int objectId, int sequence)
    {
        yield return new WaitForSecondsRealtime(0.2f);
        if (!PendingCouperetDespawns.TryGetValue(objectId, out int pendingSequence)
            || pendingSequence != sequence)
        {
            yield break;
        }

        PendingCouperetDespawns.Remove(objectId);
        NetworkManager? networkManager = FishNet.InstanceFinder.NetworkManager;
        if (networkManager == null || !networkManager.IsServer || heldObject == null || !heldObject)
        {
            yield break;
        }

        Weapon? weapon = heldObject.GetComponent<Weapon>();
        if (weapon == null || !RemoveHeldWeapon(pickup, rightHand, weapon))
        {
            DespawnHeldWeapon(networkManager, heldObject);
        }
    }

    internal static void AttachGrantedWeaponForOwner(int playerId, bool rightHand)
    {
        if (Plugin.Instance == null)
        {
            return;
        }

        byte handMask = rightHand ? RightHandAttachmentMask : LeftHandAttachmentMask;
        PendingOwnerAttachments.TryGetValue(playerId, out byte pendingMask);
        if ((pendingMask & handMask) != 0)
        {
            return;
        }

        PendingOwnerAttachments[playerId] = (byte)(pendingMask | handMask);
        Plugin.Instance.StartCoroutine(AttachGrantedWeaponAfterSync(playerId, rightHand,
            SessionState.Generation));
    }

    internal static bool IsOwnerAttachmentPending(PlayerPickup pickup)
    {
        return pickup.IsOwner && ClientInstance.Instance != null
            && PendingOwnerAttachments.ContainsKey(ClientInstance.Instance.PlayerId);
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

    internal static bool IsOwnerHandObjectUnattached(PlayerPickup pickup, bool rightHand)
    {
        if (!pickup.IsOwner)
        {
            return false;
        }

        bool hasObject = rightHand ? pickup.hasObjectInHand : pickup.hasObjectInLeftHand;
        GameObject? objectInHand = rightHand ? pickup.objInHand : pickup.objInLeftHand;
        if (!hasObject || objectInHand == null || !objectInHand)
        {
            return false;
        }

        ItemBehaviour? item = objectInHand.GetComponent<ItemBehaviour>();
        Weapon? weapon = objectInHand.GetComponent<Weapon>();
        if (item == null || weapon == null)
        {
            return false;
        }

        try
        {
            Transform expectedParent = rightHand
                ? (weapon.requireBothHands
                    ? pickup.pickupPositionBothHand[item.camChildIndex]
                    : pickup.pickupPositionRightHand[item.camChildIndex])
                : pickup.pickupPositionLeftHand[item.camChildIndexLeftHand];
            bool inExpectedHand = rightHand ? weapon.inRightHand : weapon.inLeftHand;
            return objectInHand.transform.parent != expectedParent
                || !inExpectedHand
                || item.playerPickup != pickup
                || item.rootObject != pickup.gameObject;
        }
        catch
        {
            return true;
        }
    }

    private static IEnumerator AttachGrantedWeaponAfterSync(int playerId, bool rightHand,
        int sessionGeneration)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            if (!SessionState.IsCurrent(sessionGeneration))
            {
                ClearPendingOwnerAttachment(playerId, rightHand);
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
            GameObject? expectedObject = rightHand ? rightObject : leftObject;
            if (pickup != null && expectedObject != null && expectedObject)
            {
                bool hasRightObject = rightObject != null && rightObject;
                bool hasLeftObject = leftObject != null && leftObject;
                pickup.hasObjectInHand = hasRightObject;
                pickup.hasObjectInLeftHand = hasLeftObject;

                bool attached = AttachWeaponLocally(pickup, expectedObject, rightHand);
                if (attached)
                {
                    ClearPendingOwnerAttachment(playerId, rightHand);
                    yield break;
                }
            }


            yield return new WaitForSeconds(0.1f);
        }

        ClearPendingOwnerAttachment(playerId, rightHand);
    }

    private static void ClearPendingOwnerAttachment(int playerId, bool rightHand)
    {
        if (!PendingOwnerAttachments.TryGetValue(playerId, out byte pendingMask))
        {
            return;
        }

        byte handMask = rightHand ? RightHandAttachmentMask : LeftHandAttachmentMask;
        pendingMask = (byte)(pendingMask & ~handMask);
        if (pendingMask == 0)
        {
            PendingOwnerAttachments.Remove(playerId);
        }
        else
        {
            PendingOwnerAttachments[playerId] = pendingMask;
        }
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

    internal static void NotifyOwnerWeaponAttached(int playerId, bool rightHand)
    {
        if (!MyceliumNetwork.InLobby || !MyceliumNetwork.IsHost)
        {
            return;
        }

        if (ClientInstance.Instance != null && ClientInstance.Instance.PlayerId == playerId)
        {
            AttachGrantedWeaponForOwner(playerId, rightHand);
        }

        MyceliumNetwork.RPC(Plugin.GlobalWeaponsModId, nameof(Plugin.AttachServerGrantedWeapon),
            ReliableType.Reliable, playerId, rightHand);
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