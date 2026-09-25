using UnityEngine;

namespace Eights;

internal static class CaptureTheFlagHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[2];
        for (int teamId = 0; teamId < 2; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow(TeamDisplayNames.Get(teamId),
                CaptureTheFlagState.GetScore(teamId), teamId);
        }

        return GameModeScoreboard.Build(GameMode.CaptureTheFlag, "CTF",
            GameModeManager.EffectivePointsToWin, rows,
            "Timer: " + Mathf.CeilToInt(CaptureTheFlagState.MatchTimeRemaining) + "s");
    }
}
