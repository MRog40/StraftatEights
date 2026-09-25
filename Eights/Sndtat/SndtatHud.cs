using UnityEngine;

namespace Eights;

internal static class SndtatHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        bool countertat = GameModeManager.IsActive(GameMode.Countertat);
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[2];
        for (int teamId = 0; teamId < 2; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow(TeamDisplayNames.Get(teamId,
                SndtatState.OffensiveTeamId, countertat), SndtatState.GetScore(teamId), teamId);
        }

        bool bombPlanted = SndtatState.BombStatus == SndtatBombStatus.Planted;
        string timerText = bombPlanted
            ? "<color=#FF3B30><b>Bomb: "
                + Mathf.CeilToInt(SndtatState.FuseTimeRemaining) + "s</b></color>"
            : "Take: " + Mathf.CeilToInt(SndtatState.TakeTimeRemaining) + "s";
        return GameModeScoreboard.Build(countertat ? GameMode.Countertat : GameMode.Sndtat,
            countertat ? "COUNTERTAT" : "SND",
            GameModeManager.EffectivePointsToWin, rows, timerText);
    }
}
