using System.Text;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class TeamDeathmatchHud
{
    internal static string BuildScoreboard()
    {
        StringBuilder text = new(GameModeManager.GetModeLabelMarkup(GameMode.TeamDeathmatch)
            + " - " + GameModeManager.EffectivePointsToWin);

        for (int teamId = 0; teamId < 2; teamId++)
        {
            TeamColorData color = TeamRules.GetColor(teamId);
            string hex = ColorUtility.ToHtmlStringRGB(new Color32(color.Red, color.Green,
                color.Blue, 255));
            text.Append("\n<color=#").Append(hex).Append("><b>TEAM ").Append(teamId + 1)
                .Append(": ").Append(TeamDeathmatchState.GetScore(teamId))
                .Append("</b></color>");
        }

        return text.ToString();
    }
}