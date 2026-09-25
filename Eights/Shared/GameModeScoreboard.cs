using System;
using System.Collections.Generic;
using System.Text;

namespace Eights;

internal readonly struct GameModeScoreboardRow
{
    internal GameModeScoreboardRow(string label, int score, int teamId = -1, int playerId = -1,
        bool isSpecialPlayer = false)
    {
        Label = label;
        Score = score;
        TeamId = teamId;
        PlayerId = playerId;
        IsSpecialPlayer = isSpecialPlayer;
    }

    internal string Label { get; }
    internal int Score { get; }
    internal int TeamId { get; }
    internal int PlayerId { get; }
    internal bool IsSpecialPlayer { get; }
}

internal readonly struct GameModeScoreboardLayout
{
    internal GameModeScoreboardLayout(string nameColumnText, string scoreColumnText)
    {
        NameColumnText = nameColumnText;
        ScoreColumnText = scoreColumnText;
    }

    internal string NameColumnText { get; }
    internal string ScoreColumnText { get; }
}

internal static class GameModeScoreboard
{
    private const int MaxDisplayedModeLength = 12;

    internal static GameModeScoreboardLayout Build(GameMode mode, string? labelOverride, int? pointsToWin,
        IReadOnlyList<GameModeScoreboardRow> rows, string? timerText = null)
    {
        StringBuilder nameColumn = new();
        StringBuilder scoreColumn = new();
        nameColumn.Append(PlayerNameMarkup.Truncate(
            GameModeManager.GetScoreboardModeLabelMarkup(mode, labelOverride),
            MaxDisplayedModeLength));
        if (pointsToWin.HasValue)
        {
            nameColumn.Append(" - ").Append(pointsToWin.Value);
        }
        if (!string.IsNullOrEmpty(timerText))
        {
            nameColumn.Append("\n<i>").Append(timerText).Append("</i>");
            scoreColumn.Append('\n');
        }

        foreach (GameModeScoreboardRow row in rows)
        {
            nameColumn.Append('\n');
            if (row.IsSpecialPlayer)
            {
                nameColumn.Append(IsLocalRow(row) ? ">*" : "  *");
            }
            else
            {
                nameColumn.Append(IsLocalRow(row) ? "> " : "   ");
            }
            if (row.TeamId >= 0)
            {
                TeamColorData color = TeamColorPolicy.GetRelativeTeamColorData(row.TeamId);
                nameColumn.Append("<color=#").Append(color.Red.ToString("X2"))
                    .Append(color.Green.ToString("X2"))
                    .Append(color.Blue.ToString("X2"))
                    .Append(">");
            }

            nameColumn.Append(row.Label);
            if (row.TeamId >= 0)
            {
                nameColumn.Append("</color>");
            }

            scoreColumn.Append('\n');
            scoreColumn.Append(row.Score);
        }

        return new GameModeScoreboardLayout(nameColumn.ToString(), scoreColumn.ToString());
    }

    private static bool IsLocalTeam(int teamId)
    {
        int localPlayerId = ClientInstance.Instance == null ? -1 : ClientInstance.Instance.PlayerId;
        return TeamAssignment.TryGetTeamId(localPlayerId, out int localTeamId)
            && localTeamId == teamId;
    }

    private static bool IsLocalRow(GameModeScoreboardRow row)
    {
        return row.TeamId >= 0
            ? IsLocalTeam(row.TeamId)
            : row.PlayerId >= 0 && ClientInstance.Instance != null
                && ClientInstance.Instance.PlayerId == row.PlayerId;
    }

}