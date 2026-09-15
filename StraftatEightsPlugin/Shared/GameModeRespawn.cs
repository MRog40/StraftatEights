using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class GameModeRespawn
{
    private static readonly HashSet<int> PendingManagers = new();
    private static readonly HashSet<int> SuppressedRoundStarts = new();
    private static readonly HashSet<int> PendingSpawnAdjustments = new();
    private static readonly HashSet<int> InitialTeamSpawnsApplied = new();
    private static readonly Dictionary<int, CosmeticIndices> PendingRespawnCosmetics = new();

    private readonly struct CosmeticIndices
    {
        internal CosmeticIndices(int suitIndex, int cigaretteIndex)
        {
            SuitIndex = suitIndex;
            CigaretteIndex = cigaretteIndex;
        }

        internal int SuitIndex { get; }
        internal int CigaretteIndex { get; }
    }

    internal static void ResetForLobbyLeft()
    {
        PendingManagers.Clear();
        SuppressedRoundStarts.Clear();
        PendingSpawnAdjustments.Clear();
        InitialTeamSpawnsApplied.Clear();
        PendingRespawnCosmetics.Clear();
    }

    internal static void ResetForMatch()
    {
        PendingManagers.Clear();
        SuppressedRoundStarts.Clear();
        PendingSpawnAdjustments.Clear();
        InitialTeamSpawnsApplied.Clear();
        PendingRespawnCosmetics.Clear();
    }

    internal static void Schedule(PlayerManager manager, float delay)
    {
        if (Plugin.Instance == null || !PendingManagers.Add(manager.GetInstanceID()))
        {
            return;
        }
        Plugin.Instance.StartCoroutine(RespawnAfterDelay(manager, delay, 0,
            SessionState.Generation, GameModeManager.RoundId));
    }

    internal static void Schedule(int playerId, float delay)
    {
        if (Plugin.Instance == null || !PendingManagers.Add(playerId))
        {
            return;
        }
        Plugin.Instance.StartCoroutine(RespawnPlayerAfterDelay(playerId, delay,
            SessionState.Generation, GameModeManager.RoundId));
    }

    private static IEnumerator RespawnAfterDelay(PlayerManager manager, float delay, int attempt,
        int sessionGeneration, int roundId)
    {
        int managerId = manager.GetInstanceID();
        yield return new WaitForSeconds(delay);
        if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId)
        {
            PendingManagers.Remove(managerId);
            yield break;
        }
        bool success = false;
        if (manager != null)
        {
            try
            {
                CaptureRespawnCosmetics(manager);
                MarkRoundStartSuppressed(manager);
                success = FishNetCompatibility.TryInvokeRespawn(manager);
                if (success)
                {
                    FinalizeRespawn(manager);
                    ClearSpawnAdjustment(manager);
                }
            }
            catch (System.Exception exception)
            {
                ClearRoundStartSuppressed(manager);
                Plugin.Logger.LogWarning($"[Respawn] PlayerManager respawn failed: {exception.GetBaseException().Message}");
                success = false;
            }
        }
        PendingManagers.Remove(managerId);
        if (!success && attempt < 2 && Plugin.Instance != null && manager != null
            && SessionState.IsCurrent(sessionGeneration) && GameModeManager.RoundId == roundId)
        {
            PendingManagers.Add(managerId);
            Plugin.Instance.StartCoroutine(RespawnAfterDelay(manager, 0.25f, attempt + 1,
                sessionGeneration, roundId));
        }
    }

    private static IEnumerator RespawnPlayerAfterDelay(int playerId, float delay,
        int sessionGeneration, int roundId)
    {
        yield return new WaitForSeconds(delay);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId)
            {
                PendingManagers.Remove(playerId);
                yield break;
            }
            PlayerManager? manager = FindManagerByPlayerId(playerId);
            if (manager != null)
            {
                try
                {
                    CaptureRespawnCosmetics(manager);
                    MarkRoundStartSuppressed(manager);
                    if (FishNetCompatibility.TryInvokeRespawn(manager))
                    {
                        FinalizeRespawn(manager);
                        ClearSpawnAdjustment(manager);
                        PendingManagers.Remove(playerId);
                        yield break;
                    }
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

    internal static void CaptureRespawnCosmetics(PlayerManager manager)
    {
        PlayerSetup? playerSetup = manager.player?.GetComponent<PlayerSetup>();
        if (playerSetup != null)
        {
            PendingRespawnCosmetics[manager.GetInstanceID()] = new CosmeticIndices(
                playerSetup.mat, playerSetup.cig);
        }
    }

    internal static void ApplyRespawnCosmetics(PlayerManager manager, ref int suitIndex,
        ref int cigaretteIndex)
    {
        if (PendingRespawnCosmetics.TryGetValue(manager.GetInstanceID(), out CosmeticIndices cosmetics))
        {
            suitIndex = cosmetics.SuitIndex;
            cigaretteIndex = cosmetics.CigaretteIndex;
            PendingRespawnCosmetics.Remove(manager.GetInstanceID());
        }
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
        SetPlayerMovable(manager);
        if (GameManager.Instance != null && Plugin.Instance != null)
        {
            Plugin.Instance.StartCoroutine(KeepPlayerMovable(manager, 4f,
                SessionState.Generation, GameModeManager.RoundId));
        }
    }

    private static IEnumerator KeepPlayerMovable(PlayerManager manager, float duration,
        int sessionGeneration, int roundId)
    {
        float endTime = Time.time + duration;
        while (Time.time < endTime)
        {
            if (!SessionState.IsCurrent(sessionGeneration) || GameModeManager.RoundId != roundId)
            {
                yield break;
            }
            if (manager == null || !manager || manager.player == null || !manager.player)
            {
                yield break;
            }

            SetPlayerMovable(manager);
            yield return null;
        }
    }

    private static void SetPlayerMovable(PlayerManager manager)
    {
        if (manager == null || !manager || manager.player == null || !manager.player)
        {
            return;
        }

        manager.player.canMove = true;
        manager.player.sync___set_value_canMove(true, true);
        manager.player.startOfRound = false;
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.startRound = false;
        }
    }

    private static PlayerManager? FindManagerByPlayerId(int playerId)
    {
        return ClientInstance.playerInstances.TryGetValue(playerId, out ClientInstance client)
            ? client.PlayerSpawner
            : null;
    }

    private static int FindPlayerId(PlayerManager manager)
    {
        foreach (KeyValuePair<int, ClientInstance> entry in ClientInstance.playerInstances)
        {
            if (entry.Value != null && entry.Value && entry.Value.PlayerSpawner == manager)
            {
                return entry.Key;
            }
        }

        return -1;
    }

    internal static Transform ChooseDistantSpawn(Transform currentResult)
    {
        return currentResult;
    }

    internal static Vector3 ChooseSpawnPosition(Vector3 currentPosition)
    {
        if (TryChooseMapSpawnPosition(out Vector3 mapPosition))
        {
            return mapPosition;
        }

        return currentPosition;
    }

    internal static bool TryChooseSafeSpawnPosition(PlayerManager manager, out Vector3 position)
    {
        position = default;
        if (!GameModeManager.UsesSafeRespawn || GameManager.Instance == null
            || !GameManager.Instance.IsServer)
        {
            return false;
        }

        int playerId = FindPlayerId(manager);
        if (playerId < 0)
        {
            return false;
        }

        List<Vector3> candidates;
        if (GameModeManager.IsTeamBased)
        {
            TeamAssignment.EnsureAssignedForActiveRound();
            if (!TeamAssignment.TryGetSpawnCandidates(playerId, out candidates))
            {
                return false;
            }
        }
        else
        {
            candidates = GetAvailableSpawnPositions();
        }

        return SafeSpawnService.TryChoose(playerId, candidates, out position);
    }

    internal static bool TryChooseInitialTeamSpawnPosition(PlayerManager manager,
        out Vector3 position)
    {
        position = default;
        if (!GameModeManager.IsTeamBased
            || GameModeManager.IsActive(GameMode.CaptureTheFlag)
            || GameModeManager.IsActive(GameMode.SearchAndDestroy)
            || GameManager.Instance == null
            || !GameManager.Instance.IsServer)
        {
            return false;
        }

        int playerId = FindPlayerId(manager);
        if (playerId < 0 || InitialTeamSpawnsApplied.Contains(playerId)
            || !TeamAssignment.TryGetInitialSpawnPosition(playerId, out position))
        {
            return false;
        }

        InitialTeamSpawnsApplied.Add(playerId);
        return true;
    }

    private static List<Vector3> GetAvailableSpawnPositions()
    {
        return GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            ? new List<Vector3>(definition.SpawnPoints)
            : new List<Vector3>();
    }

    internal static bool TryChooseMapSpawnPosition(out Vector3 position)
    {
        position = default;
        if (!GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            || definition.SpawnPoints.Count == 0)
        {
            return false;
        }

        position = definition.SpawnPoints[UnityEngine.Random.Range(0, definition.SpawnPoints.Count)];
        return true;
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
    private static void Prefix(PlayerManager __instance, ref int suitIndex, ref int cigIndex,
        ref Vector3 position)
    {
        GameModeRespawn.ApplyRespawnCosmetics(__instance, ref suitIndex, ref cigIndex);
        if (SearchAndDestroyState.TryGetRoleSpawnPosition(__instance, out Vector3 rolePosition))
        {
            position = rolePosition;
            return;
        }
        bool hasPendingSpawnAdjustment = GameModeRespawn.ConsumeSpawnAdjustment(__instance);
        if (!hasPendingSpawnAdjustment
            && GameModeRespawn.TryChooseInitialTeamSpawnPosition(__instance,
            out Vector3 initialTeamPosition))
        {
            position = initialTeamPosition;
        }
        else if (GameModeRespawn.TryChooseSafeSpawnPosition(__instance, out Vector3 safePosition))
        {
            position = safePosition;
        }
        else if (GameModeRespawn.TryChooseMapSpawnPosition(out Vector3 mapPosition))
        {
            position = mapPosition;
        }
        else if (hasPendingSpawnAdjustment)
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

[HarmonyPatch(typeof(PlayerManager), "TryRespawn")]
internal static class PlayerManager_SearchAndDestroyRespawn_Patch
{
    private static bool Prefix(PlayerManager __instance)
    {
        if (!SearchAndDestroyState.CanRespawn(__instance))
        {
            return false;
        }

        return true;
    }
}