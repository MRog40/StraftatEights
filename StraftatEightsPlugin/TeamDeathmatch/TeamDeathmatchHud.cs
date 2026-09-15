using System.Text;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class TeamDeathmatchHud
{
    internal static string BuildScoreboard()
    {
        StringBuilder text = new();
        text.Append(GameModeManager.GetModeLabelMarkup(GameMode.TeamDeathmatch))
            .Append("  First to ")
            .Append(GameModeManager.EffectivePointsToWin)
            .AppendLine();

        for (int teamId = 0; teamId < TeamDeathmatchState.TeamCount; teamId++)
        {
            TeamColorData color = TeamRules.GetColor(teamId);
            string hex = ColorUtility.ToHtmlStringRGB(new Color32(color.Red, color.Green,
                color.Blue, 255));
            text.Append("<color=#").Append(hex).Append(">TEAM ").Append(teamId + 1)
                .Append(": ").Append(TeamDeathmatchState.GetScore(teamId))
                .Append("/").Append(GameModeManager.EffectivePointsToWin)
                .AppendLine("</color>");
        }

        return text.ToString();
    }
}