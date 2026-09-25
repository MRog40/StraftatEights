using System.Reflection;
using FishNet.Object;
using HarmonyLib;
using UnityEngine;

namespace Eights;

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_ChambertatSpawn_Patch
{
    private static void Postfix(PlayerManager __instance)
    {
        if (!GameModeManager.IsActive(GameMode.Chambertat)
            || WeaponService.IsFinalGameScreen || __instance.player == null)
        {
            return;
        }

        ClientInstance? client = __instance.GetComponent<ClientInstance>();
        if (client != null)
        {
            ChambertatState.RequestLoadout(client.PlayerId);
        }
    }
}

[HarmonyPatch]
internal static class MeleeWeapon_ChambertatKiller_Patch
{
    private static MethodBase? TargetMethod()
    {
        return FishNetCompatibility.FindGeneratedMethod(typeof(MeleeWeapon),
            "RpcLogic___KillServer_", method => method.ReturnType == typeof(void)
                && method.GetParameters() is { Length: 1 } parameters
                && parameters[0].ParameterType == typeof(PlayerHealth));
    }

    private static bool Prepare()
    {
        MethodBase? target = TargetMethod();
        if (target == null)
        {
            Plugin.Logger.LogError("[Chambertat] Could not find the generated MeleeWeapon kill method.");
            return false;
        }

        return true;
    }

    private static void Prefix(MeleeWeapon __instance, PlayerHealth enemyHealth)
    {
        if (!GameModeManager.IsActive(GameMode.Chambertat)
            || GameManager.Instance == null || !GameManager.Instance.IsServer
            || enemyHealth == null || !enemyHealth)
        {
            return;
        }

        int attackerId = FindAttackerId(__instance);
        if (attackerId >= 0)
        {
            ChambertatState.RecordMeleeKiller(enemyHealth, attackerId);
            PlayerHealth? attackerHealth = PlayerLookup.FindPlayerHealthById(attackerId);
            if (attackerHealth != null && attackerHealth)
            {
                enemyHealth.killer = attackerHealth.transform;
                return;
            }
        }

        GameObject? attackerRoot = __instance.rootObject;
        if (attackerRoot != null && attackerRoot)
        {
            enemyHealth.killer = attackerRoot.transform;
        }
    }

    private static int FindAttackerId(MeleeWeapon weapon)
    {
        NetworkObject? networkObject = weapon.GetComponent<NetworkObject>();
        int connectionId = networkObject?.Owner?.ClientId ?? -1;
        if (connectionId >= 0)
        {
            foreach (ClientInstance client in ClientInstance.playerInstances.Values)
            {
                if (client != null && client && client.ConnectionID == connectionId)
                {
                    return client.PlayerId;
                }
            }
        }

        int playerValueId = weapon.playerValues?.playerClient?.PlayerId ?? -1;
        if (playerValueId >= 0)
        {
            return playerValueId;
        }

        FirstPersonController? controller = weapon.playerController;
        PlayerHealth? controllerHealth = controller == null
            ? null
            : controller.GetComponent<PlayerHealth>();
        int controllerPlayerId = controllerHealth?.playerValues?.playerClient?.PlayerId ?? -1;
        if (controllerPlayerId >= 0)
        {
            return controllerPlayerId;
        }

        GameObject? attackerRoot = weapon.lastPlayerHolder ?? weapon.rootObject;
        if (attackerRoot != null && attackerRoot)
        {
            PlayerHealth? attackerHealth = attackerRoot.GetComponentInParent<PlayerHealth>();
            int rootPlayerId = attackerHealth?.playerValues?.playerClient?.PlayerId ?? -1;
            if (rootPlayerId >= 0)
            {
                return rootPlayerId;
            }

            ClientInstance? client = attackerRoot.GetComponentInParent<ClientInstance>();
            if (client != null && client && client.PlayerId >= 0)
            {
                return client.PlayerId;
            }
        }

        return -1;
    }
}
