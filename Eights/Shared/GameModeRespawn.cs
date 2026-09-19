using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Eights;

internal static class GameModeRespawn
{
    private static readonly HashSet<int> PendingManagers = new();
    private static readonly HashSet<int> PendingSpawnAdjustments = new();
    private static readonly HashSet<int> InitialTeamSpawnsApplied = new();
    private static readonly Dictionary<int, CosmeticIndices> PendingRespawnCosmetics = new();
    private static int _cachedCenterSceneHandle = -1;
    private static string _cachedCenterSceneName = string.Empty;
    private static Vector3 _cachedMapCenter;
    private static bool _hasCachedMapCenter;

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
        PendingSpawnAdjustments.Clear();
        InitialTeamSpawnsApplied.Clear();
        PendingRespawnCosmetics.Clear();
        SafeSpawnService.Reset();
        ClearMapCenterCache();
    }

    internal static void ResetForMatch()
    {
        PendingManagers.Clear();
        PendingSpawnAdjustments.Clear();
        InitialTeamSpawnsApplied.Clear();
        PendingRespawnCosmetics.Clear();
        SafeSpawnService.Reset();
        ClearMapCenterCache();
    }

    internal static void Schedule(PlayerManager manager, float delay, bool protectOnRespawn = true)
    {
        if (Plugin.Instance == null || !PendingManagers.Add(manager.GetInstanceID()))
        {
            return;
        }
        Plugin.Instance.StartCoroutine(RespawnAfterDelay(manager,
            GameModeManager.GetRespawnDelay(delay), 0,
            SessionState.Generation, GameModeManager.RoundId, protectOnRespawn));
    }

    internal static void Schedule(int playerId, float delay, bool protectOnRespawn = true)
    {
        if (Plugin.Instance == null || !PendingManagers.Add(playerId))
        {
            return;
        }
        Plugin.Instance.StartCoroutine(RespawnPlayerAfterDelay(playerId,
            GameModeManager.GetRespawnDelay(delay),
            SessionState.Generation, GameModeManager.RoundId, protectOnRespawn));
    }

    private static IEnumerator RespawnAfterDelay(PlayerManager manager, float delay, int attempt,
        int sessionGeneration, int roundId, bool protectOnRespawn)
    {
        int managerId = manager.GetInstanceID();
        yield return new WaitForSeconds(delay);
        if (!CanRespawnForActiveMode())
        {
            PendingManagers.Remove(managerId);
            yield break;
        }
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
                PrepareForRespawn(manager);
                int playerId = manager.player?.GetComponent<PlayerValues>()?.playerClient?.PlayerId ?? -1;
                if (protectOnRespawn)
                {
                    RespawnProtection.ArmForRespawn(playerId);
                }
                success = FishNetCompatibility.TryInvokeRespawn(manager);
                if (success)
                {
                    FinalizeRespawn(manager);
                    ClearSpawnAdjustment(manager);
                }
                else if (protectOnRespawn)
                {
                    RespawnProtection.CancelRespawn(playerId);
                }
            }
            catch (System.Exception exception)
            {
                if (protectOnRespawn)
                {
                    int playerId = manager.player?.GetComponent<PlayerValues>()?.playerClient?.PlayerId ?? -1;
                    RespawnProtection.CancelRespawn(playerId);
                }
                ClearSpawnAdjustment(manager);
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
                sessionGeneration, roundId, protectOnRespawn));
        }
    }

    private static IEnumerator RespawnPlayerAfterDelay(int playerId, float delay,
        int sessionGeneration, int roundId, bool protectOnRespawn)
    {
        yield return new WaitForSeconds(delay);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!CanRespawnForActiveMode())
            {
                PendingManagers.Remove(playerId);
                yield break;
            }

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
                    PrepareForRespawn(manager);
                    if (protectOnRespawn)
                    {
                        RespawnProtection.ArmForRespawn(playerId);
                    }
                    if (FishNetCompatibility.TryInvokeRespawn(manager))
                    {
                        FinalizeRespawn(manager);
                        ClearSpawnAdjustment(manager);
                        PendingManagers.Remove(playerId);
                        yield break;
                    }
                    if (protectOnRespawn)
                    {
                        RespawnProtection.CancelRespawn(playerId);
                    }
                }
                catch (System.Exception exception)
                {
                    if (protectOnRespawn)
                    {
                        RespawnProtection.CancelRespawn(playerId);
                    }
                    ClearSpawnAdjustment(manager);
                    Plugin.Logger.LogWarning($"[Respawn] player={playerId} attempt={attempt + 1} failed: {exception.GetBaseException().Message}");
                }
            }
            yield return new WaitForSeconds(0.25f);
        }
        PendingManagers.Remove(playerId);
        Plugin.Logger.LogWarning($"[Respawn] player={playerId} failed after 3 attempts");
    }

    private static void PrepareForRespawn(PlayerManager manager)
    {
        if (manager.waitForRoundStartCoroutine != null)
        {
            manager.StopCoroutine(manager.waitForRoundStartCoroutine);
            manager.waitForRoundStartCoroutine = null;
        }

        PendingSpawnAdjustments.Add(manager.GetInstanceID());
    }

    private static bool CanRespawnForActiveMode()
    {
        if (GameModeManager.IsActive(GameMode.CaptureTheFlag))
        {
            return CaptureTheFlagState.CanRespawn();
        }

        if (GameModeManager.IsActive(GameMode.Hardpoint))
        {
            return HardpointState.CanRespawn();
        }

        return true;
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

            if (!IsPlayerMovementLocked(manager))
            {
                yield return null;
                continue;
            }

            SetPlayerMovable(manager);
            yield return null;
        }
    }

    private static bool IsPlayerMovementLocked(PlayerManager manager)
    {
        if (manager.player == null || !manager.player)
        {
            return false;
        }

        return !manager.player.canMove || manager.player.startOfRound
            || (PauseManager.Instance != null && PauseManager.Instance.startRound);
    }

    private static void SetPlayerMovable(PlayerManager manager)
    {
        if (manager == null || !manager || manager.player == null || !manager.player)
        {
            return;
        }

        if (!manager.player.IsOwner)
        {
            return;
        }

        bool wasMovementLocked = !manager.player.canMove;
        manager.player.canMove = true;
        if (wasMovementLocked)
        {
            manager.player.sync___set_value_canMove(true, true);
        }
        manager.player.startOfRound = false;
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.startRound = false;
        }
    }

    internal static void SetPlayerMovable(FirstPersonController player)
    {
        if (player == null || !player)
        {
            return;
        }

        bool wasMovementLocked = !player.canMove;
        player.canMove = true;
        if (wasMovementLocked)
        {
            player.sync___set_value_canMove(true, true);
        }
        player.startOfRound = false;
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.startRound = false;
        }
    }

    internal static void EnforcePreRoundMovementLock()
    {
        if (!GameModeManager.IsPreRoundTimerActive)
        {
            return;
        }

        FirstPersonController? player = GetLocalPlayerController();
        if (player == null || !player || !player.IsOwner)
        {
            return;
        }

        player.canMove = false;
        player.startOfRound = true;
        if (PauseManager.Instance != null)
        {
            PauseManager.Instance.startRound = true;
        }
    }

    internal static void ReleasePreRoundMovementLock()
    {
        FirstPersonController? player = GetLocalPlayerController();
        if (player != null && player && player.IsOwner)
        {
            SetPlayerMovable(player);
        }
    }

    private static FirstPersonController? GetLocalPlayerController()
    {
        int playerId = ClientInstance.Instance?.PlayerId ?? -1;
        PlayerHealth? health = playerId >= 0
            ? PlayerLookup.FindActivePlayerHealthById(playerId)
            : null;
        return health?.controller;
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

    internal static void ApplySpawnFacing(PlayerManager manager, Vector3 position,
        ref Quaternion rotation)
    {
        int playerId = FindPlayerId(manager);
        if (!TryGetSpawnFacingTarget(playerId, position, out Vector3 target))
        {
            return;
        }

        Vector3 direction = target - position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.01f)
        {
            return;
        }

        rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private static bool TryGetSpawnFacingTarget(int playerId, Vector3 position,
        out Vector3 target)
    {
        target = default;
        if (GameModeManager.IsActive(GameMode.Hardpoint)
            && HardpointState.TryGetCurrentObjective(out HardpointObjective objective))
        {
            target = objective.Position;
            return true;
        }

        if (GameModeManager.IsActive(GameMode.CaptureTheFlag)
            && CaptureTheFlagState.TryGetEnemyFlagPosition(playerId, out target))
        {
            return true;
        }

        if (GameModeManager.IsActive(GameMode.SearchAndDestroy))
        {
            if (SearchAndDestroyState.TryGetBombPosition(out target))
            {
                return true;
            }

            if (TryGetNearestSearchAndDestroySite(position, out target))
            {
                return true;
            }
        }

        if (GameModeManager.IsTeamBased && TryGetTeammateCenter(playerId, out target))
        {
            return true;
        }

        return TryGetMapCenter(out target);
    }

    private static bool TryGetTeammateCenter(int playerId, out Vector3 center)
    {
        center = default;
        if (!TeamAssignment.TryGetTeamId(playerId, out int teamId))
        {
            return false;
        }

        int teammateCount = 0;
        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerId == playerId
                || !TeamAssignment.TryGetTeamId(client.PlayerId, out int otherTeamId)
                || otherTeamId != teamId)
            {
                continue;
            }

            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(client.PlayerId);
            if (health == null || !health || !health.gameObject.activeInHierarchy
                || health.health <= 0f)
            {
                continue;
            }

            center += health.transform.position;
            teammateCount++;
        }

        if (teammateCount == 0)
        {
            return false;
        }

        center /= teammateCount;
        return true;
    }

    private static bool TryGetNearestSearchAndDestroySite(Vector3 position,
        out Vector3 target)
    {
        target = default;
        float nearestDistance = float.MaxValue;
        bool found = false;
        for (int siteIndex = 0; siteIndex < 2; siteIndex++)
        {
            if (!SearchAndDestroyState.TryGetSitePosition(siteIndex, out Vector3 sitePosition))
            {
                continue;
            }

            Vector3 offset = sitePosition - position;
            offset.y = 0f;
            float distance = offset.sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                target = sitePosition;
                found = true;
            }
        }

        return found;
    }

    private static bool TryGetMapCenter(out Vector3 center)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (_hasCachedMapCenter && _cachedCenterSceneHandle == scene.handle
            && _cachedCenterSceneName == scene.name)
        {
            center = _cachedMapCenter;
            return true;
        }

        if (GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            && TryGetAverage(definition.SpawnPoints, out center))
        {
            CacheMapCenter(scene, center);
            return true;
        }

        GameObject? spawnRoot = FindSpawnRoot("Spawnpoints")
            ?? FindSpawnRoot("Spawnpoints4Player");
        SpawnPoint[] spawnPoints = spawnRoot == null
            ? System.Array.Empty<SpawnPoint>()
            : spawnRoot.GetComponentsInChildren<SpawnPoint>(true);
        if (spawnPoints.Length == 0)
        {
            center = default;
            return false;
        }

        center = default;
        foreach (SpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint != null && spawnPoint)
            {
                center += spawnPoint.transform.position;
            }
        }

        center /= spawnPoints.Length;
        CacheMapCenter(scene, center);
        return true;
    }

    private static bool TryGetAverage(IReadOnlyList<Vector3> positions, out Vector3 average)
    {
        average = default;
        if (positions.Count == 0)
        {
            return false;
        }

        foreach (Vector3 position in positions)
        {
            average += position;
        }

        average /= positions.Count;
        return true;
    }

    private static GameObject? FindSpawnRoot(string tag)
    {
        try
        {
            return GameObject.FindGameObjectWithTag(tag);
        }
        catch (UnityException)
        {
            return null;
        }
    }

    private static void CacheMapCenter(Scene scene, Vector3 center)
    {
        _cachedCenterSceneHandle = scene.handle;
        _cachedCenterSceneName = scene.name;
        _cachedMapCenter = center;
        _hasCachedMapCenter = true;
    }

    private static void ClearMapCenterCache()
    {
        _cachedCenterSceneHandle = -1;
        _cachedCenterSceneName = string.Empty;
        _cachedMapCenter = default;
        _hasCachedMapCenter = false;
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

        IReadOnlyList<Vector3> candidates;
        if (GameModeManager.IsTeamBased)
        {
            TeamAssignment.EnsureAssignedForActiveRound();
            if (!TeamAssignment.TryGetSpawnCandidates(playerId,
                out List<Vector3> teamCandidates))
            {
                return false;
            }

            candidates = teamCandidates;
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
        bool usesBackWallInitialSpawn = GameModeManager.IsActive(GameMode.TeamDeathmatch)
            || GameModeManager.IsActive(GameMode.Hardpoint)
            || GameModeManager.IsActive(GameMode.CaptureTheFlag);
        if (!usesBackWallInitialSpawn
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

    private static IReadOnlyList<Vector3> GetAvailableSpawnPositions()
    {
        return GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            ? definition.SpawnPoints
            : System.Array.Empty<Vector3>();
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
        ref Vector3 position, ref Quaternion rotation)
    {
        GameModeRespawn.ApplyRespawnCosmetics(__instance, ref suitIndex, ref cigIndex);
        if (SearchAndDestroyState.TryGetRoleSpawnPosition(__instance, out Vector3 rolePosition))
        {
            position = rolePosition;
        }
        else
        {
            bool hasPendingSpawnAdjustment = GameModeRespawn.ConsumeSpawnAdjustment(__instance);
            if (!hasPendingSpawnAdjustment
                && GameModeRespawn.TryChooseInitialTeamSpawnPosition(__instance,
                out Vector3 initialTeamPosition))
            {
                position = initialTeamPosition;
            }
            else if (GameModeRespawn.TryChooseSafeSpawnPosition(__instance,
                out Vector3 safePosition))
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

        GameModeRespawn.ApplySpawnFacing(__instance, position, ref rotation);
    }

    private static void Postfix(PlayerManager __instance)
    {
        PlayerLookup.RegisterSpawnedPlayer(__instance);
        TeamWeaponLoadouts.OnPlayerSpawned(__instance);
    }
}


[HarmonyPatch]
internal static class PlayerSetup_CustomRespawnMovement_Patch
{
    private static MethodBase? TargetMethod()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return typeof(PlayerSetup).GetMethod("OnStartClient___UserLogic", flags)
            ?? typeof(PlayerSetup).GetMethod("OnStartClient", flags);
    }

    private static bool Prepare() => TargetMethod() != null;

    private static void Postfix(PlayerSetup __instance)
    {
        FirstPersonController? player = __instance.GetComponent<FirstPersonController>();
        if (player != null && player.IsOwner && GameModeManager.Phase == GameModePhase.ActiveRound
            && GameModeManager.UsesSafeRespawn)
        {
            GameModeRespawn.SetPlayerMovable(player);
        }
    }
}

[HarmonyPatch(typeof(PlayerManager), "TryRespawn")]
internal static class PlayerManager_SearchAndDestroyRespawn_Patch
{
    private static bool Prefix(PlayerManager __instance)
    {
        if (GameModeManager.IsActive(GameMode.CaptureTheFlag)
            && !CaptureTheFlagState.CanRespawn())
        {
            return false;
        }

        if (GameModeManager.IsActive(GameMode.Hardpoint)
            && !HardpointState.CanRespawn())
        {
            return false;
        }

        if (!SearchAndDestroyState.CanRespawn(__instance))
        {
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(PlayerManager), "WaitForRoundStartCoroutineStart")]
internal static class PlayerManager_CustomRoundStartScreen_Patch
{
    private static bool Prefix(PlayerManager __instance)
    {
        if (!GameModeManager.IsCustomMode
            || GameModeManager.Phase != GameModePhase.ActiveRound)
        {
            return true;
        }

        GameModeRespawn.SetPlayerMovable(__instance.player);
        return false;
    }
}