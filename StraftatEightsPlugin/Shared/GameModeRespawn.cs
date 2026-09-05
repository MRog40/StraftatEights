using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class GameModeRespawn
{
    private const float MinimumPlayerSeparation = 1.25f;
    private static readonly Vector3[] SpawnOffsets =
    {
        Vector3.zero,
        new Vector3(1f, 0f, 0f),
        new Vector3(-1f, 0f, 0f),
        new Vector3(0f, 0f, 1f),
        new Vector3(0f, 0f, -1f),
        new Vector3(0.7f, 0f, 0.7f),
        new Vector3(-0.7f, 0f, 0.7f),
        new Vector3(0.7f, 0f, -0.7f),
        new Vector3(-0.7f, 0f, -0.7f),
        new Vector3(1.5f, 0f, 0f),
        new Vector3(-1.5f, 0f, 0f),
        new Vector3(0f, 0f, 1.5f),
        new Vector3(0f, 0f, -1.5f)
    };
    private static readonly HashSet<int> PendingManagers = new();
    private static readonly HashSet<int> SuppressedRoundStarts = new();
    private static readonly HashSet<int> PendingSpawnAdjustments = new();
    private static readonly MethodInfo? CmdRespawnLogic = typeof(PlayerManager).GetMethod(
        "RpcLogic___CmdRespawn_2166136261", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static bool AnyModeEnabled => GameModeManager.IsCustomMode;

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
                MarkRoundStartSuppressed(manager);
                respawnLogic!.Invoke(manager, null);
                FinalizeRespawn(manager);
                ClearSpawnAdjustment(manager);
                success = true;
            }
            catch (System.Exception exception)
            {
                ClearRoundStartSuppressed(manager);
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
                    MarkRoundStartSuppressed(manager);
                    CmdRespawnLogic.Invoke(manager, null);
                    FinalizeRespawn(manager);
                    ClearSpawnAdjustment(manager);
                    PendingManagers.Remove(playerId);
                    yield break;
                }
                catch (System.Exception exception)
                {
                    ClearRoundStartSuppressed(manager);
                    Plugin.Logger.LogWarning($"[Respawn] player={playerId} attempt={attempt + 1} failed: {exception.GetBaseException().Message}");
                }
            }
            yield return new WaitForSeconds(0.25f);
        }
        PendingManagers.Remove(playerId);
        Plugin.Logger.LogWarning($"[Respawn] player={playerId} failed after 3 attempts");
    }

    internal static void MarkRoundStartSuppressed(PlayerManager manager)
    {
        SuppressedRoundStarts.Add(manager.GetInstanceID());
        PendingSpawnAdjustments.Add(manager.GetInstanceID());
    }

    internal static void ClearRoundStartSuppressed(PlayerManager manager)
    {
        SuppressedRoundStarts.Remove(manager.GetInstanceID());
        ClearSpawnAdjustment(manager);
    }

    internal static bool ConsumeRoundStartSuppressed(PlayerManager manager)
    {
        return SuppressedRoundStarts.Remove(manager.GetInstanceID());
    }

    internal static bool ConsumeSpawnAdjustment(PlayerManager manager)
    {
        return PendingSpawnAdjustments.Remove(manager.GetInstanceID());
    }

    private static void ClearSpawnAdjustment(PlayerManager manager)
    {
        PendingSpawnAdjustments.Remove(manager.GetInstanceID());
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
        List<Vector3> activePlayerPositions = GetActivePlayerPositions();
        if (activePlayerPositions.Count == 0)
        {
            return currentResult;
        }

        foreach (SpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint == null || !spawnPoint.gameObject.activeInHierarchy)
            {
                continue;
            }

            float nearestPlayerDistance = GetNearestPlayerDistance(spawnPoint.transform.position, activePlayerPositions);
            if (nearestPlayerDistance > bestDistance)
            {
                bestDistance = nearestPlayerDistance;
                best = spawnPoint.transform;
            }
        }

        return best ?? currentResult!;
    }

    internal static Vector3 ChooseSpawnPosition(Vector3 currentPosition)
    {
        if (!AnyModeEnabled)
        {
            return currentPosition;
        }

        List<Vector3> activePlayerPositions = GetActivePlayerPositions();
        SpawnPoint[] spawnPoints = FindFreeForAllSpawnPoints();
        Vector3 bestPosition = currentPosition;
        float bestDistance = float.MinValue;
        bool bestPositionCrowded = true;
        bool foundCandidate = false;

        foreach (SpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint == null || !spawnPoint.gameObject.activeInHierarchy)
            {
                continue;
            }

            foreach (Vector3 offset in SpawnOffsets)
            {
                Vector3 candidate = spawnPoint.transform.position + offset;
                float nearestPlayerDistance = GetNearestPlayerDistance(candidate, activePlayerPositions);
                bool candidateCrowded = nearestPlayerDistance < MinimumPlayerSeparation;
                if (!foundCandidate || IsBetterCandidate(candidateCrowded, nearestPlayerDistance, bestPositionCrowded, bestDistance))
                {
                    bestPosition = candidate;
                    bestDistance = nearestPlayerDistance;
                    bestPositionCrowded = candidateCrowded;
                    foundCandidate = true;
                }
            }
        }

        return bestPosition;
    }

    private static bool IsBetterCandidate(bool candidateCrowded, float candidateDistance, bool bestCrowded, float bestDistance)
    {
        if (candidateCrowded != bestCrowded)
        {
            return !candidateCrowded;
        }
        return candidateDistance > bestDistance;
    }

    private static List<Vector3> GetActivePlayerPositions()
    {
        List<Vector3> positions = new();
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            PlayerHealth? health = client == null ? null : PlayerLookup.FindPlayerHealthById(client.PlayerId);
            if (health == null || !health.gameObject.activeInHierarchy || health.health <= 0f)
            {
                continue;
            }
            positions.Add(health.transform.position);
        }
        return positions;
    }

    private static float GetNearestPlayerDistance(Vector3 position, List<Vector3> playerPositions)
    {
        if (playerPositions.Count == 0)
        {
            return float.MaxValue;
        }

        float nearestDistance = float.MaxValue;
        foreach (Vector3 playerPosition in playerPositions)
        {
            Vector3 horizontalDelta = position - playerPosition;
            horizontalDelta.y = 0f;
            nearestDistance = Mathf.Min(nearestDistance, horizontalDelta.magnitude);
        }
        return nearestDistance;
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

[HarmonyPatch(typeof(PlayerManager), "SpawnPlayer", new[] { typeof(int), typeof(int), typeof(Vector3), typeof(Quaternion) })]
internal static class PlayerManager_CustomRespawnSpawn_Patch
{
    private static void Prefix(PlayerManager __instance, ref Vector3 position)
    {
        if (GameModeRespawn.ConsumeSpawnAdjustment(__instance))
        {
            position = GameModeRespawn.ChooseSpawnPosition(position);
        }
    }
}

[HarmonyPatch(typeof(PlayerManager), "WaitForRoundStartCoroutineStart")]
internal static class PlayerManager_CustomRespawnRoundStart_Patch
{
    private static bool Prefix(PlayerManager __instance)
    {
        return !GameModeRespawn.ConsumeRoundStartSuppressed(__instance);
    }
}