using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(FirstPersonController), "Update")]
[HarmonyPriority(Priority.First)]
internal static class FirstPersonController_VoidDeath_Patch
{
    private const float VoidDeathHeight = -300f;
    private const float SafeHeight = -299f;

    private static bool Prefix(FirstPersonController __instance)
    {
        if (__instance == null || !__instance
            || GameModeManager.ActiveMode == GameMode.None
            || GameModeManager.IsVanillaScene
            || GameModeManager.Phase != GameModePhase.ActiveRound
            || __instance.transform.position.y >= VoidDeathHeight)
        {
            return true;
        }

        PlayerHealth? health = __instance.GetComponent<PlayerHealth>();
        if (health == null || !health)
        {
            return true;
        }

        if (!health.IsServer && !__instance.IsOwner)
        {
            return false;
        }

        float currentHealth = health.sync___get_value_health();
        if (currentHealth <= 0f)
        {
            return false;
        }

        health.fellVoid = true;
        float lethalDamage = currentHealth + 1f;
        if (health.IsServer)
        {
            if (!FishNetCompatibility.TryRemoveHealth(health, lethalDamage))
            {
                return true;
            }
        }
        else
        {
            health.RemoveHealth(lethalDamage);
        }

        health.ChangeKilledState(true);
        Vector3 safePosition = __instance.transform.position;
        safePosition.y = SafeHeight;
        __instance.transform.position = safePosition;
        return false;
    }
}