using UnityEngine;

namespace Eights;

internal static class HardpointHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        float rotationRemaining = Mathf.Max(0f, HardpointState.ObjectiveDurationSeconds
            - HardpointState.ObjectiveElapsedSeconds);
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[HardpointState.TeamCount];
        for (int teamId = 0; teamId < HardpointState.TeamCount; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow(TeamDisplayNames.Get(teamId),
                HardpointState.GetScore(teamId), teamId);
        }

        return GameModeScoreboard.Build(GameMode.Hardpoint, "HP",
            GameModeManager.EffectivePointsToWin, rows,
            "R/Timer: " + Mathf.CeilToInt(rotationRemaining) + "s / "
            + Mathf.CeilToInt(HardpointState.ContestTimeRemaining) + "s");
    }
}