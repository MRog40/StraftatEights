using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Eights;

[HarmonyPatch]
internal static class PlayerRadarShotPatches
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        Type[] weaponTypes =
        {
            typeof(Gun),
            typeof(Shotgun),
            typeof(Minigun),
            typeof(BeamGun),
            typeof(LargeRaycastGun),
            typeof(ChargeGun),
            typeof(BumpGun),
            typeof(RepulsiveGun),
            typeof(DualLauncher)
        };

        foreach (Type weaponType in weaponTypes)
        {
            MethodBase? method = FishNetCompatibility.FindGeneratedMethod(weaponType,
                "RpcLogic___ShootObserversEffect_", candidate => candidate.ReturnType == typeof(void)
                    && candidate.GetParameters().Length == 0);
            if (method != null)
            {
                yield return method;
            }
        }
    }

    private static void Postfix(Weapon __instance)
    {
        if (__instance == null || __instance.IsOwner)
        {
            return;
        }

        FirstPersonController? controller = __instance.playerController;
        if (controller == null || !controller)
        {
            return;
        }

        PlayerHealth? health = controller.GetComponent<PlayerHealth>();
        int playerId = health?.playerValues?.playerClient?.PlayerId ?? -1;
        PlayerRadar.NotifyEnemyShot(playerId);
    }
}