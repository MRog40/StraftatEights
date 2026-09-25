namespace Eights;

internal static class TeamDeathmatchHud
{
    internal static GameModeScoreboardLayout BuildScoreboard()
    {
        GameModeScoreboardRow[] rows = new GameModeScoreboardRow[2];
        for (int teamId = 0; teamId < 2; teamId++)
        {
            rows[teamId] = new GameModeScoreboardRow(TeamDisplayNames.Get(teamId),
                TeamDeathmatchState.GetScore(teamId), teamId);
        }

        return GameModeScoreboard.Build(GameMode.TeamDeathmatch, null,
            GameModeManager.EffectivePointsToWin, rows);
    }
}