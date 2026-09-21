using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Eights;

internal static class TeamAssignment
{
    private static readonly Dictionary<int, int> Assignments = new();
    private static readonly Dictionary<int, int> PreviousAssignments = new();
    private static readonly HashSet<int> InitialSpawnEligiblePlayers = new();
    private static readonly System.Random TeamAssignmentRandom = new();

    internal static IReadOnlyDictionary<int, int> Current => Assignments;
    internal static int TeamCount { get; private set; }
    internal static int HealthCompensationVersion { get; private set; }

    internal static void Reset()
    {
        RememberCurrentAssignments();
        Assignments.Clear();
        InitialSpawnEligiblePlayers.Clear();
        TeamCount = 0;
        HealthCompensationVersion++;
        if (GameManager.Instance != null && GameManager.Instance.IsServer)
        {
            GameManager.Instance.sync___set_value_playingTeams(false, true);
            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.ResetTeams();
            }
        }
    }

    internal static void ResetDistributionHistory()
    {
        PreviousAssignments.Clear();
    }

    internal static bool AssignForRound()
    {
        if (!MyceliumNetworking.MyceliumNetwork.IsHost)
        {
            return false;
        }

        RememberCurrentAssignments();
        List<int> playerIds = PlayerLookup.GetConnectedPlayerIds();
        bool isTeamDeathmatch = GameModeManager.IsActive(GameMode.TeamDeathmatch);
        Dictionary<int, int> nextAssignments = GameModeManager.IsActive(GameMode.Hardpoint)
            ? TeamRules.AssignHardpointBalanced(playerIds, TeamAssignmentRandom,
                PreviousAssignments)
            : isTeamDeathmatch ? TeamRules.AssignTwoTeams(playerIds, TeamAssignmentRandom,
                PreviousAssignments)
            : TeamRules.AssignBalanced(playerIds, TeamAssignmentRandom, PreviousAssignments);
        if (nextAssignments.Count == 0)
        {
            return false;
        }

        Assignments.Clear();
        InitialSpawnEligiblePlayers.Clear();
        foreach (KeyValuePair<int, int> assignment in nextAssignments)
        {
            Assignments[assignment.Key] = assignment.Value;
            InitialSpawnEligiblePlayers.Add(assignment.Key);
        }

        TeamCount = GameModeManager.IsActive(GameMode.Hardpoint)
            ? TeamRules.GetHardpointTeamCount(Assignments.Count)
            : isTeamDeathmatch ? 2 : TeamRules.GetTeamCount(Assignments.Count);
        HealthCompensationVersion++;
        return true;
    }

    internal static bool AssignCaptureTheFlagRound()
    {
        if (!MyceliumNetworking.MyceliumNetwork.IsHost)
        {
            return false;
        }

        RememberCurrentAssignments();
        List<int> playerIds = PlayerLookup.GetConnectedPlayerIds();
        if (playerIds.Count == 0)
        {
            return false;
        }

        Assignments.Clear();
        InitialSpawnEligiblePlayers.Clear();
        foreach (KeyValuePair<int, int> assignment
            in TeamRules.AssignTwoTeams(playerIds, TeamAssignmentRandom, PreviousAssignments))
        {
            Assignments[assignment.Key] = assignment.Value;
            InitialSpawnEligiblePlayers.Add(assignment.Key);
        }

        TeamCount = 2;
        HealthCompensationVersion++;
        return true;
    }

    internal static bool AssignSearchAndDestroyRound()
    {
        if (!MyceliumNetworking.MyceliumNetwork.IsHost)
        {
            return false;
        }

        RememberCurrentAssignments();
        List<int> playerIds = PlayerLookup.GetConnectedPlayerIds();
        if (playerIds.Count == 0)
        {
            return false;
        }

        Assignments.Clear();
        InitialSpawnEligiblePlayers.Clear();
        foreach (KeyValuePair<int, int> assignment
            in TeamRules.AssignTwoTeams(playerIds, TeamAssignmentRandom, PreviousAssignments))
        {
            Assignments[assignment.Key] = assignment.Value;
            InitialSpawnEligiblePlayers.Add(assignment.Key);
        }

        TeamCount = 2;
        HealthCompensationVersion++;
        return true;
    }

    internal static bool AssignHuntersRound()
    {
        if (!MyceliumNetworking.MyceliumNetwork.IsHost)
        {
            return false;
        }

        RememberCurrentAssignments();
        List<int> playerIds = PlayerLookup.GetConnectedPlayerIds();
        if (playerIds.Count == 0)
        {
            return false;
        }

        Assignments.Clear();
        InitialSpawnEligiblePlayers.Clear();
        foreach (KeyValuePair<int, int> assignment
            in TeamRules.AssignTwoTeams(playerIds, TeamAssignmentRandom, PreviousAssignments))
        {
            Assignments[assignment.Key] = assignment.Value;
            InitialSpawnEligiblePlayers.Add(assignment.Key);
        }

        TeamCount = 2;
        HealthCompensationVersion++;
        return true;
    }

    internal static bool AssignLatePlayer(int playerId)
    {
        if (playerId < 0 || TeamCount < 2 || Assignments.ContainsKey(playerId))
        {
            return false;
        }

        int selectedTeam = 0;
        int smallestTeamSize = int.MaxValue;
        for (int teamId = 0; teamId < TeamCount; teamId++)
        {
            int teamSize = 0;
            foreach (int assignedTeamId in Assignments.Values)
            {
                if (assignedTeamId == teamId)
                {
                    teamSize++;
                }
            }

            if (teamSize < smallestTeamSize)
            {
                smallestTeamSize = teamSize;
                selectedTeam = teamId;
            }
        }

        Assignments[playerId] = selectedTeam;
        HealthCompensationVersion++;
        return true;
    }

    internal static bool RemovePlayer(int playerId)
    {
        if (!Assignments.Remove(playerId))
        {
            return false;
        }

        InitialSpawnEligiblePlayers.Remove(playerId);
        HealthCompensationVersion++;
        return true;
    }

    internal static void EnsureAssignedForActiveRound()
    {
        if (!GameModeManager.IsTeamBased || !MyceliumNetworking.MyceliumNetwork.IsHost
            || GameModeManager.Phase != GameModePhase.ActiveRound || Assignments.Count != 0)
        {
            return;
        }

        if (GameModeManager.IsActive(GameMode.CaptureTheFlag))
        {
            AssignCaptureTheFlagRound();
        }
        else if (GameModeManager.IsActive(GameMode.SearchAndDestroy))
        {
            AssignSearchAndDestroyRound();
        }
        else if (GameModeManager.IsHuntersActive)
        {
            AssignHuntersRound();
        }
        else
        {
            AssignForRound();
        }
    }

    private static void RememberCurrentAssignments()
    {
        foreach (KeyValuePair<int, int> assignment in Assignments)
        {
            PreviousAssignments[assignment.Key] = assignment.Value;
        }
    }

    internal static void ApplySnapshot(string data, int teamCount)
    {
        if (teamCount < 2 || teamCount > 3)
        {
            return;
        }

        Dictionary<int, int> parsed = TeamRules.ParseAssignments(data, teamCount);
        bool changed = TeamCount != teamCount || Assignments.Count != parsed.Count;
        if (!changed)
        {
            foreach (KeyValuePair<int, int> assignment in parsed)
            {
                if (!Assignments.TryGetValue(assignment.Key, out int currentTeamId)
                    || currentTeamId != assignment.Value)
                {
                    changed = true;
                    break;
                }
            }
        }

        Assignments.Clear();
        foreach (KeyValuePair<int, int> assignment in parsed)
        {
            Assignments[assignment.Key] = assignment.Value;
        }

        TeamCount = teamCount;
        if (changed)
        {
            HealthCompensationVersion++;
        }
    }

    internal static float GetHealthMultiplier(int playerId)
    {
        return GameModeManager.IsTeamBased
            ? TeamRules.GetTeamHealthMultiplier(Assignments, playerId)
            : 1f;
    }

    internal static int ResolveTeamId(int playerId)
    {
        return TeamRules.ResolveTeamId(Assignments, playerId);
    }

    internal static bool TryGetTeamId(int playerId, out int teamId)
    {
        if (Assignments.TryGetValue(playerId, out teamId))
        {
            return true;
        }

        teamId = -1;
        return false;
    }

    internal static bool TryGetTeamOrigin(MapDefinition definition, int teamId,
        out Vector3 origin)
    {
        origin = default;
        if (teamId < 0)
        {
            return false;
        }

        if (teamId < definition.TeamOrigins.Count)
        {
            origin = definition.TeamOrigins[teamId];
            return true;
        }

        if (teamId != 2 || definition.TeamOrigins.Count != 2
            || definition.SpawnPoints.Count == 0)
        {
            return false;
        }

        List<TeamPoint> candidates = definition.SpawnPoints
            .Select(ToTeamPoint)
            .ToList();
        List<TeamPoint> authoredOrigins = definition.TeamOrigins
            .Select(ToTeamPoint)
            .ToList();
        TeamPoint selected = TeamRules.SelectFarthestFromOrigins(candidates,
            authoredOrigins);
        origin = new Vector3(selected.X, selected.Y, selected.Z);
        return true;
    }

    internal static bool TryGetInitialSpawnPosition(int playerId, out Vector3 position)
    {
        position = default;
        if (!TryGetTeamId(playerId, out int teamId)
            || !InitialSpawnEligiblePlayers.Contains(playerId)
            || !GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            || !TryGetTeamOrigin(definition, teamId, out Vector3 teamOrigin))
        {
            return false;
        }

        List<int> teamPlayers = new();
        foreach (KeyValuePair<int, int> assignment in Assignments)
        {
            if (assignment.Value == teamId)
            {
                teamPlayers.Add(assignment.Key);
            }
        }

        teamPlayers.Sort();
        int playerIndex = teamPlayers.IndexOf(playerId);
        if (playerIndex < 0)
        {
            return false;
        }

        List<Vector3> candidates = GetInitialSpawnCandidates(definition, teamId);
        if (candidates.Count == 0)
        {
            return false;
        }

        position = candidates[playerIndex % candidates.Count];
        return true;
    }

    private static List<Vector3> GetInitialSpawnCandidates(MapDefinition definition, int teamId)
    {
        int activeTeamCount = TeamCount < 2 ? 2 : TeamCount;
        List<Vector3> teamOrigins = new(activeTeamCount);
        for (int originTeamId = 0; originTeamId < activeTeamCount; originTeamId++)
        {
            if (!TryGetTeamOrigin(definition, originTeamId, out Vector3 teamOrigin))
            {
                return new List<Vector3>();
            }

            teamOrigins.Add(teamOrigin);
        }

        if (teamId < 0 || teamId >= teamOrigins.Count)
        {
            return new List<Vector3>();
        }

        Vector3 ownOrigin = teamOrigins[teamId];
        List<Vector3> candidates = new();
        foreach (Vector3 candidate in definition.SpawnPoints)
        {
            if (HorizontalDistanceSquared(candidate, ownOrigin) <= 0.01f)
            {
                continue;
            }

            bool isClosestToOwnOrigin = true;
            for (int otherTeamId = 0; otherTeamId < teamOrigins.Count; otherTeamId++)
            {
                if (otherTeamId == teamId)
                {
                    continue;
                }

                float ownDistance = HorizontalDistanceSquared(candidate, ownOrigin);
                float otherDistance = HorizontalDistanceSquared(candidate,
                    teamOrigins[otherTeamId]);
                if (otherDistance < ownDistance
                    || (Mathf.Approximately(otherDistance, ownDistance)
                        && otherTeamId < teamId))
                {
                    isClosestToOwnOrigin = false;
                    break;
                }
            }

            if (isClosestToOwnOrigin)
            {
                candidates.Add(candidate);
            }
        }

        candidates.Sort((first, second) =>
        {
            int comparison = HorizontalDistanceSquared(first, ownOrigin)
                .CompareTo(HorizontalDistanceSquared(second, ownOrigin));
            if (comparison != 0)
            {
                return comparison;
            }

            return GetNearestOtherOriginDistanceSquared(second, teamOrigins, teamId)
                .CompareTo(GetNearestOtherOriginDistanceSquared(first, teamOrigins, teamId));
        });
        return candidates;
    }

    internal static bool TryGetSpawnCandidates(int playerId, out List<Vector3> candidates)
    {
        candidates = new List<Vector3>();
        if (!TryGetTeamId(playerId, out int teamId)
            || !GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition))
        {
            return false;
        }

        candidates = GetCandidates(definition, teamId);
        return candidates.Count > 0;
    }

    private static List<Vector3> GetCandidates(MapDefinition definition, int teamId)
    {
        if (GameModeManager.IsActive(GameMode.CaptureTheFlag))
        {
            Vector3 origin = definition.TeamOrigins[Mathf.Clamp(teamId, 0, 1)];
            int candidateCount = CaptureTheFlagRules.GetSpawnCandidateCount(
                definition.SpawnPoints.Count);
            return definition.SpawnPoints
                .OrderBy(position => HorizontalDistanceSquared(position, origin))
                .Take(candidateCount)
                .ToList();
        }

        int activeTeamCount = TeamCount < 2 ? 2 : TeamCount;
        List<Vector3> teamOrigins = new(activeTeamCount);
        for (int originTeamId = 0; originTeamId < activeTeamCount; originTeamId++)
        {
            if (!TryGetTeamOrigin(definition, originTeamId, out Vector3 candidateOrigin))
            {
                return new List<Vector3>(definition.SpawnPoints);
            }
            teamOrigins.Add(candidateOrigin);
        }

        if (teamId < 0 || teamId >= teamOrigins.Count)
        {
            return new List<Vector3>(definition.SpawnPoints);
        }

        Vector3 teamOrigin = teamOrigins[teamId];
        List<Vector3> teamCandidates = new() { teamOrigin };
        foreach (Vector3 candidate in definition.SpawnPoints)
        {
            float candidateDistance = HorizontalDistanceSquared(candidate, teamOrigin);
            if (candidateDistance <= 0.01f)
            {
                continue;
            }

            bool isClosestOrigin = true;
            for (int otherTeamId = 0; otherTeamId < teamOrigins.Count; otherTeamId++)
            {
                if (otherTeamId == teamId)
                {
                    continue;
                }

                float otherDistance = HorizontalDistanceSquared(candidate,
                    teamOrigins[otherTeamId]);
                if (otherDistance < candidateDistance
                    || (Mathf.Approximately(otherDistance, candidateDistance)
                        && otherTeamId < teamId))
                {
                    isClosestOrigin = false;
                    break;
                }
            }

            if (isClosestOrigin)
            {
                teamCandidates.Add(candidate);
            }
        }

        return teamCandidates;
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.x - second.x;
        float z = first.z - second.z;
        return x * x + z * z;
    }

    private static float GetNearestOtherOriginDistanceSquared(Vector3 position,
        IReadOnlyList<Vector3> teamOrigins, int teamId)
    {
        float nearestDistance = float.MaxValue;
        for (int originTeamId = 0; originTeamId < teamOrigins.Count; originTeamId++)
        {
            if (originTeamId == teamId)
            {
                continue;
            }

            nearestDistance = Mathf.Min(nearestDistance,
                HorizontalDistanceSquared(position, teamOrigins[originTeamId]));
        }

        return nearestDistance;
    }

    private static TeamPoint ToTeamPoint(Vector3 position)
    {
        return new TeamPoint(position.x, position.y, position.z);
    }

}