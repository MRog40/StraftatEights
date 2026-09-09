using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

[HarmonyPatch(typeof(MeleeWeapon), "HitServer")]
internal static class MeleeWeapon_HotPotatoBatDamage_Patch
{
    private static void Prefix(MeleeWeapon __instance, PlayerHealth enemyHealth, string hitName,
        bool ___secondAttackPlaying, ref float ___baseAttackDamage, ref float ___secondAttackDamage,
        float ___headMultiplier)
    {
        if (!GameModeManager.IsActive(GameMode.HotPotato)
            || __instance == null
            || !__instance.name.StartsWith(HotPotatoState.BatWeaponName, System.StringComparison.Ordinal)
            || enemyHealth == null)
        {
            return;
        }

        float damage = Mathf.Max(0.01f, enemyHealth.fullHealth * 0.5f);
        if (hitName == "Head_Col" && ___headMultiplier > 0f)
        {
            damage /= ___headMultiplier;
        }

        if (___secondAttackPlaying)
        {
            ___secondAttackDamage = damage;
        }
        else
        {
            ___baseAttackDamage = damage;
        }
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
