using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(FirstPersonController), "Update")]
[HarmonyPriority(Priority.First)]
internal static class FirstPersonController_VoidDeath_Patch
{
    private const float VoidDeathHeight = -300f;

    private static bool Prefix(FirstPersonController __instance, ref bool ___safeDeathFalling)
    {
        if (__instance == null || !__instance
            || !IsCustomRoundActive
            || ___safeDeathFalling
            || __instance.transform.position.y >= VoidDeathHeight)
        {
            return true;
        }

        return HandleDeath(__instance, ref ___safeDeathFalling);
    }

    internal static bool IsCustomRoundActive => GameModeManager.ActiveMode != GameMode.None
        && !GameModeManager.IsVanillaScene
        && GameModeManager.Phase == GameModePhase.ActiveRound;

    internal static bool HandleDeath(FirstPersonController controller, ref bool safeDeathFalling)
    {
        PlayerHealth? health = controller.GetComponent<PlayerHealth>();
        if (health == null || !health)
        {
            return true;
        }

        if (!health.IsServer && !controller.IsOwner)
        {
            return false;
        }

        if (controller.IsOwner)
        {
            health.fellVoid = true;
            if (!safeDeathFalling)
            {
                Settings.Instance.IncreaseSuicidesAmount();
            }
        }

        if (safeDeathFalling)
        {
            return false;
        }

        safeDeathFalling = true;
        float currentHealth = health.sync___get_value_health();
        if (currentHealth <= 0f)
        {
            return false;
        }

        health.fellVoid = true;
        if (health.IsServer)
        {
            if (!FishNetCompatibility.TryInvokeVoidDespawn(controller))
            {
                if (!FishNetCompatibility.TryRemoveHealth(health, currentHealth + 1f))
                {
                    safeDeathFalling = false;
                    return true;
                }

                health.ChangeKilledState(true);
            }
        }
        else
        {
            health.RemoveHealth(currentHealth + 1f);
            health.ChangeKilledState(true);
        }

        return false;
    }
}

[HarmonyPatch(typeof(FirstPersonController), "OnTriggerEnter")]
internal static class FirstPersonController_KillzDeath_Patch
{
    private static bool Prefix(FirstPersonController __instance, Collider col,
        ref bool ___safeDeathFalling)
    {
        if (__instance == null || !__instance || col == null || !col
            || !FirstPersonController_VoidDeath_Patch.IsCustomRoundActive
            || !col.CompareTag("Killz"))
        {
            return true;
        }

        return FirstPersonController_VoidDeath_Patch.HandleDeath(__instance, ref ___safeDeathFalling);
    }
}