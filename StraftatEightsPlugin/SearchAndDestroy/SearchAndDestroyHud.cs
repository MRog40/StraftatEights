using System.Text;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyHud
{
    internal static string BuildScoreboard()
    {
        StringBuilder text = new();
        text.Append(GameModeManager.GetModeLabelMarkup(GameMode.SearchAndDestroy))
            .Append(" - ")
            .Append(GameModeManager.EffectivePointsToWin);

        for (int teamId = 0; teamId < 2; teamId++)
        {
            TeamColorData colorData = TeamRules.GetColor(teamId);
            string color = ColorUtility.ToHtmlStringRGB(new Color32(colorData.Red,
                colorData.Green, colorData.Blue, 255));
            text.Append("\n<color=#").Append(color).Append("><b>")
                .Append("TEAM ").Append(teamId + 1).Append("</b></color>: ")
                .Append(SearchAndDestroyState.GetScore(teamId));
        }

        text.Append("\nTimer: ")
            .Append(Mathf.CeilToInt(SearchAndDestroyState.SubRoundTimeRemaining))
            .Append("s");
        return text.ToString();
    }
}
