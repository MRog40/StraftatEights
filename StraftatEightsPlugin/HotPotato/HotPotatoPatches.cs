using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(MeleeWeapon), "HitServer")]
internal static class MeleeWeapon_HotPotatoBatDamage_Patch
{
    private static void Prefix(MeleeWeapon __instance, PlayerHealth enemyHealth, ref float damageToGive)
    {
        if (!GameModeManager.IsActive(GameMode.HotPotato)
            || __instance == null
            || !__instance.name.StartsWith(HotPotatoState.BatWeaponName, System.StringComparison.Ordinal)
            || enemyHealth == null)
        {
            return;
        }

        damageToGive = Mathf.Max(0.01f, enemyHealth.fullHealth * 0.5f);
    }
}

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_HotPotatoSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.HotPotato)
            || WeaponService.IsFinalGameScreen || __instance.player == null)
        {
            return;
        }

        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null)
        {
            HotPotatoState.RequestLoadout(client.PlayerId);
        }
    }
}
