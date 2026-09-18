using System.Collections.Generic;
using System.Text;

namespace StraftatEightsPlugin;

internal readonly struct GameModeScoreboardRow
{
    internal GameModeScoreboardRow(string label, int score, int teamId = -1, int playerId = -1)
    {
        Label = label;
        Score = score;
        TeamId = teamId;
        PlayerId = playerId;
    }

    internal string Label { get; }
    internal int Score { get; }
    internal int TeamId { get; }
    internal int PlayerId { get; }
}

internal static class GameModeScoreboard
{
    private const int ScoreColumnWidth = 4;
    private const string ScoreColumnGap = "  ";

    internal static string Build(GameMode mode, string? labelOverride, int? pointsToWin,
        IReadOnlyList<GameModeScoreboardRow> rows, string? timerText = null)
    {
        StringBuilder text = new(GameModeManager.GetScoreboardModeLabelMarkup(mode, labelOverride));
        if (pointsToWin.HasValue)
        {
            text.Append(" - ").Append(pointsToWin.Value);
        }

        foreach (GameModeScoreboardRow row in rows)
        {
            string score = row.Score.ToString().PadLeft(ScoreColumnWidth);
            text.Append('\n');
            text.Append(IsLocalRow(row) ? "> " : "  ");
            if (row.TeamId >= 0)
            {
                TeamColorData color = TeamRules.GetColor(row.TeamId);
                text.Append("<color=#").Append(color.Red.ToString("X2"))
                    .Append(color.Green.ToString("X2"))
                    .Append(color.Blue.ToString("X2"))
                    .Append(">");
            }

            text.Append(row.Label);
            if (row.TeamId >= 0)
            {
                text.Append("</color>");
            }

            text.Append(ScoreColumnGap).Append(score);
        }

        if (!string.IsNullOrEmpty(timerText))
        {
            text.Append("\n<i>").Append(timerText).Append("</i>");
        }

        return text.ToString();
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