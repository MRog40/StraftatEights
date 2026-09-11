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
        public bool SpareRoundsInitialized;
        public bool Reloading;
        public bool ManualReloadPressed;
        public bool OriginalInHandDespawn;
        public bool UnlimitedAmmo;
        public bool SingleShot;
    }

    private static readonly ConditionalWeakTable<Weapon, Memory> MemoryByWeapon = new();
    private static bool fallbackReloadClipResolved;
    private static AudioClip? fallbackReloadClip;
    private static Coroutine? hudRefreshCoroutine;

    internal static void CaptureMagazineSize(Weapon weapon)
    {
        if (weapon == null || !weapon.needsAmmo)
        {
            return;
        }

        Memory memory = MemoryByWeapon.GetOrCreateValue(weapon);
        if (memory.Initialized || memory.SingleShot)
        {
            return;
        }

        int magazineSize = GetPrefabMagazineSize(weapon);
        if (magazineSize <= 0)
        {
            magazineSize = weapon.reloadWeapon
                ? (weapon.ammoCharge > 0 ? weapon.ammoCharge : Mathf.RoundToInt(weapon.chargedBullets))
                : weapon.currentAmmo;
        }
        if (magazineSize > 0)
        {
            memory.MagazineSize = magazineSize;
            memory.Initialized = true;
        }
    }

    private static int GetPrefabMagazineSize(Weapon weapon)
    {
        string prefabName = weapon.name;
        const string cloneSuffix = "(Clone)";
        if (prefabName.EndsWith(cloneSuffix, StringComparison.Ordinal))
        {
            prefabName = prefabName.Substring(0, prefabName.Length - cloneSuffix.Length);
        }

        Weapon? prefabWeapon = WeaponService.FindPrefab(prefabName)?.GetComponent<Weapon>();
        if (prefabWeapon == null || !prefabWeapon.needsAmmo)
        {
            return 0;
        }

        return prefabWeapon.reloadWeapon
            ? (prefabWeapon.ammoCharge > 0
                ? prefabWeapon.ammoCharge
                : Mathf.RoundToInt(prefabWeapon.chargedBullets))
            : prefabWeapon.currentAmmo;
    }

    internal static void Initialize(Weapon weapon, int spareMagazines)
    {
        if (weapon == null || !weapon.needsAmmo)
        {
            return;
        }

        CaptureMagazineSize(weapon);
        Memory memory = MemoryByWeapon.GetOrCreateValue(weapon);
        if (!memory.Initialized)
        {
            memory.MagazineSize = Mathf.Max(1, weapon.currentAmmo);
            memory.Initialized = true;
        }
        if (!memory.SpareRoundsInitialized)
        {
            memory.SpareRounds = memory.MagazineSize * Mathf.Max(0, spareMagazines);
            memory.SpareRoundsInitialized = true;
        }
    }

    internal static void InitializeUnlimited(Weapon weapon, int magazineSizeOverride = 0)
    {
        if (weapon == null || !weapon.needsAmmo)
        {
            return;
        }

        Memory memory = MemoryByWeapon.GetOrCreateValue(weapon);
        CaptureMagazineSize(weapon);
        if (!memory.Initialized)
        {
            memory.MagazineSize = Mathf.Max(1, weapon.currentAmmo);
            memory.Initialized = true;
        }
        if (magazineSizeOverride > 0)
        {
            memory.MagazineSize = magazineSizeOverride;
            memory.Initialized = true;
        }
        memory.UnlimitedAmmo = true;

        bool shouldRestoreAmmo = weapon.currentAmmo <= 0
            && (weapon.reloadWeapon || weapon.gameObject.layer != 8);
        if (shouldRestoreAmmo)
        {
            weapon.CancelInvoke("DespawnObject");
            weapon.currentAmmo = memory.MagazineSize;
            weapon.cantTakeSafeBool = false;
            weapon.noAmmoClicks = 0;
        }
    }

    internal static void InitializeSingleShot(Weapon weapon, int spareRounds)
    {
        if (weapon == null || !weapon.needsAmmo)
        {
            return;
        }

        Memory memory = MemoryByWeapon.GetOrCreateValue(weapon);
        if (!memory.SingleShot)
        {
            memory.MagazineSize = 1;
            memory.SpareRounds = Mathf.Max(0, spareRounds);
            memory.Initialized = true;
            memory.UnlimitedAmmo = false;
            memory.SingleShot = true;
            memory.SpareRoundsInitialized = true;
            weapon.reloadWeapon = false;
            weapon.currentAmmo = 1;
        }
    }

    internal static void SetSingleShotSpareRounds(Weapon weapon, int spareRounds)
    {
        InitializeSingleShot(weapon, spareRounds);
        if (MemoryByWeapon.TryGetValue(weapon, out Memory memory))
        {
            memory.SpareRounds = Mathf.Max(0, spareRounds);
        }
    }

    internal static void AddSingleShotSpareRounds(Weapon weapon, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        InitializeSingleShot(weapon, 0);
        if (MemoryByWeapon.TryGetValue(weapon, out Memory memory))
        {
            memory.SpareRounds += amount;
        }
    }

    internal static bool IsSingleShot(Weapon weapon)
    {
        return MemoryByWeapon.TryGetValue(weapon, out Memory memory) && memory.SingleShot;
    }

    internal static void InitializeFromSpawnerPickup(Weapon weapon, int spareMagazines)
    {
        if (weapon == null || !weapon.needsAmmo)
        {
            return;
        }

        CaptureMagazineSize(weapon);
        Memory memory = MemoryByWeapon.GetOrCreateValue(weapon);
        memory.Reloading = false;
        memory.ManualReloadPressed = false;
        memory.UnlimitedAmmo = false;
        memory.SpareRoundsInitialized = true;

        if (weapon.reloadWeapon)
        {
            if (memory.MagazineSize <= 0)
            {
                memory.MagazineSize = Mathf.Max(1, weapon.ammoCharge > 0 ? weapon.ammoCharge : Mathf.RoundToInt(weapon.chargedBullets));
            }
            weapon.currentAmmo = memory.MagazineSize * Mathf.Max(0, spareMagazines);
            memory.SpareRounds = weapon.currentAmmo;
        }
        else
        {
            if (memory.MagazineSize <= 0)
            {
                memory.MagazineSize = Mathf.Max(1, weapon.currentAmmo);
            }
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

        hudRefreshCoroutine = Plugin.Instance.StartCoroutine(RefreshLocalAmmoHudAfterReset(SessionState.Generation));
    }

    internal static void ResetWeaponState(Weapon weapon)
    {
        if (weapon == null)
        {
            return;
        }

        Memory memory = MemoryByWeapon.GetOrCreateValue(weapon);
        memory.Reloading = false;
        memory.ManualReloadPressed = false;
        memory.OriginalInHandDespawn = false;
        weapon.isReloading = false;
        weapon.cantTakeSafeBool = false;
        weapon.noAmmoClicks = 0;
        weapon.shot = false;
    }

    internal static void PreventAutoDespawn(Weapon weapon)
    {
        if (weapon != null)
        {
            SetFieldValue(weapon, "inHandDespawn", false);
        }
    }

    private static IEnumerator RefreshLocalAmmoHudAfterReset(int sessionGeneration)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (!SessionState.IsCurrent(sessionGeneration))
            {
                hudRefreshCoroutine = null;
                yield break;
            }
            yield return new WaitForSeconds(0.1f);
            RefreshLocalAmmoHud();
        }

        hudRefreshCoroutine = null;
    }

    internal static void RefreshLocalAmmoHud()
    {
        if (PauseManager.Instance == null || ClientInstance.Instance == null)
        {
            return;
        }

        int localPlayerId = ClientInstance.Instance.PlayerId;
        PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(localPlayerId);
        PlayerSetup? setup = health?.GetComponent<PlayerSetup>();
        if (setup == null)
        {
            foreach (PlayerSetup candidate in UnityEngine.Object.FindObjectsOfType<PlayerSetup>())
            {
                if (candidate != null && candidate.IsOwner)
                {
                    setup = candidate;
                    health = candidate.GetComponent<PlayerHealth>();
                    break;
                }
            }
        }
        if (setup != null && setup.IsOwner)
        {
            if (!GameModeManager.ShouldHideCustomHud)
            {
                setup.HideHUD(false);
            }
        }

        PlayerPickup? pickup = health?.controller?.playerPickupScript;
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
        if (weapon == null || !weapon.needsAmmo || weapon.gameObject.layer != 8)
        {
            return;
        }

        if (WeaponSettingsState.Enabled)
        {
            Initialize(weapon, WeaponSettingsState.SpareMagazines);
        }
        int currentAmmo = Mathf.Max(0, weapon.currentAmmo);
        PauseManager.Instance.MoveAmmoDisplay(true, rightHand);
        string text;
        string reloadText;
        if (weapon.reloadWeapon)
        {
            text = currentAmmo.ToString();
            reloadText = Mathf.Max(0, weapon.chargedBullets) + " / ";
        }
        else if (WeaponSettingsState.Enabled)
        {
            text = GetSpareRounds(weapon).ToString();
            reloadText = currentAmmo + " / ";
        }
        else
        {
            text = currentAmmo.ToString();
            reloadText = "";
        }
        PauseManager.Instance.ChangeAmmoText(text, reloadText, rightHand);
    }

    internal static void UpdateUnlimitedAmmoHud(Weapon weapon)
    {
        if (PauseManager.Instance == null || weapon == null || !weapon.IsOwner
            || !weapon.needsAmmo || weapon.reloadWeapon || weapon.gameObject.layer != 8)
        {
            return;
        }

        int currentAmmo = IsReloading(weapon) ? 0 : Mathf.Max(0, weapon.currentAmmo);
        PauseManager.Instance.MoveAmmoDisplay(true, weapon.inRightHand);
        PauseManager.Instance.ChangeAmmoText("∞", currentAmmo + " / ", weapon.inRightHand);
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

        if (!enabled || !reloadPressed || wasReloadPressed || weapon.reloadWeapon || !weapon.IsOwner || weapon.gameObject.layer != 8 || memory.Reloading || (!memory.UnlimitedAmmo && memory.SpareRounds <= 0) || weapon.currentAmmo >= memory.MagazineSize)
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
        if (MemoryByWeapon.TryGetValue(weapon, out Memory memory))
        {
            memory.UnlimitedAmmo = false;
        }
        if (weapon.gameObject.layer == 8)
        {
            ReloadIfEmpty(weapon, spareMagazines);
        }
    }

    internal static void ApplyUnlimitedToWeapon(Weapon weapon, int magazineSizeOverride = 0)
    {
        if (weapon == null || !weapon.needsAmmo)
        {
            return;
        }

        InitializeUnlimited(weapon, magazineSizeOverride);
        if (weapon.gameObject.layer == 8)
        {
            ReloadIfEmpty(weapon, 0);
        }
    }

    private static void ReloadIfEmpty(Weapon weapon, int spareMagazines)
    {
        if (weapon == null || weapon.reloadWeapon || !weapon.needsAmmo)
        {
            return;
        }

        Initialize(weapon, spareMagazines);
        if (!MemoryByWeapon.TryGetValue(weapon, out Memory memory)
            || (!memory.UnlimitedAmmo && memory.SpareRounds <= 0))
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

        int rounds = memory.UnlimitedAmmo
            ? memory.MagazineSize
            : Mathf.Min(memory.MagazineSize, memory.SpareRounds);
        if (!memory.UnlimitedAmmo)
        {
            memory.SpareRounds -= rounds;
        }
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