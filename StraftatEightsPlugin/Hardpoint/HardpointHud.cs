using System.Text;
using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HardpointHud
{
    internal static string BuildScoreboard()
    {
        StringBuilder text = new();
        float rotationRemaining = Mathf.Max(0f, HardpointState.ObjectiveDurationSeconds
            - HardpointState.ObjectiveElapsedSeconds);
        text.Append("HP rotates in ").Append(Mathf.CeilToInt(rotationRemaining))
            .AppendLine("s");
        text.Append("Timer ").Append(Mathf.CeilToInt(HardpointState.ContestTimeRemaining))
            .AppendLine("s");
        for (int teamId = 0; teamId < HardpointState.TeamCount; teamId++)
        {
            TeamColorData color = TeamRules.GetColor(teamId);
            string hex = ColorUtility.ToHtmlStringRGB(new Color32(color.Red, color.Green,
                color.Blue, 255));
            text.Append("<color=#").Append(hex).Append(">TEAM ").Append(teamId + 1)
                .Append(": ").Append(HardpointState.GetScore(teamId)).AppendLine("</color>");
        }
        return text.ToString();
    }
}