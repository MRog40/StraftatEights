using System.Text;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HardpointHud
{
    internal static string BuildScoreboard()
    {
        StringBuilder text = new();
        text.AppendLine("<b>HARDPOINT</b>");
        for (int teamId = 0; teamId < HardpointState.TeamCount; teamId++)
        {
            TeamColorData color = TeamRules.GetColor(teamId);
            string hex = ColorUtility.ToHtmlStringRGB(new Color32(color.Red, color.Green,
                color.Blue, 255));
            text.Append("<color=#").Append(hex).Append(">TEAM ").Append(teamId + 1)
                .Append(": ").Append(HardpointState.GetScore(teamId)).AppendLine("</color>");
        }

        string control = HardpointState.CurrentController switch
        {
            -2 => "CONTESTED",
            -1 => "UNCONTROLLED",
            _ => "TEAM " + (HardpointState.CurrentController + 1)
        };
        text.Append(control).Append("  ")
            .Append(Mathf.CeilToInt(HardpointState.ContestTimeRemaining)).AppendLine("s");
        text.Append("POINT ").Append(HardpointState.CurrentObjectiveIndex + 1);
        if (HardpointState.IsWarningActive)
        {
            text.Append("  NEXT POINT SOON");
        }

        return text.ToString();
    }
}