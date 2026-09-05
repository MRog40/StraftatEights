using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(ItemSpawner), "Spawn")]
internal static class ItemSpawner_GlobalWeapons_Patch
{
    private static bool Prefix(ItemSpawner __instance)
    {
        if (GameModeManager.ShouldIgnoreGlobalWeaponSettings) return false;
        if (!WeaponSettingsState.Enabled || WeaponSettingsState.Allowed.Count == 0) return true;
        if (WeaponSettingsState.Cycle) return false;
        GameObject? prefab = WeaponService.FindPrefab(WeaponSettingsState.Allowed[Random.Range(0, WeaponSettingsState.Allowed.Count)]);
        if (prefab != null) __instance.itemToSpawn = prefab;
        return true;
    }
}

[HarmonyPatch(typeof(Weapon), "WeaponUpdate")]
internal static class Weapon_GlobalReserveAmmo_Patch
{
    private static void Postfix(Weapon __instance)
    {
        if (GameModeManager.ShouldIgnoreGlobalWeaponSettingsFor(__instance))
        {
            return;
        }
        WeaponAmmoTuning.ApplyToWeapon(__instance, WeaponSettingsState.Enabled, WeaponSettingsState.SpareMagazines);
        WeaponAmmoTuning.TryStartManualReload(__instance, WeaponSettingsState.Enabled, WeaponSettingsState.SpareMagazines);
    }
}

[HarmonyPatch(typeof(ItemBehaviour), "Start")]
internal static class ItemBehaviour_GlobalWeaponsStart_Patch
{
    private static void Prefix(ItemBehaviour __instance)
    {
        if (GameModeManager.ShouldIgnoreGlobalWeaponSettings || !WeaponSettingsState.Enabled || !WeaponSettingsState.Cycle)
        {
            return;
        }

        __instance.dispenserStart = true;
        Rigidbody? body = __instance.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_GlobalWeaponsSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (GameModeManager.ShouldIgnoreGlobalWeaponSettings || !WeaponSettingsState.Enabled || !WeaponSettingsState.Cycle || __instance.player == null)
        {
            return;
        }
        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null && !JuggernautState.IsCurrentJuggernaut(__instance.player))
        {
            string? selectedWeapon = WeaponSettingsState.GetSelectedWeapon(client.PlayerId);
            if (selectedWeapon != null)
            {
                WeaponSettingsState.RequestLoadout(client.PlayerId, selectedWeapon);
            }
        }
    }
}

[HarmonyPatch(typeof(PlayerPickup), "RightHandFix")]
internal static class PlayerPickup_GlobalWeaponsHand_Patch
{
    private static void Prefix(PlayerPickup __instance)
    {
        if (!GameModeManager.ShouldIgnoreGlobalWeaponSettings && WeaponSettingsState.Enabled && WeaponSettingsState.Cycle)
        {
            WeaponService.AttachUnparentedWeapon(__instance);
        }
    }
}