using UnityEngine;

namespace StraftatEightsPlugin;

internal static class SearchAndDestroyHud
{
    internal static string BuildScoreboard()
    {
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[2];
        for (int teamId = 0; teamId < 2; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow("TEAM " + (teamId + 1),
                SearchAndDestroyState.GetScore(teamId));
        }

        return GameModeScoreboard.Build(GameMode.SearchAndDestroy, "SND",
            GameModeManager.EffectivePointsToWin, rows,
            "Timer: " + Mathf.CeilToInt(SearchAndDestroyState.SubRoundTimeRemaining) + "s");
    }
}
