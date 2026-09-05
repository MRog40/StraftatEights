using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class GameModeRespawn
{
    private static readonly HashSet<int> PendingManagers = new();
    private static readonly MethodInfo? CmdRespawnLogic = typeof(PlayerManager).GetMethod(
        "RpcLogic___CmdRespawn_2166136261", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static bool AnyModeEnabled => GameModeManager.ActiveMode != GameMode.None;

    internal static void Schedule(PlayerManager manager, float delay)
    {
        if (Plugin.Instance == null || !PendingManagers.Add(manager.GetInstanceID()))
        {
            return;
        }
        Plugin.Instance.StartCoroutine(RespawnAfterDelay(manager, delay, 0));
    }

    internal static void Schedule(int playerId, float delay)
    {
        if (Plugin.Instance == null || !PendingManagers.Add(playerId))
        {
            return;
        }
        Plugin.Instance.StartCoroutine(RespawnPlayerAfterDelay(playerId, delay));
    }

    private static IEnumerator RespawnAfterDelay(PlayerManager manager, float delay, int attempt)
    {
        int managerId = manager.GetInstanceID();
        yield return new WaitForSeconds(delay);
        MethodInfo? respawnLogic = CmdRespawnLogic;
        bool success = false;
        if (respawnLogic != null && manager != null)
        {
            try
            {
                respawnLogic!.Invoke(manager, null);
                FinalizeRespawn(manager);
                success = true;
            }
            catch (System.Exception exception)
            {
                Plugin.Logger.LogWarning($"[Respawn] PlayerManager respawn failed: {exception.GetBaseException().Message}");
                success = false;
            }
        }
        PendingManagers.Remove(managerId);
        if (!success && attempt < 2 && Plugin.Instance != null && manager != null)
        {
            PendingManagers.Add(managerId);
            Plugin.Instance.StartCoroutine(RespawnAfterDelay(manager, 0.25f, attempt + 1));
        }
    }

    private static IEnumerator RespawnPlayerAfterDelay(int playerId, float delay)
    {
        yield return new WaitForSeconds(delay);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            PlayerManager? manager = FindManagerByPlayerId(playerId);
            if (manager != null && CmdRespawnLogic != null)
            {
                try
                {
                    CmdRespawnLogic.Invoke(manager, null);
                    FinalizeRespawn(manager);
                    PendingManagers.Remove(playerId);
                    yield break;
                }
                catch (System.Exception exception)
                {
                    Plugin.Logger.LogWarning($"[Respawn] player={playerId} attempt={attempt + 1} failed: {exception.GetBaseException().Message}");
                }
            }
            yield return new WaitForSeconds(0.25f);
        }
        PendingManagers.Remove(playerId);
        Plugin.Logger.LogWarning($"[Respawn] player={playerId} failed after 3 attempts");
    }

    private static void FinalizeRespawn(PlayerManager manager)
    {
        manager.SetPlayerMove(true);
        if (manager.player != null)
        {
            manager.player.sync___set_value_canMove(true, true);
            manager.player.startOfRound = false;
        }
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.startRound = false;
        }
        if (GameManager.Instance != null && Plugin.Instance != null)
        {
            Plugin.Instance.StartCoroutine(KeepPlayerMovable(manager, 4f));
        }
    }

    private static IEnumerator KeepPlayerMovable(PlayerManager manager, float duration)
    {
        float endTime = Time.time + duration;
        while (Time.time < endTime)
        {
            if (manager != null)
            {
                manager.SetPlayerMove(true);
                if (manager.player != null)
                {
                    manager.player.sync___set_value_canMove(true, true);
                    manager.player.startOfRound = false;
                }
                if (PauseManager.Instance != null)
                {
                    PauseManager.Instance.startRound = false;
                }
            }
            yield return null;
        }
    }

    private static PlayerManager? FindManagerByPlayerId(int playerId)
    {
        return ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client)
            ? client.PlayerSpawner
            : null;
    }

    internal static Transform ChooseDistantSpawn(Transform currentResult)
    {
        if (!AnyModeEnabled)
        {
            return currentResult;
        }

        SpawnPoint[] spawnPoints = FindFreeForAllSpawnPoints();
        Transform? best = null;
        float bestDistance = float.MinValue;
        foreach (SpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint == null || !spawnPoint.gameObject.activeInHierarchy)
            {
                continue;
            }

            int occupiedLayers = Physics.OverlapSphereNonAlloc(spawnPoint.transform.position, spawnPoint.Radius, null, 5);
            if (occupiedLayers > 0)
            {
                continue;
            }

            float nearestPlayerDistance = float.MaxValue;
            bool foundEnemy = false;
            foreach (ClientInstance client in ClientInstance.playerInstances.Values)
            {
                PlayerHealth? health = client == null ? null : client.GetComponent<PlayerHealth>();
                if (health == null || !health.gameObject.activeInHierarchy || health.health <= 0f)
                {
                    continue;
                }
                foundEnemy = true;
                nearestPlayerDistance = Mathf.Min(nearestPlayerDistance,
                    Vector3.Distance(spawnPoint.transform.position, health.transform.position));
            }

            if (foundEnemy && nearestPlayerDistance > bestDistance)
            {
                bestDistance = nearestPlayerDistance;
                best = spawnPoint.transform;
            }
        }

        return best ?? currentResult!;
    }

    private static SpawnPoint[] FindFreeForAllSpawnPoints()
    {
        GameObject? group = GameObject.FindGameObjectWithTag("Spawnpoints4Player");
        if (group == null)
        {
            group = GameObject.FindGameObjectWithTag("Spawnpoints");
        }
        SpawnPoint[] spawnPoints = group == null
            ? Object.FindObjectsOfType<SpawnPoint>()
            : group.GetComponentsInChildren<SpawnPoint>(true);
        return spawnPoints;
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