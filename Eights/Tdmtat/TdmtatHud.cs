namespace Eights;

internal static class TdmtatHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[2];
        for (int teamId = 0; teamId < 2; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow(TeamDisplayNames.Get(teamId),
                TdmtatState.GetScore(teamId), teamId);
        }

        return GameModeScoreboard.Build(GameMode.Tdmtat, null,
            GameModeManager.EffectivePointsToWin, rows);
    }
}