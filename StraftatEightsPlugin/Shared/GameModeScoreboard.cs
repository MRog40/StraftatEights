using System.Collections.Generic;
using System.Text;

namespace StraftatEightsPlugin;

internal readonly struct GameModeScoreboardRow
{
    internal GameModeScoreboardRow(string label, int score)
    {
        Label = label;
        Score = score;
    }

    internal string Label { get; }
    internal int Score { get; }
}

internal static class GameModeScoreboard
{
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
            text.Append('\n').Append(row.Label).Append("  ").Append(row.Score);
        }

        if (!string.IsNullOrEmpty(timerText))
        {
            text.Append("\n<i>").Append(timerText).Append("</i>");
        }

        return text.ToString();
    }
}