using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class WeaponAmmoTuning
{
    private const float CustomReloadTime = 1.75f;
    private sealed class Memory
    {
        public int MagazineSize;
        public int SpareRounds;
        public bool Initialized;
        public bool Reloading;
        public bool ManualReloadPressed;
        public bool OriginalInHandDespawn;
    }

    private static readonly ConditionalWeakTable<Weapon, Memory> MemoryByWeapon = new();
    private static bool fallbackReloadClipResolved;
    private static AudioClip? fallbackReloadClip;
    private static Coroutine? hudRefreshCoroutine;

    internal static void Initialize(Weapon weapon, int spareMagazines)
    {
        if (weapon == null || !weapon.needsAmmo)
        {
            return;
        }

        Memory memory = MemoryByWeapon.GetOrCreateValue(weapon);
        if (memory.Initialized)
        {
            return;
        }

        memory.MagazineSize = Mathf.Max(1, weapon.currentAmmo);
        memory.SpareRounds = memory.MagazineSize * Mathf.Max(0, spareMagazines);
        memory.Initialized = true;
    }

    internal static void InitializeFromSpawnerPickup(Weapon weapon, int spareMagazines)
    {
        if (weapon == null || !weapon.needsAmmo)
        {
            return;
        }

        Memory memory = MemoryByWeapon.GetOrCreateValue(weapon);
        memory.Reloading = false;
        memory.ManualReloadPressed = false;

        if (weapon.reloadWeapon)
        {
            memory.MagazineSize = Mathf.Max(1, weapon.ammoCharge > 0 ? weapon.ammoCharge : Mathf.RoundToInt(weapon.chargedBullets));
            weapon.currentAmmo = memory.MagazineSize * Mathf.Max(0, spareMagazines);
            memory.SpareRounds = weapon.currentAmmo;
        }
        else
        {
            memory.MagazineSize = Mathf.Max(1, weapon.currentAmmo);
            memory.SpareRounds = memory.MagazineSize * Mathf.Max(0, spareMagazines);
            weapon.currentAmmo = memory.MagazineSize;
        }

        memory.Initialized = true;
    }

    private static object? GetFieldValue(Weapon weapon, string fieldName)
    {
        for (Type? type = weapon.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                return field.GetValue(weapon);
            }
        }

        return null;
    }

    private static void SetFieldValue(Weapon weapon, string fieldName, object value)
    {
        for (Type? type = weapon.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null)
            {
                field.SetValue(weapon, value);
                return;
            }
        }
    }

    internal static int GetSpareRounds(Weapon weapon)
    {
        return MemoryByWeapon.TryGetValue(weapon, out Memory memory) ? memory.SpareRounds : 0;
    }

    internal static bool IsReloading(Weapon weapon)
    {
        return MemoryByWeapon.TryGetValue(weapon, out Memory memory) && memory.Reloading;
    }

    internal static void ScheduleLocalAmmoHudRefresh()
    {
        if (Plugin.Instance == null || hudRefreshCoroutine != null)
        {
            return;
        }

        hudRefreshCoroutine = Plugin.Instance.StartCoroutine(RefreshLocalAmmoHudAfterReset());
    }

    private static IEnumerator RefreshLocalAmmoHudAfterReset()
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            yield return new WaitForSeconds(0.1f);
            RefreshLocalAmmoHud();
        }

        hudRefreshCoroutine = null;
    }

    internal static void RefreshLocalAmmoHud()
    {
        if (!WeaponSettingsState.Enabled || PauseManager.Instance == null || ClientInstance.Instance == null)
        {
            return;
        }

        FirstPersonController? player = ClientInstance.Instance.PlayerSpawner?.player;
        PlayerPickup? pickup = player?.playerPickupScript;
        if (pickup == null)
        {
            return;
        }

        RefreshHeldWeaponHud(pickup.objInHand, true);
        RefreshHeldWeaponHud(pickup.objInLeftHand, false);
    }

    private static void RefreshHeldWeaponHud(GameObject? heldObject, bool rightHand)
    {
        Weapon? weapon = heldObject?.GetComponent<Weapon>();
        if (weapon == null || !weapon.needsAmmo || weapon.reloadWeapon || weapon.gameObject.layer != 8)
        {
            return;
        }

        Initialize(weapon, WeaponSettingsState.SpareMagazines);
        int currentAmmo = Mathf.Max(0, weapon.currentAmmo);
        PauseManager.Instance.MoveAmmoDisplay(true, rightHand);
        PauseManager.Instance.ChangeAmmoText(GetSpareRounds(weapon).ToString(), currentAmmo + " / ", rightHand);
    }

    internal static void TryStartManualReload(Weapon weapon, bool enabled, int spareMagazines)
    {
        if (weapon == null || !weapon.needsAmmo)
        {
            return;
        }

        Initialize(weapon, spareMagazines);
        if (!MemoryByWeapon.TryGetValue(weapon, out Memory memory))
        {
            return;
        }

        bool reloadPressed = weapon.gameObject.layer == 8 && weapon.reload != null && weapon.reload.ReadValue<float>() > 0.1f;
        bool wasReloadPressed = memory.ManualReloadPressed;
        memory.ManualReloadPressed = reloadPressed;

        if (!enabled || !reloadPressed || wasReloadPressed || weapon.reloadWeapon || !weapon.IsOwner || weapon.gameObject.layer != 8 || memory.Reloading || memory.SpareRounds <= 0 || weapon.currentAmmo >= memory.MagazineSize)
        {
            return;
        }

        StartReload(weapon, memory);
    }

    internal static void ApplyToWeapon(Weapon weapon, bool enabled, int spareMagazines)
    {
        if (!enabled || weapon == null || !weapon.needsAmmo)
        {
            return;
        }
        Initialize(weapon, spareMagazines);
        if (weapon.gameObject.layer == 8)
        {
            ReloadIfEmpty(weapon, spareMagazines);
        }
    }

    private static void ReloadIfEmpty(Weapon weapon, int spareMagazines)
    {
        if (weapon == null || weapon.reloadWeapon || !weapon.needsAmmo)
        {
            return;
        }

        Initialize(weapon, spareMagazines);
        if (!MemoryByWeapon.TryGetValue(weapon, out Memory memory) || memory.SpareRounds <= 0)
        {
            return;
        }

        if (weapon.currentAmmo > 0 || memory.Reloading)
        {
            return;
        }

        StartReload(weapon, memory);
    }

    private static void StartReload(Weapon weapon, Memory memory)
    {
        memory.Reloading = true;
        memory.OriginalInHandDespawn = GetFieldValue(weapon, "inHandDespawn") is bool inHandDespawn && inHandDespawn;
        weapon.cantTakeSafeBool = true;
        SetFieldValue(weapon, "inHandDespawn", false);
        weapon.isReloading = true;
        weapon.StartCoroutine(Reload(weapon, memory));
    }

    private static IEnumerator Reload(Weapon weapon, Memory memory)
    {
        AudioClip? reloadClip = GetFieldValue(weapon, "reloadClip") as AudioClip ?? GetFallbackReloadClip();
        if (reloadClip != null && weapon.audio != null)
        {
            weapon.audio.PlayOneShot(reloadClip);
        }

        float reloadTime = CustomReloadTime;
        bool hasReloadAnimation = TriggerReloadAnimation(weapon);
        weapon.OnReload();
        if (hasReloadAnimation)
        {
            yield return new WaitForSeconds(reloadTime);
        }
        else
        {
            yield return AnimateFallbackReload(weapon, reloadTime);
        }

        if (!memory.Reloading)
        {
            yield break;
        }

        int rounds = Mathf.Min(memory.MagazineSize, memory.SpareRounds);
        memory.SpareRounds -= rounds;
        weapon.currentAmmo = rounds;
        weapon.cantTakeSafeBool = false;
        weapon.noAmmoClicks = 0;
        memory.Reloading = false;
        weapon.isReloading = false;
        SetFieldValue(weapon, "inHandDespawn", memory.OriginalInHandDespawn);
    }

    private static bool TriggerReloadAnimation(Weapon weapon)
    {
        if (weapon.animator == null)
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in weapon.animator.parameters)
        {
            if (parameter.name == "Reload" && parameter.type == AnimatorControllerParameterType.Trigger)
            {
                weapon.animator.SetTrigger(parameter.name);
                return true;
            }
        }

        return false;
    }

    private static IEnumerator AnimateFallbackReload(Weapon weapon, float reloadTime)
    {
        Transform transform = weapon.transform;
        Vector3 initialPosition = transform.localPosition;
        Quaternion initialRotation = transform.localRotation;
        float elapsed = 0f;

        while (elapsed < reloadTime)
        {
            float progress = Mathf.Clamp01(elapsed / reloadTime);
            float envelope = Mathf.Sin(progress * Mathf.PI);
            transform.localPosition = initialPosition + Vector3.down * (0.08f * envelope);
            transform.localRotation = initialRotation * Quaternion.Euler(25f * envelope, 0f, 0f);
            elapsed += Time.deltaTime;
            yield return null;
        }

        transform.localPosition = initialPosition;
        transform.localRotation = initialRotation;
    }

    private static AudioClip? GetFallbackReloadClip()
    {
        if (fallbackReloadClipResolved)
        {
            return fallbackReloadClip;
        }

        fallbackReloadClipResolved = true;
        GameObject? prefab = WeaponService.FindPrefab("QCW05");
        Weapon? weapon = prefab?.GetComponent<Weapon>();
        fallbackReloadClip = weapon == null ? null : GetFieldValue(weapon, "reloadClip") as AudioClip;
        return fallbackReloadClip;
    }
}