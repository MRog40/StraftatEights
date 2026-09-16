using System.Collections.Generic;
using System.Text;

namespace StraftatEightsPlugin;

internal readonly struct GameModeScoreboardRow
{
    internal GameModeScoreboardRow(string label, int score, int teamId = -1)
    {
        Label = label;
        Score = score;
        TeamId = teamId;
    }

    internal string Label { get; }
    internal int Score { get; }
    internal int TeamId { get; }
}

internal static class GameModeScoreboard
{
    private const int ScoreColumnWidth = 3;
    private const string ScoreColumnPosition = "90%";

    internal static string Build(GameMode mode, string? labelOverride, int? pointsToWin,
        IReadOnlyList<GameModeScoreboardRow> rows, string? timerText = null)
    {
        StringBuilder text = new(GameModeManager.GetModeLabelMarkup(mode, labelOverride));
        if (pointsToWin.HasValue)
        {
            text.Append(" - ").Append(pointsToWin.Value);
        }

        foreach (GameModeScoreboardRow row in rows)
        {
            string score = row.Score.ToString().PadLeft(ScoreColumnWidth);
            text.Append('\n');
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

            text.Append("<pos=")
                .Append(ScoreColumnPosition).Append(">")
                .Append(score);
        }

        if (!string.IsNullOrEmpty(timerText))
        {
            text.Append("\n<i>").Append(timerText).Append("</i>");
        }

        return text.ToString();
    }
}