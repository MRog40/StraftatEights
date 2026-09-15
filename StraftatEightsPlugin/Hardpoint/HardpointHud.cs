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
        text.Append(GameModeManager.GetModeLabelMarkup(GameMode.Hardpoint))
            .Append(" - ")
            .Append(GameModeManager.EffectivePointsToWin);
        for (int teamId = 0; teamId < HardpointState.TeamCount; teamId++)
        {
            TeamColorData color = TeamRules.GetColor(teamId);
            string hex = ColorUtility.ToHtmlStringRGB(new Color32(color.Red, color.Green,
                color.Blue, 255));
            text.Append("<color=#").Append(hex).Append(">TEAM ").Append(teamId + 1)
                .Append(": ").Append(HardpointState.GetScore(teamId))
                .Append("</color>\n");
        }

        text.Append("R/Timer: ").Append(Mathf.CeilToInt(rotationRemaining))
            .Append("s / ").Append(Mathf.CeilToInt(HardpointState.ContestTimeRemaining))
            .Append("s");
        return text.ToString();
    }
}