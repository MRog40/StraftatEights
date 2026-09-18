using System;
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
    private const int NameGradientStartRed = 255;
    private const int NameGradientStartGreen = 255;
    private const int NameGradientStartBlue = 255;
    private const int NameGradientEndRed = 148;
    private const int NameGradientEndGreen = 196;
    private const int NameGradientEndBlue = 255;

    internal static string ApplyPlayerNameGradient(string playerName)
    {
        playerName = StripRichTextTags(playerName);
        if (playerName.Length == 0)
        {
            return playerName;
        }

        int visibleCharacterCount = CountVisibleCharacters(playerName);
        if (visibleCharacterCount == 0)
        {
            return playerName;
        }

        StringBuilder gradient = new(playerName.Length + visibleCharacterCount * 18);
        int visibleIndex = 0;
        int index = 0;
        while (index < playerName.Length)
        {
            int red = Interpolate(NameGradientStartRed, NameGradientEndRed,
                visibleIndex, visibleCharacterCount - 1);
            int green = Interpolate(NameGradientStartGreen, NameGradientEndGreen,
                visibleIndex, visibleCharacterCount - 1);
            int blue = Interpolate(NameGradientStartBlue, NameGradientEndBlue,
                visibleIndex, visibleCharacterCount - 1);
            gradient.Append("<color=#").Append(red.ToString("X2"))
                .Append(green.ToString("X2")).Append(blue.ToString("X2"))
                .Append(">").Append(playerName[index]).Append("</color>");
            visibleIndex++;
            index++;
        }

        return gradient.ToString();
    }

    internal static string StripRichTextTags(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder plainText = new(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] == '<')
            {
                int tagEnd = text.IndexOf('>', index);
                if (tagEnd >= index)
                {
                    index = tagEnd + 1;
                    continue;
                }
            }

            plainText.Append(text[index]);
            index++;
        }

        return plainText.ToString();
    }

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

    private static int CountVisibleCharacters(string text)
    {
        int count = 0;
        int index = 0;
        while (index < text.Length)
        {
            count++;
            index++;
        }

        return count;
    }

    private static int Interpolate(int start, int end, int index, int lastIndex)
    {
        if (lastIndex <= 0)
        {
            return start;
        }

        return start + (end - start) * index / lastIndex;
    }
}