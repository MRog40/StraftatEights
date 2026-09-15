using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class TeamAssignment
{
    private static readonly Dictionary<int, int> Assignments = new();
    private static readonly HashSet<int> InitialSpawnEligiblePlayers = new();

    internal static IReadOnlyDictionary<int, int> Current => Assignments;
    internal static int TeamCount { get; private set; }

    internal static void Reset()
    {
        Assignments.Clear();
        InitialSpawnEligiblePlayers.Clear();
        TeamCount = 0;
        if (GameManager.Instance != null && GameManager.Instance.IsServer)
        {
            GameManager.Instance.sync___set_value_playingTeams(false, true);
        }
    }

    internal static bool AssignForRound()
    {
        if (!MyceliumNetworking.MyceliumNetwork.IsHost)
        {
            return false;
        }

        Dictionary<int, int> nextAssignments =
            TeamRules.AssignBalanced(PlayerLookup.GetConnectedPlayerIds());
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

        TeamCount = TeamRules.GetTeamCount(Assignments.Count);
        ApplyNativeAssignments();
        return true;
    }

    internal static bool AssignCaptureTheFlagRound()
    {
        if (!MyceliumNetworking.MyceliumNetwork.IsHost)
        {
            return false;
        }

        List<int> playerIds = PlayerLookup.GetConnectedPlayerIds();
        if (playerIds.Count == 0)
        {
            return false;
        }

        Assignments.Clear();
        InitialSpawnEligiblePlayers.Clear();
        foreach (KeyValuePair<int, int> assignment
            in CaptureTheFlagRules.AssignStrictTwoTeams(playerIds))
        {
            Assignments[assignment.Key] = assignment.Value;
            InitialSpawnEligiblePlayers.Add(assignment.Key);
        }

        TeamCount = 2;
        ApplyNativeAssignments();
        return true;
    }

    internal static bool AssignSearchAndDestroyRound()
    {
        if (!MyceliumNetworking.MyceliumNetwork.IsHost)
        {
            return false;
        }

        List<int> playerIds = PlayerLookup.GetConnectedPlayerIds();
        if (playerIds.Count == 0)
        {
            return false;
        }

        Assignments.Clear();
        InitialSpawnEligiblePlayers.Clear();
        foreach (KeyValuePair<int, int> assignment
            in SearchAndDestroyRules.AssignStrictTwoTeams(playerIds))
        {
            Assignments[assignment.Key] = assignment.Value;
            InitialSpawnEligiblePlayers.Add(assignment.Key);
        }

        TeamCount = 2;
        ApplyNativeAssignments();
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
        ApplyNativeAssignments();
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
        else
        {
            AssignForRound();
        }
    }

    internal static void ApplySnapshot(string data, int teamCount)
    {
        if (teamCount < 2 || teamCount > 3)
        {
            return;
        }

        Dictionary<int, int> parsed = TeamRules.ParseAssignments(data, teamCount);
        Assignments.Clear();
        foreach (KeyValuePair<int, int> assignment in parsed)
        {
            Assignments[assignment.Key] = assignment.Value;
        }

        TeamCount = teamCount;
    }

    internal static bool TryGetTeamId(int playerId, out int teamId)
    {
        return Assignments.TryGetValue(playerId, out teamId);
    }

    internal static bool TryGetInitialSpawnPosition(int playerId, out Vector3 position)
    {
        position = default;
        if (!TryGetTeamId(playerId, out int teamId)
            || !InitialSpawnEligiblePlayers.Contains(playerId)
            || !GameModeManager.TryGetCurrentMapDefinition(out MapDefinition definition)
            || teamId < 0 || teamId >= 2 || teamId >= definition.TeamOrigins.Count)
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

        Vector3[] cardinalOffsets =
        {
            Vector3.forward * 0.5f,
            Vector3.right * 0.5f,
            Vector3.back * 0.5f,
            Vector3.left * 0.5f
        };
        position = definition.TeamOrigins[teamId]
            + cardinalOffsets[playerIndex % cardinalOffsets.Length];
        return true;
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

        if (teamId < 2 && teamId < definition.TeamOrigins.Count)
        {
            Vector3 origin = definition.TeamOrigins[teamId];
            List<Vector3> available = new(definition.SpawnPoints);
            List<Vector3> teamCandidates = new() { origin };
            Vector3 otherOrigin = definition.TeamOrigins[teamId == 0 ? 1 : 0];
            foreach (Vector3 candidate in available)
            {
                if (HorizontalDistanceSquared(candidate, origin)
                    <= HorizontalDistanceSquared(candidate, otherOrigin)
                    && HorizontalDistanceSquared(candidate, origin) > 0.01f)
                {
                    teamCandidates.Add(candidate);
                }
            }

            return teamCandidates;
        }

        return new List<Vector3>(definition.SpawnPoints);
    }

    private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
    {
        float x = first.x - second.x;
        float z = first.z - second.z;
        return x * x + z * z;
    }

    private static void ApplyNativeAssignments()
    {
        if (GameManager.Instance == null || !GameManager.Instance.IsServer
            || ScoreManager.Instance == null)
        {
            return;
        }

        GameManager.Instance.sync___set_value_playingTeams(true, true);
        foreach (KeyValuePair<int, int> assignment in Assignments)
        {
            ScoreManager.Instance.SetTeamId(assignment.Key, assignment.Value);
        }
    }

}