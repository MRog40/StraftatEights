using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;

namespace Eights;

internal static class TeamLayoutState
{
    private const string LayoutFileName = "Eights.TeamLayouts.cfg";
    private static readonly Layout?[] Layouts = new Layout?[4];
    private static bool _loaded;

    private sealed class Layout
    {
        internal int TeamCount;
        internal List<string> Players = new();
        internal string TeamOrder = string.Empty;
        internal int[] TeamSizes = Array.Empty<int>();
    }

    internal static bool TryApplySaved(IReadOnlyList<int> playerIds, int teamCount,
        out Dictionary<int, int> assignments)
    {
        assignments = new Dictionary<int, int>();
        Layout? layout = GetLayout(teamCount);
        if (layout == null)
        {
            return false;
        }

        Dictionary<string, int> livePlayers = new(StringComparer.Ordinal);
        foreach (int playerId in playerIds)
        {
            if (TryGetPlayerKey(playerId, out string playerKey))
            {
                livePlayers[playerKey] = playerId;
            }
        }

        List<int> teamSizes = new(new int[teamCount]);
        HashSet<int> assignedPlayerIds = new();
        List<int> order = ParseOrder(layout.TeamOrder);
        for (int orderIndex = 0; orderIndex < order.Count; orderIndex++)
        {
            int rosterIndex = order[orderIndex];
            if (rosterIndex < 0 || rosterIndex >= layout.Players.Count)
            {
                continue;
            }

            int teamId = GetTeamForOrderIndex(layout.TeamSizes, orderIndex);
            if (teamId < 0 || !livePlayers.TryGetValue(layout.Players[rosterIndex],
                    out int playerId))
            {
                continue;
            }

            if (assignedPlayerIds.Add(playerId))
            {
                assignments[playerId] = teamId;
                teamSizes[teamId]++;
            }
        }

        foreach (int playerId in playerIds.OrderBy(GetPlayerSortKey, StringComparer.Ordinal))
        {
            if (assignedPlayerIds.Contains(playerId))
            {
                continue;
            }

            int selectedTeam = FindSmallestTeam(teamSizes);
            assignments[playerId] = selectedTeam;
            teamSizes[selectedTeam]++;
            assignedPlayerIds.Add(playerId);
        }

        if (assignments.Count != playerIds.Distinct().Count())
        {
            assignments.Clear();
            return false;
        }

        SaveAssignments(assignments, teamCount);
        return true;
    }

    internal static void SaveAssignments(IReadOnlyDictionary<int, int> assignments, int teamCount)
    {
        if (teamCount < 2 || teamCount > 3 || assignments.Count == 0)
        {
            return;
        }

        EnsureLoaded();
        List<(string Key, int Team)> entries = new();
        HashSet<string> seenKeys = new(StringComparer.Ordinal);
        foreach (KeyValuePair<int, int> assignment in assignments)
        {
            if (assignment.Value < 0 || assignment.Value >= teamCount
                || !TryGetPlayerKey(assignment.Key, out string playerKey)
                || !seenKeys.Add(playerKey))
            {
                return;
            }

            entries.Add((playerKey, assignment.Value));
        }

        entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
        Dictionary<string, int> rosterIndices = new(StringComparer.Ordinal);
        List<string> players = new(entries.Count);
        for (int index = 0; index < entries.Count; index++)
        {
            rosterIndices[entries[index].Key] = index;
            players.Add(entries[index].Key);
        }

        int[] teamSizes = new int[teamCount];
        List<int> order = new(entries.Count);
        for (int teamId = 0; teamId < teamCount; teamId++)
        {
            foreach ((string key, int assignedTeam) in entries)
            {
                if (assignedTeam != teamId)
                {
                    continue;
                }

                order.Add(rosterIndices[key]);
                teamSizes[teamId]++;
            }
        }

        if (order.Count != entries.Count || entries.Count > 9)
        {
            return;
        }

        Layouts[teamCount] = new Layout
        {
            TeamCount = teamCount,
            Players = players,
            TeamOrder = string.Concat(order.Select(index => (index + 1).ToString(
                CultureInfo.InvariantCulture))),
            TeamSizes = teamSizes
        };
        WriteLayouts();
    }

    internal static bool TryGetPlayerKey(int playerId, out string playerKey)
    {
        playerKey = string.Empty;
        if (playerId < 0 || !ClientInstance.playerInstances.TryGetValue(playerId,
                out ClientInstance client) || client == null || !client
            || client.PlayerSteamID == 0)
        {
            return false;
        }

        playerKey = "steam:" + client.PlayerSteamID.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    private static Layout? GetLayout(int teamCount)
    {
        if (teamCount < 2 || teamCount > 3)
        {
            return null;
        }

        EnsureLoaded();
        Layout? layout = Layouts[teamCount];
        return layout != null && IsValid(layout) ? layout : null;
    }

    private static bool IsValid(Layout layout)
    {
        if (layout.TeamCount < 2 || layout.TeamCount > 3
            || layout.TeamSizes.Length != layout.TeamCount
            || layout.Players.Count == 0
            || layout.Players.Count > 9
            || layout.TeamSizes.Sum() != layout.Players.Count)
        {
            return false;
        }

        List<int> order = ParseOrder(layout.TeamOrder);
        return order.Count == layout.Players.Count
            && order.Distinct().Count() == order.Count
            && order.All(index => index >= 0 && index < layout.Players.Count);
    }

    private static List<int> ParseOrder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<int>();
        }

        if (value.Contains(','))
        {
            return value.Split(',').Select(part => int.TryParse(part,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
                    ? index - 1 : -1).ToList();
        }

        return value.Select(character => char.IsDigit(character) ? character - '1' : -1).ToList();
    }

    private static int GetTeamForOrderIndex(IReadOnlyList<int> teamSizes, int orderIndex)
    {
        int offset = 0;
        for (int teamId = 0; teamId < teamSizes.Count; teamId++)
        {
            if (orderIndex < offset + teamSizes[teamId])
            {
                return teamId;
            }

            offset += teamSizes[teamId];
        }

        return -1;
    }

    private static int FindSmallestTeam(IReadOnlyList<int> teamSizes)
    {
        int selectedTeam = 0;
        for (int teamId = 1; teamId < teamSizes.Count; teamId++)
        {
            if (teamSizes[teamId] < teamSizes[selectedTeam])
            {
                selectedTeam = teamId;
            }
        }

        return selectedTeam;
    }

    private static string GetPlayerSortKey(int playerId)
    {
        return TryGetPlayerKey(playerId, out string playerKey)
            ? playerKey
            : "player:" + playerId.ToString(CultureInfo.InvariantCulture);
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        try
        {
            string path = GetFilePath();
            if (!File.Exists(path))
            {
                return;
            }

            foreach (string line in File.ReadAllLines(path))
            {
                string[] fields = line.Split('|');
                if (fields.Length != 4 || !int.TryParse(fields[0], out int teamCount)
                    || teamCount < 2 || teamCount > 3)
                {
                    continue;
                }

                string[] players = fields[1].Split(',', StringSplitOptions.RemoveEmptyEntries);
                string[] sizes = fields[3].Split(',', StringSplitOptions.RemoveEmptyEntries);
                if (!int.TryParse(fields[0], out _)
                    || !sizes.All(value => int.TryParse(value, out _)))
                {
                    continue;
                }

                Layouts[teamCount] = new Layout
                {
                    TeamCount = teamCount,
                    Players = players.ToList(),
                    TeamOrder = fields[2],
                    TeamSizes = sizes.Select(value => int.Parse(value,
                        CultureInfo.InvariantCulture)).ToArray()
                };
            }
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogWarning("[Teams] Could not load saved team layouts: "
                + exception.GetBaseException().Message);
        }
    }

    private static void WriteLayouts()
    {
        try
        {
            StringBuilder contents = new();
            for (int teamCount = 2; teamCount <= 3; teamCount++)
            {
                Layout? layout = Layouts[teamCount];
                if (layout == null || !IsValid(layout))
                {
                    continue;
                }

                contents.Append(layout.TeamCount.ToString(CultureInfo.InvariantCulture));
                contents.Append('|');
                contents.Append(string.Join(',', layout.Players));
                contents.Append('|');
                contents.Append(layout.TeamOrder);
                contents.Append('|');
                contents.Append(string.Join(',', layout.TeamSizes));
                contents.AppendLine();
            }

            File.WriteAllText(GetFilePath(), contents.ToString());
        }
        catch (Exception exception)
        {
            Plugin.Logger.LogWarning("[Teams] Could not save team layouts: "
                + exception.GetBaseException().Message);
        }
    }

    private static string GetFilePath()
    {
        return Path.Combine(Paths.ConfigPath, LayoutFileName);
    }
}
