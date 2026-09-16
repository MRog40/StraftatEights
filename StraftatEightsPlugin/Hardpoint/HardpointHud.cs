using UnityEngine;

namespace StraftatEightsPlugin;

internal static class HardpointHud
{
    internal static string BuildScoreboard()
    {
        float rotationRemaining = Mathf.Max(0f, HardpointState.ObjectiveDurationSeconds
            - HardpointState.ObjectiveElapsedSeconds);
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[HardpointState.TeamCount];
        for (int teamId = 0; teamId < HardpointState.TeamCount; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow("TEAM " + (teamId + 1),
                HardpointState.GetScore(teamId), teamId);
        }

        return GameModeScoreboard.Build(GameMode.Hardpoint, "HP",
            GameModeManager.EffectivePointsToWin, rows,
            "R/Timer: " + Mathf.CeilToInt(rotationRemaining) + "s / "
            + Mathf.CeilToInt(HardpointState.ContestTimeRemaining) + "s");
    }
}