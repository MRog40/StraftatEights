using System;
using System.Collections.Generic;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class SafeSpawnService
{
    private const float EyeHeight = 1.4f;
    private const int RecentSpawnHistoryLength = 4;
    private const int DroppedWeaponLayer = 7;
    private const int SuppressionLayer = 17;
    private static readonly int LineOfSightMask = Physics.DefaultRaycastLayers
        & ~(1 << DroppedWeaponLayer)
        & ~(1 << SuppressionLayer);
    private static readonly Dictionary<int, List<TeamPoint>> RecentSpawnPositions = new();

    internal static void Reset()
    {
        RecentSpawnPositions.Clear();
    }

    internal static bool TryChoose(int playerId, IReadOnlyList<Vector3> candidatePositions,
        out Vector3 position)
    {
        position = default;
        if (playerId < 0 || candidatePositions.Count == 0
            || GameManager.Instance == null || !GameManager.Instance.IsServer)
        {
            return false;
        }

        bool isHardpoint = GameModeManager.IsActive(GameMode.Hardpoint);
        bool isCaptureTheFlag = GameModeManager.IsActive(GameMode.CaptureTheFlag);
        int teamId = -1;
        bool hasTeam = GameModeManager.IsTeamBased
            && TeamAssignment.TryGetTeamId(playerId, out teamId);
        List<TeamPoint> candidates = ToTeamPoints(candidatePositions);
        List<TeamPoint> teammates = new();
        List<PlayerHealth> enemies = new();

        foreach (ClientInstance client in ClientInstance.playerInstances.Values)
        {
            if (client == null || !client || client.PlayerId == playerId)
            {
                continue;
            }

            PlayerHealth? health = PlayerLookup.FindActivePlayerHealthById(client.PlayerId);
            if (health == null || !health.gameObject.activeInHierarchy || health.health <= 0f)
            {
                continue;
            }

            TeamPoint playerPosition = ToTeamPoint(health.transform.position);
            bool isTeammate = hasTeam
                && TeamAssignment.TryGetTeamId(client.PlayerId, out int otherTeamId)
                && otherTeamId == teamId;
            if (isTeammate)
            {
                teammates.Add(playerPosition);
            }
            else
            {
                enemies.Add(health);
            }
        }

        TeamPoint? objective = null;
        if (isHardpoint && HardpointState.TryGetCurrentObjective(out HardpointObjective point))
        {
            objective = ToTeamPoint(point.Position);
        }
        else if (isCaptureTheFlag
            && CaptureTheFlagState.TryGetEnemyFlagPosition(playerId, out Vector3 flagPosition))
        {
            objective = ToTeamPoint(flagPosition);
        }

        if (hasTeam && teamId >= 2 && enemies.Count == 0
            && GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition))
        {
            List<TeamPoint> origins = new();
            for (int originIndex = 0; originIndex < 2
                && originIndex < definition.TeamOrigins.Count; originIndex++)
            {
                origins.Add(ToTeamPoint(definition.TeamOrigins[originIndex]));
            }

            if (origins.Count > 0)
            {
                TeamPoint fallback = TeamRules.SelectFarthestFromOrigins(candidates, origins);
                position = new Vector3(fallback.X, fallback.Y, fallback.Z);
                RememberSpawn(playerId, fallback);
                return true;
            }
        }

        List<SpawnCandidate> scoredCandidates = new(candidates.Count);
        foreach (TeamPoint candidate in candidates)
        {
            List<SpawnThreat> threats = new(enemies.Count);
            foreach (PlayerHealth enemy in enemies)
            {
                threats.Add(new SpawnThreat(ToTeamPoint(enemy.transform.position),
                    HasLineOfSight(new Vector3(candidate.X, candidate.Y, candidate.Z), enemy)));
            }

            scoredCandidates.Add(new SpawnCandidate(candidate, threats));
        }

        RecentSpawnPositions.TryGetValue(playerId, out List<TeamPoint>? recentPositions);
        TeamPoint selected = SafeSpawnRules.SelectRandomized(scoredCandidates, teammates,
            objective, recentPositions ?? new List<TeamPoint>(),
            UnityEngine.Random.Range(0, int.MaxValue), out _);
        position = new Vector3(selected.X, selected.Y, selected.Z);
        RememberSpawn(playerId, selected);
        return true;
    }

    private static void RememberSpawn(int playerId, TeamPoint position)
    {
        if (!RecentSpawnPositions.TryGetValue(playerId, out List<TeamPoint>? recentPositions))
        {
            recentPositions = new List<TeamPoint>(RecentSpawnHistoryLength);
            RecentSpawnPositions[playerId] = recentPositions;
        }

        recentPositions.Add(position);
        if (recentPositions.Count > RecentSpawnHistoryLength)
        {
            recentPositions.RemoveAt(0);
        }
    }

    private static bool HasLineOfSight(Vector3 candidate, PlayerHealth enemy)
    {
        Vector3 origin = candidate + Vector3.up * EyeHeight;
        Vector3 target = enemy.transform.position + Vector3.up * EyeHeight;
        Vector3 delta = target - origin;
        float distance = delta.magnitude;
        if (distance <= 0.01f)
        {
            return true;
        }

        if (!Physics.Raycast(origin, delta / distance, out RaycastHit hit, distance,
            LineOfSightMask, QueryTriggerInteraction.Ignore))
        {
            return true;
        }

        return hit.collider != null && hit.collider.transform.root == enemy.transform.root;
    }

    private static List<TeamPoint> ToTeamPoints(IReadOnlyList<Vector3> positions)
    {
        List<TeamPoint> points = new(positions.Count);
        foreach (Vector3 position in positions)
        {
            points.Add(ToTeamPoint(position));
        }

        return points;
    }

    private static TeamPoint ToTeamPoint(Vector3 position)
    {
        return new TeamPoint(position.x, position.y, position.z);
    }
}