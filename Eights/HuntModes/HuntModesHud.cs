using System.Collections.Generic;
using UnityEngine;

namespace Eights;

internal static class HuntModesHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        GameModeScoreboardRow[] rows =
        {
            new GameModeScoreboardRow(TeamDisplayNames.Get(0), HuntModesState.GetScore(0), 0),
            new GameModeScoreboardRow(TeamDisplayNames.Get(1), HuntModesState.GetScore(1), 1)
        };
        string timerText = HuntModesState.IsTieBreakActive
            ? "HARDPOINT: " + Mathf.CeilToInt(HuntModesState.TieBreakHoldRemaining) + "s"
            : HuntModesState.TakeTimeRemaining > 0f
                ? "Take: " + Mathf.CeilToInt(HuntModesState.TakeTimeRemaining) + "s"
                : string.Empty;
        return GameModeScoreboard.Build(HuntModesState.Mode, null,
            GameModeManager.EffectivePointsToWin, rows, timerText);
    }
}
