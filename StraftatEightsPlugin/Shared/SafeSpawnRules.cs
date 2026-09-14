using System;
using System.Collections.Generic;

namespace StraftatEightsPlugin;

internal readonly struct SpawnThreat
{
    internal SpawnThreat(TeamPoint position, bool isVisible)
    {
        Position = position;
        IsVisible = isVisible;
    }

    internal TeamPoint Position { get; }
    internal bool IsVisible { get; }
}

internal readonly struct SpawnCandidate
{
    internal SpawnCandidate(TeamPoint position, IReadOnlyList<SpawnThreat> enemies)
    {
        Position = position;
        Enemies = enemies;
    }

    internal TeamPoint Position { get; }
    internal IReadOnlyList<SpawnThreat> Enemies { get; }
}

internal static class SafeSpawnRules
{
    internal const float EnemyDistanceRange = 50f;
    internal const float TeammateDistanceRange = 40f;
    internal const float ObjectiveDistanceRange = 60f;
    internal const float EnemyDistanceWeight = 0.5f;
    internal const float CoverWeight = 0.25f;
    internal const float TeammateWeight = 0.15f;
    internal const float ObjectiveWeight = 0.1f;

    internal static TeamPoint SelectBest(IReadOnlyList<SpawnCandidate> candidates,
        IReadOnlyList<TeamPoint> teammates,
        TeamPoint? objective, out float selectedScore)
    {
        if (candidates.Count == 0)
        {
            throw new ArgumentException("At least one spawn candidate is required.", nameof(candidates));
        }

        int selectedIndex = 0;
        selectedScore = Score(candidates[0], teammates, objective);
        for (int candidateIndex = 1; candidateIndex < candidates.Count; candidateIndex++)
        {
            float candidateScore = Score(candidates[candidateIndex], teammates, objective);
            if (candidateScore > selectedScore + 0.0001f)
            {
                selectedIndex = candidateIndex;
                selectedScore = candidateScore;
            }
        }

        return candidates[selectedIndex].Position;
    }

    internal static float Score(SpawnCandidate candidate,
        IReadOnlyList<TeamPoint> teammates, TeamPoint? objective)
    {
        float enemySafety = GetEnemySafety(candidate.Position, candidate.Enemies);
        float cover = GetCoverScore(candidate.Enemies);
        float teammateProximity = GetProximityScore(candidate.Position, teammates,
            TeammateDistanceRange);
        float objectiveProximity = objective.HasValue
            ? GetProximityScore(candidate.Position, new[] { objective.Value }, ObjectiveDistanceRange)
            : 0f;

        return enemySafety * EnemyDistanceWeight
            + cover * CoverWeight
            + teammateProximity * TeammateWeight
            + objectiveProximity * ObjectiveWeight;
    }

    private static float GetEnemySafety(TeamPoint candidate,
        IReadOnlyList<SpawnThreat> enemies)
    {
        if (enemies.Count == 0)
        {
            return 0f;
        }

        float nearestDistanceSquared = float.MaxValue;
        foreach (SpawnThreat enemy in enemies)
        {
            nearestDistanceSquared = Math.Min(nearestDistanceSquared,
                candidate.HorizontalDistanceSquared(enemy.Position));
        }

        return Clamp01((float)Math.Sqrt(nearestDistanceSquared) / EnemyDistanceRange);
    }

    private static float GetCoverScore(IReadOnlyList<SpawnThreat> enemies)
    {
        if (enemies.Count == 0)
        {
            return 0f;
        }

        int coveredEnemies = 0;
        foreach (SpawnThreat enemy in enemies)
        {
            if (!enemy.IsVisible)
            {
                coveredEnemies++;
            }
        }

        return (float)coveredEnemies / enemies.Count;
    }

    private static float GetProximityScore(TeamPoint candidate,
        IReadOnlyList<TeamPoint> targets, float distanceRange)
    {
        if (targets.Count == 0)
        {
            return 0f;
        }

        float nearestDistanceSquared = float.MaxValue;
        foreach (TeamPoint target in targets)
        {
            nearestDistanceSquared = Math.Min(nearestDistanceSquared,
                candidate.HorizontalDistanceSquared(target));
        }

        float distance = (float)Math.Sqrt(nearestDistanceSquared);
        return 1f - Clamp01(distance / distanceRange);
    }

    private static float Clamp01(float value)
    {
        return Math.Max(0f, Math.Min(1f, value));
    }
}