using System.Collections.Generic;
using UnityEngine;

namespace Eights;

internal static class HuntersHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        GameModeScoreboardRow[] rows =
        {
            new GameModeScoreboardRow(HuntersState.TeamZeroName, HuntersState.GetScore(0), 0),
            new GameModeScoreboardRow(HuntersState.TeamOneName, HuntersState.GetScore(1), 1)
        };
        string timerText = HuntersState.IsTieBreakActive
            ? "HARDPOINT: " + Mathf.CeilToInt(HuntersState.TieBreakHoldRemaining) + "s"
            : HuntersState.TakeTimeRemaining > 0f
                ? "Take: " + Mathf.CeilToInt(HuntersState.TakeTimeRemaining) + "s"
                : string.Empty;
        return GameModeScoreboard.Build(HuntersState.Mode, null,
            GameModeManager.EffectivePointsToWin, rows, timerText);
    }
}
