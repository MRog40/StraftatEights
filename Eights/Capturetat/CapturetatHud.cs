using UnityEngine;

namespace Eights;

internal static class CapturetatHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[2];
        for (int teamId = 0; teamId < 2; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow(TeamDisplayNames.Get(teamId),
                CapturetatState.GetScore(teamId), teamId);
        }

        return GameModeScoreboard.Build(GameMode.Capturetat, "CTF",
            GameModeManager.EffectivePointsToWin, rows,
            "Timer: " + Mathf.CeilToInt(CapturetatState.MatchTimeRemaining) + "s");
    }
}
