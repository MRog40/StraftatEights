using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class GameModeRespawn
{
    private static readonly HashSet<int> PendingManagers = new();

    internal static bool AnyModeEnabled => GameModeManager.ActiveMode != GameMode.None;

    internal static void Schedule(PlayerManager manager, float delay)
    {
        if (Plugin.Instance == null || !PendingManagers.Add(manager.GetInstanceID()))
        {
            return;
        }
        Plugin.Instance.StartCoroutine(RespawnAfterDelay(manager, delay));
    }

    private static IEnumerator RespawnAfterDelay(PlayerManager manager, float delay)
    {
        yield return new WaitForSeconds(delay);
        PendingManagers.Remove(manager.GetInstanceID());
        if (manager != null)
        {
            manager.TryRespawn();
        }
    }

    internal static Transform ChooseDistantSpawn(Transform currentResult)
    {
        if (!AnyModeEnabled)
        {
            return currentResult;
        }

        SpawnPoint[] spawnPoints = Object.FindObjectsOfType<SpawnPoint>();
        Transform? best = null;
        float bestDistance = float.MinValue;
        foreach (SpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint == null || !spawnPoint.gameObject.activeInHierarchy)
            {
                continue;
            }

            float nearestPlayerDistance = float.MaxValue;
            foreach (ClientInstance client in ClientInstance.playerInstances.Values)
            {
                PlayerHealth? health = client == null ? null : client.GetComponent<PlayerHealth>();
                if (health == null || !health.gameObject.activeInHierarchy || health.health <= 0f)
                {
                    continue;
                }
                nearestPlayerDistance = Mathf.Min(nearestPlayerDistance,
                    Vector3.Distance(spawnPoint.transform.position, health.transform.position));
            }

            if (nearestPlayerDistance > bestDistance)
            {
                bestDistance = nearestPlayerDistance;
                best = spawnPoint.transform;
            }
        }

        return best ?? currentResult!;
    }

    internal static PlayerManager? FindManager(PlayerHealth health)
    {
        return health.playerValues?.playerClient?.PlayerSpawner;
    }
}

[HarmonyPatch(typeof(PlayerManager), "ReturnSpawnPoint")]
internal static class PlayerManager_DistantSpawn_Patch
{
    private static void Postfix(ref Transform __result)
    {
        __result = GameModeRespawn.ChooseDistantSpawn(__result);
    }
}