using System.Text;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class CaptureTheFlagHud
{
    internal static string BuildScoreboard()
    {
        StringBuilder text = new();
        text.Append("<b>CTF</b>");

        for (int teamId = 0; teamId < 2; teamId++)
        {
            TeamColorData teamColor = TeamRules.GetColor(teamId);
            string color = ColorUtility.ToHtmlStringRGB(new Color32(teamColor.Red,
                teamColor.Green, teamColor.Blue, 255));
            text.Append("\n<color=#").Append(color).Append(">")
                .Append("TEAM ").Append(teamId + 1).Append(": ")
                .Append(CaptureTheFlagState.GetScore(teamId)).Append("</color>");
        }

        return text.ToString();
    }
}
