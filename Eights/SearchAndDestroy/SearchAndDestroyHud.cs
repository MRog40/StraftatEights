using UnityEngine;

namespace Eights;

internal static class SearchAndDestroyHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[2];
        for (int teamId = 0; teamId < 2; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow(TeamDisplayNames.Get(teamId),
                SearchAndDestroyState.GetScore(teamId), teamId);
        }

        bool bombPlanted = SearchAndDestroyState.BombStatus == SearchAndDestroyBombStatus.Planted;
        string timerText = bombPlanted
            ? "<color=#FF3B30><b>Bomb: "
                + Mathf.CeilToInt(SearchAndDestroyState.FuseTimeRemaining) + "s</b></color>"
            : "Take: " + Mathf.CeilToInt(SearchAndDestroyState.TakeTimeRemaining) + "s";
        return GameModeScoreboard.Build(GameMode.SearchAndDestroy, "SND",
            GameModeManager.EffectivePointsToWin, rows, timerText);
    }
}
