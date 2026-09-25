using UnityEngine;

namespace Eights;

internal static class HardtatHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        float rotationRemaining = Mathf.Max(0f, HardtatState.ObjectiveDurationSeconds
            - HardtatState.ObjectiveElapsedSeconds);
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[HardtatState.TeamCount];
        for (int teamId = 0; teamId < HardtatState.TeamCount; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow(TeamDisplayNames.Get(teamId),
                HardtatState.GetScore(teamId), teamId);
        }

        return GameModeScoreboard.Build(GameMode.Hardtat, "HP",
            GameModeManager.EffectivePointsToWin, rows,
            "R/Timer: " + Mathf.CeilToInt(rotationRemaining) + "s / "
            + Mathf.CeilToInt(HardtatState.ContestTimeRemaining) + "s");
    }
}